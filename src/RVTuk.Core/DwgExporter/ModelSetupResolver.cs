using System;
using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.DwgExporter
{
    /// <summary>What one open model offers, by name — all the resolver needs to know about it.</summary>
    public class ModelSetupInventory
    {
        /// <summary>Document path, or title while unsaved.</summary>
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public bool IsReadOnly { get; set; }
        public IReadOnlyList<string> SheetSetNames { get; set; } = new List<string>();
        public IReadOnlyList<string> PdfSetupNames { get; set; } = new List<string>();
        public IReadOnlyList<string> DwgSetupNames { get; set; } = new List<string>();
    }

    /// <summary>Whether a model can take part in the run, and what has to be created in it first.</summary>
    public class ModelExportPlan
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public bool CanRun { get; set; }

        /// <summary>Empty when <see cref="CanRun"/>; otherwise a phrase that reads after
        /// "Tower-B.rvt: ".</summary>
        public string SkipReason { get; set; } = "";

        public IReadOnlyList<string> PdfSetupsToCopy { get; set; } = new List<string>();
        public bool CopyDwgSetup { get; set; }
    }

    /// <summary>
    /// Export setups and view/sheet sets are per-document elements with no cross-document
    /// identity, so an extra model resolves the active model's choices by name. This decides
    /// what that lookup means for one model: run as-is, run after copying setups in, or skip.
    /// Pure — the Revit layer supplies the inventory and performs any copies.
    /// </summary>
    public static class ModelSetupResolver
    {
        public static ModelExportPlan Resolve(ModelSetupInventory model, DwgExportRequest request)
        {
            var plan = new ModelExportPlan { Key = model.Key, Title = model.Title };

            // A ViewSheetSet holds references to views that don't exist in another document,
            // so a missing set can never be copied across — it always skips.
            if (!Has(model.SheetSetNames, request.SheetSetName))
            {
                plan.SkipReason = "no view/sheet set named '" + request.SheetSetName + "'";
                return plan;
            }

            var pdfMissing = RequiredPdfSetups(request)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(name => !Has(model.PdfSetupNames, name))
                .ToList();
            var dwgMissing = RequiresDwgSetup(request) && !Has(model.DwgSetupNames, request.DwgSetupName);

            if (pdfMissing.Count == 0 && !dwgMissing)
            {
                plan.CanRun = true;
                return plan;
            }

            var missing = string.Join(", ",
                pdfMissing.Concat(dwgMissing ? new[] { request.DwgSetupName } : Array.Empty<string>())
                          .Select(n => "'" + n + "'"));

            if (!request.CopyMissingSetups)
            {
                plan.SkipReason = "no export setup named " + missing + " (copying is off)";
                return plan;
            }
            if (model.IsReadOnly)
            {
                plan.SkipReason = "the model is read-only, so " + missing + " cannot be created in it";
                return plan;
            }

            plan.CanRun = true;
            plan.PdfSetupsToCopy = pdfMissing;
            plan.CopyDwgSetup = dwgMissing;
            return plan;
        }

        /// <summary>The naming setups this run needs by name. Both formats need them — the DWG
        /// filenames are evaluated from the same rules — but the two sentinels are built in code
        /// and so need nothing from the model.</summary>
        private static IEnumerable<string> RequiredPdfSetups(DwgExportRequest request)
        {
            if (IsRealSetup(request.SheetNamingSetupName)) yield return request.SheetNamingSetupName;
            if (IsRealSetup(request.ViewNamingSetupName)) yield return request.ViewNamingSetupName;
        }

        private static bool IsRealSetup(string name)
            => !string.IsNullOrWhiteSpace(name)
            && name != DwgExportDefaults.FallbackPdfSetupName
            && name != DwgExportDefaults.ViewNameNamingName;

        private static bool RequiresDwgSetup(DwgExportRequest request)
            => request.ExportDwg
            && !string.IsNullOrWhiteSpace(request.DwgSetupName)
            && request.DwgSetupName != DwgExportDefaults.DefaultDwgSetupName;

        private static bool Has(IReadOnlyList<string> names, string name)
            => names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }
}
