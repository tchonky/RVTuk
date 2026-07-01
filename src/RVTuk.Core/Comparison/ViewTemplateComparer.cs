using System.Collections.Generic;
using RVTuk.Core.Models.Comparison;
using RVTuk.Core.Serialization;

namespace RVTuk.Core.Comparison
{
    /// <summary>Compares view templates. Matches within a (ViewType, Name) bucket and produces
    /// inclusion-aware field diffs: a field only counts when at least one side controls it.</summary>
    public class ViewTemplateComparer : ICategoryComparer
    {
        public string CategoryId => ViewTemplatesSnapshot.Category;
        public string DisplayName => "View Templates";

        public CategorySnapshot LoadSnapshot(string payloadJson)
            => SnapshotJson.Deserialize<ViewTemplatesSnapshot>(payloadJson);

        public CategoryDiffResult Compare(CategorySnapshot a, CategorySnapshot b)
        {
            var sa = (ViewTemplatesSnapshot)a;
            var sb = (ViewTemplatesSnapshot)b;
            var result = new CategoryDiffResult { CategoryId = CategoryId, DisplayName = DisplayName };

            var join = Matcher.OuterJoin(sa.Templates, sb.Templates, Key);
            foreach (var (ta, tb) in join.Matched)
                result.Items.Add(DiffPair(ta, tb));
            foreach (var ta in join.OnlyA)
                result.Items.Add(OneSided(ta, DiffKind.Added));
            foreach (var tb in join.OnlyB)
                result.Items.Add(OneSided(tb, DiffKind.Removed));

            foreach (var item in result.Items)
            {
                switch (item.Kind)
                {
                    case DiffKind.Added: result.Summary.Added++; break;
                    case DiffKind.Removed: result.Summary.Removed++; break;
                    case DiffKind.Changed: result.Summary.Changed++; break;
                    case DiffKind.Unchanged: result.Summary.Unchanged++; break;
                }
            }
            return result;
        }

        private static string Key(ViewTemplateDto t) => t.ViewType + "|" + t.Name;

        private static ItemDiff OneSided(ViewTemplateDto t, DiffKind kind)
        {
            var item = new ItemDiff { Key = Key(t), DisplayName = t.Name, Kind = kind };
            var score = CompletenessScorer.Score(t);
            if (kind == DiffKind.Added) item.CompletenessA = score;
            else item.CompletenessB = score;
            return item;
        }

        private static ItemDiff DiffPair(ViewTemplateDto a, ViewTemplateDto b)
        {
            var item = new ItemDiff
            {
                Key = Key(a),
                DisplayName = a.Name,
                CompletenessA = CompletenessScorer.Score(a),
                CompletenessB = CompletenessScorer.Score(b),
            };

            var inclA = IncludedMap(a);
            var inclB = IncludedMap(b);
            var setA = a.Settings.ToDictionary();
            var setB = b.Settings.ToDictionary();
            var labels = LabelMap(a, b);

            var keys = new SortedSet<string>(inclA.Keys);
            keys.UnionWith(inclB.Keys);

            foreach (var key in keys)
            {
                bool ctrlA = inclA.TryGetValue(key, out var ca) && ca;
                bool ctrlB = inclB.TryGetValue(key, out var cb) && cb;
                if (!ctrlA && !ctrlB) continue; // both uncontrolled → ignore

                string? valA = ctrlA ? Get(setA, key) : null;
                string? valB = ctrlB ? Get(setB, key) : null;
                var label = labels.TryGetValue(key, out var l) ? l : key;
                item.Fields.Add(new FieldDiff(key, label, valA, valB));
            }

            // Synthetic V/G overrides field — only added when the digests differ, so it is
            // always a real difference (values are the digests, "none" when absent).
            if (!string.Equals(a.CategoryOverridesHash, b.CategoryOverridesHash))
            {
                item.Fields.Add(new FieldDiff(ViewTemplateFields.VgOverrides, "V/G Overrides",
                    HashLabel(a.CategoryOverridesHash), HashLabel(b.CategoryOverridesHash)));
            }

            // Synthetic Filters field — only when the applied filter sets differ.
            var fa = string.Join(", ", a.FilterNames);
            var fb = string.Join(", ", b.FilterNames);
            if (!string.Equals(fa, fb))
                item.Fields.Add(new FieldDiff(ViewTemplateFields.Filters, "Filters", fa, fb));

            item.Kind = AnyDiff(item.Fields) ? DiffKind.Changed : DiffKind.Unchanged;
            return item;
        }

        private static bool AnyDiff(List<FieldDiff> fields)
        {
            foreach (var f in fields)
                if (!f.IsEqual) return true;
            return false;
        }

        private static string HashLabel(string hash) => string.IsNullOrEmpty(hash) ? "none" : hash;

        private static string? Get(Dictionary<string, string> d, string key)
            => d.TryGetValue(key, out var v) ? v : null;

        private static Dictionary<string, bool> IncludedMap(ViewTemplateDto t)
        {
            var d = new Dictionary<string, bool>();
            foreach (var p in t.Included)
                d[p.Id] = p.Controlled;
            return d;
        }

        private static Dictionary<string, string> LabelMap(ViewTemplateDto a, ViewTemplateDto b)
        {
            var d = new Dictionary<string, string>();
            foreach (var p in b.Included) d[p.Id] = p.Label;
            foreach (var p in a.Included) d[p.Id] = p.Label; // A's label wins on conflict
            return d;
        }
    }
}
