using System.Collections.Generic;

namespace RVTuk.Core.DwgExporter
{
    /// <summary>Sentinel dropdown entries used when the document has no saved setups, or when
    /// a rule is built in code rather than read from a setup.</summary>
    public static class DwgExportDefaults
    {
        /// <summary>Fallback naming "setup" offered when the document has no ExportPDFSettings.</summary>
        public const string FallbackPdfSetupName = "<Sheet Number> - <Sheet Name>";

        /// <summary>Fallback DWG setup entry meaning "stock DWGExportOptions".</summary>
        public const string DefaultDwgSetupName = "<Revit defaults>";

        /// <summary>Views-naming entry meaning "use the view's own name" — what non-sheet views
        /// did before they could take a rule, and still the default.</summary>
        public const string ViewNameNamingName = "<View Name>";
    }

    /// <summary>What the user picked in the dialog; handed to the Revit-side delegates.</summary>
    public class DwgExportRequest
    {
        public bool CurrentWindow { get; set; }
        public string SheetSetName { get; set; } = "";

        /// <summary>PDF setup whose naming rule names sheets.</summary>
        public string SheetNamingSetupName { get; set; } = "";

        /// <summary>PDF setup whose naming rule names non-sheet views, or
        /// <see cref="DwgExportDefaults.ViewNameNamingName"/> for the view's own name.</summary>
        public string ViewNamingSetupName { get; set; } = DwgExportDefaults.ViewNameNamingName;

        public string DwgSetupName { get; set; } = "";
        public string OutputFolder { get; set; } = "";

        /// <summary>Only consulted when <see cref="SeparatePdfFolder"/> is on.</summary>
        public string PdfOutputFolder { get; set; } = "";
        public bool SeparatePdfFolder { get; set; }

        /// <summary>Formats to produce. At least one must be true (the dialog enforces it).</summary>
        public bool ExportDwg { get; set; } = true;
        public bool ExportPdf { get; set; }

        /// <summary>Keys of other open models to include; empty for an active-model-only run.
        /// Keys are document paths, or titles while unsaved.</summary>
        public List<string> ExtraModelKeys { get; set; } = new List<string>();

        /// <summary>When an extra model has no setup with the chosen name: true creates it
        /// there, false skips the model.</summary>
        public bool CopyMissingSetups { get; set; }

        /// <summary>Where PDFs go: the separate folder when asked for and filled in, else the
        /// DWG folder — so an unticked checkbox behaves exactly as before it existed.</summary>
        public string PdfFolder => SeparatePdfFolder && !string.IsNullOrWhiteSpace(PdfOutputFolder)
            ? PdfOutputFolder
            : OutputFolder;
    }

    /// <summary>One view/sheet the export will produce. FileName has no ".dwg" extension.</summary>
    public class PlannedExportFile
    {
        /// <summary>Title of the model this came from; blank on a single-model run.</summary>
        public string ModelTitle { get; set; } = "";
        public string ViewLabel { get; set; } = "";
        public string FileName { get; set; } = "";

        /// <summary>Sheets take the sheets rule, everything else the views rule.</summary>
        public bool IsSheet { get; set; }

        /// <summary>The views rule resolved to nothing here, so the view's own name was used.</summary>
        public bool UsedViewNameFallback { get; set; }

        /// <summary>"Tower-A.rvt — A-101" when the run spans models, else just the label.</summary>
        public string Source => string.IsNullOrEmpty(ModelTitle) ? ViewLabel : ModelTitle + " — " + ViewLabel;
    }

    /// <summary>One filename two or more views would both produce, and where each came from.</summary>
    public class DuplicateFileName
    {
        public string FileName { get; set; } = "";

        /// <summary>Each clashing view, as "Tower-A.rvt — A-101" once a run spans models.</summary>
        public IReadOnlyList<string> Sources { get; set; } = new List<string>();
    }

    /// <summary>Pre-flight result: everything the dialog needs to warn/abort before exporting.</summary>
    public class DwgExportPlan
    {
        public IReadOnlyList<PlannedExportFile> Files { get; set; } = new List<PlannedExportFile>();
        public IReadOnlyList<DuplicateFileName> Duplicates { get; set; } = new List<DuplicateFileName>();
        public IReadOnlyList<string> ExistingFileNames { get; set; } = new List<string>();
    }

    public class DwgExportResult
    {
        public int ExportedCount { get; set; }
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>Things that went through but the user should know about — a skipped model,
        /// a views rule that resolved to nothing.</summary>
        public List<string> Notes { get; set; } = new List<string>();
    }

    /// <summary>A saved PDF export setup as shown in a naming dropdown.</summary>
    public class PdfSetupItem
    {
        public string Name { get; set; } = "";
        /// <summary>Human-readable rule, e.g. "&lt;Project Number&gt;-A-BLD_&lt;Building Number&gt;-&lt;Sheet Number&gt;".</summary>
        public string Pattern { get; set; } = "";
    }

    /// <summary>A saved view/sheet set as shown in the range dropdown.</summary>
    public class SheetSetItem
    {
        public string Name { get; set; } = "";
        public int SheetCount { get; set; }
        public string Display => $"{Name} ({SheetCount} {(SheetCount == 1 ? "sheet" : "sheets")})";
    }
}
