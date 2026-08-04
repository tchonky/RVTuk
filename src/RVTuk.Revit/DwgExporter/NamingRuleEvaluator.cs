using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.DwgExporter;

namespace RVTuk.Revit.DwgExporter
{
    /// <summary>
    /// Turns a PDF export setup's naming rule into (a) a display pattern like
    /// "&lt;Project Number&gt;-A-BLD_&lt;Building Number&gt;-&lt;Sheet Number&gt;" and
    /// (b) resolved parts for a concrete sheet, which Core's FileNameComposer joins.
    /// A null rule means the built-in fallback: "&lt;Sheet Number&gt; - &lt;Sheet Name&gt;".
    /// </summary>
    internal static class NamingRuleEvaluator
    {
        public static string DescribePattern(Document doc, IList<TableCellCombinedParameterData>? rule)
        {
            if (rule == null || rule.Count == 0) return DwgExportDefaults.FallbackPdfSetupName;

            var parts = rule.Select(entry => new NamingRulePart
            {
                Prefix = entry.Prefix ?? "",
                Value = "<" + ParamName(doc, entry) + ">",
                Suffix = entry.Suffix ?? "",
                Separator = entry.Separator ?? "",
            }).ToList();
            return FileNameComposer.Compose(parts);
        }

        /// <summary>
        /// Resolves the rule against one view. A null rule means the built-in fallback, which
        /// differs by kind: sheets get "&lt;Sheet Number&gt; - &lt;Sheet Name&gt;", other views
        /// their own name.
        /// </summary>
        public static IReadOnlyList<NamingRulePart> ResolveForView(
            Document doc, View view, IList<TableCellCombinedParameterData>? rule)
        {
            if (rule == null || rule.Count == 0)
            {
                if (view is ViewSheet sheet)
                {
                    return new List<NamingRulePart>
                    {
                        new NamingRulePart { Value = sheet.SheetNumber, Separator = " - " },
                        new NamingRulePart { Value = sheet.Name },
                    };
                }
                return new List<NamingRulePart> { new NamingRulePart { Value = view.Name } };
            }

            return rule.Select(entry => new NamingRulePart
            {
                Prefix = entry.Prefix ?? "",
                Value = ResolveValue(doc, view, entry),
                Suffix = entry.Suffix ?? "",
                Separator = entry.Separator ?? "",
            }).ToList();
        }

        /// <summary>
        /// A one-field rule over the view's own Name. The "&lt;View Name&gt;" naming entry is
        /// expressed as a real rule rather than a special case in the export loop, so Revit
        /// names the PDFs exactly the way we name the DWGs.
        /// </summary>
        public static IList<TableCellCombinedParameterData> ViewNameRule()
        {
            var field = TableCellCombinedParameterData.Create();
            field.ParamId = new ElementId(BuiltInParameter.VIEW_NAME);
            return new List<TableCellCombinedParameterData> { field };
        }

        private static string ParamName(Document doc, TableCellCombinedParameterData entry)
        {
            if (entry.ParamId.Value < 0)
                return LabelUtils.GetLabelFor((BuiltInParameter)entry.ParamId.Value);
            return (doc.GetElement(entry.ParamId) as ParameterElement)?.Name ?? "?";
        }

        /// <summary>
        /// The rule stores which category each field comes from, but resolving is simpler and
        /// more robust by probing: try the view (or sheet) first, then Project Information —
        /// the two sources the PDF naming rule offers.
        /// </summary>
        private static string ResolveValue(Document doc, View view, TableCellCombinedParameterData entry)
        {
            var param = FindParameter(doc, view, entry.ParamId)
                        ?? FindParameter(doc, doc.ProjectInformation, entry.ParamId);
            if (param == null || !param.HasValue) return "";
            return (param.StorageType == StorageType.String ? param.AsString() : param.AsValueString()) ?? "";
        }

        private static Parameter? FindParameter(Document doc, Element element, ElementId paramId)
        {
            if (paramId.Value < 0)
                return element.get_Parameter((BuiltInParameter)paramId.Value);

            var paramElement = doc.GetElement(paramId) as ParameterElement;
            return paramElement == null ? null : element.LookupParameter(paramElement.Name);
        }
    }
}
