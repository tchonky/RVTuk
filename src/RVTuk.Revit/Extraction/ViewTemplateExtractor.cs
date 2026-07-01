using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using RVTuk.Core.Comparison;
using RVTuk.Core.Models.Comparison;

namespace RVTuk.Revit.Extraction
{
    /// <summary>
    /// Captures every view template in a document into a serializable snapshot. Inclusion-aware:
    /// records which parameters the template actually controls (GetNonControlledTemplateParameterIds).
    ///
    /// NOTE: this touches a lot of Revit API surface and can only be runtime-verified inside Revit.
    /// It is written defensively (per-template try/catch) so one bad template never aborts a capture.
    /// </summary>
    public class ViewTemplateExtractor : ICategoryExtractor
    {
        public string CategoryId => ViewTemplatesSnapshot.Category;

        // Well-known view parameters mapped to the Core canonical keys the scorer/comparer use.
        private static readonly Dictionary<long, (string Key, string Label, BuiltInParameter Bip)> KnownParams =
            new Dictionary<long, (string, string, BuiltInParameter)>
            {
                [Raw(BuiltInParameter.VIEW_SCALE)]        = (ViewTemplateFields.Scale, "View Scale", BuiltInParameter.VIEW_SCALE),
                [Raw(BuiltInParameter.VIEW_DETAIL_LEVEL)] = (ViewTemplateFields.DetailLevel, "Detail Level", BuiltInParameter.VIEW_DETAIL_LEVEL),
                [Raw(BuiltInParameter.VIEW_DISCIPLINE)]   = (ViewTemplateFields.Discipline, "Discipline", BuiltInParameter.VIEW_DISCIPLINE),
                [Raw(BuiltInParameter.VIEW_PHASE)]        = (ViewTemplateFields.Phase, "Phase", BuiltInParameter.VIEW_PHASE),
                [Raw(BuiltInParameter.VIEW_PHASE_FILTER)] = (ViewTemplateFields.PhaseFilter, "Phase Filter", BuiltInParameter.VIEW_PHASE_FILTER),
            };

        public CategorySnapshot Extract(Document doc)
        {
            var snap = new ViewTemplatesSnapshot();
            var templates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate);

            foreach (var v in templates)
            {
                try { snap.Templates.Add(ExtractOne(doc, v)); }
                catch { /* skip a malformed template; never abort the whole capture */ }
            }
            return snap;
        }

        private static ViewTemplateDto ExtractOne(Document doc, View v)
        {
            var dto = new ViewTemplateDto
            {
                Name = v.Name,
                UniqueId = v.UniqueId,
                ViewType = v.ViewType.ToString(),
            };

            // Map this view's parameters by id so we can label/read non-built-in ones.
            var byId = new Dictionary<long, Parameter>();
            foreach (Parameter p in v.Parameters)
            {
                var raw = Raw(p.Id);
                if (!byId.ContainsKey(raw)) byId[raw] = p;
            }

            var nonControlled = new HashSet<ElementId>(v.GetNonControlledTemplateParameterIds());
            foreach (var id in v.GetTemplateParameterIds())
            {
                bool controlled = !nonControlled.Contains(id);
                var raw = Raw(id);

                string key, label;
                string? value = null;

                if (KnownParams.TryGetValue(raw, out var known))
                {
                    key = known.Key;
                    label = known.Label;
                    if (controlled) value = ReadValue(v, known.Bip);
                }
                else if (byId.TryGetValue(raw, out var p))
                {
                    key = p.Definition?.Name ?? raw.ToString();
                    label = key;
                    if (controlled) value = ValueString(p);
                }
                else
                {
                    key = raw.ToString();
                    label = key;
                }

                dto.Included.Add(new ControlledParam(key, label, controlled));
                if (controlled && !string.IsNullOrEmpty(value))
                    dto.Settings.Add(new KvPair(key, value!));
            }

            dto.FilterNames.AddRange(GetFilterNames(doc, v));
            dto.CategoryOverridesHash = ComputeOverridesHash(doc, v);
            return dto;
        }

        private static List<string> GetFilterNames(Document doc, View v)
        {
            var names = new List<string>();
            try
            {
                foreach (var fid in v.GetFilters())
                {
                    var fe = doc.GetElement(fid);
                    if (fe != null) names.Add(fe.Name);
                }
            }
            catch { /* some view types don't support filters */ }
            return names;
        }

        /// <summary>Digest of the per-category visibility/graphic overrides. Empty string when the
        /// template applies no category overrides, so completeness scoring treats "no V/G" as 0.</summary>
        private static string ComputeOverridesHash(Document doc, View v)
        {
            var sb = new StringBuilder();
            foreach (Category cat in doc.Settings.Categories)
            {
                try
                {
                    var cid = cat.Id;
                    bool hidden = v.GetCategoryHidden(cid);
                    var ogs = v.GetCategoryOverrides(cid);
                    if (hidden || IsOverride(ogs))
                    {
                        sb.Append(cat.Name).Append(':').Append(hidden ? '1' : '0');
                        if (ogs != null)
                            sb.Append(',').Append(ogs.Halftone ? 'h' : '_').Append(ogs.Transparency)
                              .Append('/').Append(ogs.ProjectionLineWeight).Append('/').Append(ogs.CutLineWeight);
                        sb.Append(';');
                    }
                }
                catch { /* category not controllable in this view */ }
            }
            return sb.Length == 0 ? "" : Sha1(sb.ToString());
        }

        private static bool IsOverride(OverrideGraphicSettings o)
        {
            if (o == null) return false;
            return o.Halftone
                || o.Transparency != 0
                || o.ProjectionLineWeight != -1
                || o.CutLineWeight != -1
                || (o.ProjectionLineColor != null && o.ProjectionLineColor.IsValid)
                || (o.CutLineColor != null && o.CutLineColor.IsValid);
        }

        private static string? ReadValue(View v, BuiltInParameter bip)
        {
            var p = v.get_Parameter(bip);
            if (p == null) return null;
            return ValueString(p);
        }

        private static string? ValueString(Parameter p)
        {
            try
            {
                var vs = p.AsValueString();
                if (!string.IsNullOrEmpty(vs)) return vs;
                switch (p.StorageType)
                {
                    case StorageType.String: return p.AsString();
                    case StorageType.Integer: return p.AsInteger().ToString();
                    case StorageType.Double: return p.AsDouble().ToString("R");
                    case StorageType.ElementId: return p.AsElementId() == null ? null : Raw(p.AsElementId()).ToString();
                    default: return null;
                }
            }
            catch { return null; }
        }

        private static string Sha1(string s)
        {
            using var sha = System.Security.Cryptography.SHA1.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>ElementId raw value. ElementId.Value (long) exists in Revit 2024+.</summary>
        private static long Raw(ElementId id)
        {
            return id.Value;
        }

        private static long Raw(BuiltInParameter bip)
        {
            return (long)(int)bip;
        }
    }
}
