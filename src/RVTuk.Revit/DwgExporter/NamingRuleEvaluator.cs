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

        public static IReadOnlyList<NamingRulePart> ResolveForSheet(
            Document doc, ViewSheet sheet, IList<TableCellCombinedParameterData>? rule)
        {
            if (rule == null || rule.Count == 0)
            {
                return new List<NamingRulePart>
                {
                    new NamingRulePart { Value = sheet.SheetNumber, Separator = " - " },
                    new NamingRulePart { Value = sheet.Name },
                };
            }

            return rule.Select(entry => new NamingRulePart
            {
                Prefix = entry.Prefix ?? "",
                Value = ResolveValue(doc, sheet, entry),
                Suffix = entry.Suffix ?? "",
                Separator = entry.Separator ?? "",
            }).ToList();
        }

        private static string ParamName(Document doc, TableCellCombinedParameterData entry)
        {
            if (entry.ParamId.Value < 0)
                return LabelUtils.GetLabelFor((BuiltInParameter)entry.ParamId.Value);
            return (doc.GetElement(entry.ParamId) as ParameterElement)?.Name ?? "?";
        }

        /// <summary>
        /// The rule stores which category each field comes from, but resolving is simpler and
        /// more robust by probing: try the sheet first, then Project Information (the only two
        /// sources the PDF naming rule offers for sheets).
        /// </summary>
        private static string ResolveValue(Document doc, ViewSheet sheet, TableCellCombinedParameterData entry)
        {
            var param = FindParameter(doc, sheet, entry.ParamId)
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
