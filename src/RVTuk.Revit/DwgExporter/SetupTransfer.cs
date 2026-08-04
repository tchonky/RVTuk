using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.DwgExporter
{
    /// <summary>
    /// Reproduces one model's export setups in another. Used when a run spans models and the
    /// user asked for missing setups to be created rather than the model skipped — this writes
    /// into that model, so every caller must have checked <c>Document.IsReadOnly</c> first.
    /// </summary>
    internal static class SetupTransfer
    {
        /// <summary>
        /// Rebuilds a naming rule against another document. Built-in parameters have negative
        /// ids that mean the same thing in every document; a shared or project parameter's id
        /// is per-document, so it is matched by name instead. Returns null when any field has
        /// no counterpart in the target — a rule that would silently resolve to blanks is worse
        /// than skipping the model. The source's own field objects are never mutated.
        /// </summary>
        public static IList<TableCellCombinedParameterData>? Remap(
            IList<TableCellCombinedParameterData> rule,
            Document source,
            Document target,
            out List<string> unresolved)
        {
            unresolved = new List<string>();
            var rebuilt = new List<TableCellCombinedParameterData>();

            foreach (var entry in rule)
            {
                var field = TableCellCombinedParameterData.Create();
                field.Prefix = entry.Prefix ?? "";
                field.Suffix = entry.Suffix ?? "";
                field.Separator = entry.Separator ?? "";
                // CategoryId is a BuiltInCategory: negative, and the same in every document.
                field.CategoryId = entry.CategoryId;

                if (entry.ParamId.Value < 0)
                {
                    field.ParamId = entry.ParamId;
                }
                else
                {
                    var name = (source.GetElement(entry.ParamId) as ParameterElement)?.Name;
                    var match = name == null ? null : FindParameterElement(target, name);
                    if (match == null)
                    {
                        unresolved.Add(name ?? "parameter " + entry.ParamId.Value);
                        continue;
                    }
                    field.ParamId = match.Id;
                }

                rebuilt.Add(field);
            }

            return unresolved.Count > 0 ? null : rebuilt;
        }

        /// <summary>Creates the named PDF setup in the target document, naming rule and all.
        /// No-op when it is already there.</summary>
        public static void CopyPdfSetup(Document source, Document target, string name)
        {
            if (ExportPDFSettings.FindByName(target, name) != null) return;

            var from = ExportPDFSettings.FindByName(source, name)
                ?? throw new InvalidOperationException(
                    "PDF export setup '" + name + "' no longer exists in the active model.");
            if (!ExportPDFSettings.IsValidName(target, name))
                throw new InvalidOperationException("'" + name + "' is not a valid setup name in this model.");

            var options = from.GetOptions();
            var rule = options.GetNamingRule();
            if (rule != null && rule.Count > 0)
            {
                var remapped = Remap(rule, source, target, out var unresolved);
                if (remapped == null)
                {
                    throw new InvalidOperationException(
                        "'" + name + "' uses parameter(s) this model doesn't have: " +
                        string.Join(", ", unresolved));
                }
                options.SetNamingRule(remapped);
            }

            using var tx = new Transaction(target, "RVTuk – copy PDF export setup");
            tx.Start();
            ExportPDFSettings.Create(target, name, options);
            tx.Commit();
        }

        /// <summary>Creates the named DWG setup in the target document. Its layer table is keyed
        /// by category and subcategory name, not by element id, so it carries over as-is.</summary>
        public static void CopyDwgSetup(Document source, Document target, string name)
        {
            if (ExportDWGSettings.FindByName(target, name) != null) return;

            var from = ExportDWGSettings.FindByName(source, name)
                ?? throw new InvalidOperationException(
                    "DWG export setup '" + name + "' no longer exists in the active model.");

            var options = from.GetDWGExportOptions();

            using var tx = new Transaction(target, "RVTuk – copy DWG export setup");
            tx.Start();
            ExportDWGSettings.Create(target, name, options);
            tx.Commit();
        }

        private static ParameterElement? FindParameterElement(Document doc, string name)
            => new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterElement))
                .Cast<ParameterElement>()
                .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
