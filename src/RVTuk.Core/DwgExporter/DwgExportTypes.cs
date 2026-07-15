using System.Collections.Generic;

namespace RVTuk.Core.DwgExporter
{
    /// <summary>Sentinel dropdown entries used when the document has no saved setups.</summary>
    public static class DwgExportDefaults
    {
        /// <summary>Fallback naming "setup" offered when the document has no ExportPDFSettings.</summary>
        public const string FallbackPdfSetupName = "<Sheet Number> - <Sheet Name>";

        /// <summary>Fallback DWG setup entry meaning "stock DWGExportOptions".</summary>
        public const string DefaultDwgSetupName = "<Revit defaults>";
    }

    /// <summary>What the user picked in the dialog; handed to the Revit-side delegates.</summary>
    public class DwgExportRequest
    {
        public bool CurrentWindow { get; set; }
        public string SheetSetName { get; set; } = "";
        public string PdfSetupName { get; set; } = "";
        public string DwgSetupName { get; set; } = "";
        public string OutputFolder { get; set; } = "";
    }

    /// <summary>One view/sheet the export will produce. FileName has no ".dwg" extension.</summary>
    public class PlannedExportFile
    {
        public string ViewLabel { get; set; } = "";
        public string FileName { get; set; } = "";
    }

    /// <summary>Pre-flight result: everything the dialog needs to warn/abort before exporting.</summary>
    public class DwgExportPlan
    {
        public IReadOnlyList<PlannedExportFile> Files { get; set; } = new List<PlannedExportFile>();
        public IReadOnlyList<string> DuplicateNames { get; set; } = new List<string>();
        public IReadOnlyList<string> ExistingFileNames { get; set; } = new List<string>();
    }

    public class DwgExportResult
    {
        public int ExportedCount { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    /// <summary>A saved PDF export setup as shown in the naming dropdown.</summary>
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
