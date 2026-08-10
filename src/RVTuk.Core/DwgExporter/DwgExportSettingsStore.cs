using RVTuk.Core.Shared.Config;

namespace RVTuk.Core.DwgExporter
{
    /// <summary>The dialog's remembered choices, in the shape the dialog wants them —
    /// free of the inverted/additive booleans AppConfig has to store them as.</summary>
    public class DwgExportSettings
    {
        public string OutputFolder { get; set; } = "";
        public string PdfOutputFolder { get; set; } = "";
        public bool SeparatePdfFolder { get; set; }
        public string SheetNamingSetupName { get; set; } = "";
        public string ViewNamingSetupName { get; set; } = DwgExportDefaults.ViewNameNamingName;
        public string DwgSetupName { get; set; } = "";
        public string SheetSetName { get; set; } = "";
        public bool UseCurrentWindow { get; set; }
        public bool ExportDwg { get; set; } = true;
        public bool ExportPdf { get; set; }
        public bool CopyMissingSetups { get; set; }
        public TransmittalMode ZipMode { get; set; } = TransmittalMode.None;
    }

    /// <summary>
    /// Reads and writes the DWG Export dialog's last-used values on an <see cref="AppConfig"/>.
    /// Kept out of the view-model so the storage quirks — inverted format booleans, per-model
    /// folder lists, the empty-means-default view naming key — are testable without WPF or disk.
    /// </summary>
    public static class DwgExportSettingsStore
    {
        public static DwgExportSettings Read(AppConfig config, string modelKey) => new DwgExportSettings
        {
            OutputFolder = config.GetDwgExportFolder(modelKey),
            PdfOutputFolder = config.GetDwgExportPdfFolder(modelKey),
            SeparatePdfFolder = config.DwgExportSeparatePdfFolder,
            // The pre-split key kept its name: it has always meant the sheets rule.
            SheetNamingSetupName = config.DwgExportPdfSetupName,
            ViewNamingSetupName = string.IsNullOrWhiteSpace(config.DwgExportViewNamingSetupName)
                ? DwgExportDefaults.ViewNameNamingName
                : config.DwgExportViewNamingSetupName,
            DwgSetupName = config.DwgExportDwgSetupName,
            SheetSetName = config.DwgExportSheetSetName,
            UseCurrentWindow = config.DwgExportUseCurrentWindow,
            ExportDwg = !config.DwgExportDwgOff,
            ExportPdf = config.DwgExportPdfOn,
            CopyMissingSetups = config.DwgExportCopyMissingSetups,
            // The mode supersedes the old bool; fall back to it so a config written before
            // the mode existed doesn't silently lose its bundling.
            ZipMode = config.DwgExportZipMode != TransmittalMode.None
                ? config.DwgExportZipMode
                : config.DwgExportCreateZip ? TransmittalMode.OneBundle : TransmittalMode.None,
        };

        public static void Write(AppConfig config, string modelKey, DwgExportSettings settings)
        {
            config.SetDwgExportFolder(modelKey, settings.OutputFolder);
            // With one folder chosen, the PDF folder tracks it — otherwise a stale path would
            // reappear the moment the checkbox is ticked again.
            config.SetDwgExportPdfFolder(modelKey,
                settings.SeparatePdfFolder ? settings.PdfOutputFolder : settings.OutputFolder);
            config.DwgExportSeparatePdfFolder = settings.SeparatePdfFolder;
            config.DwgExportPdfSetupName = settings.SheetNamingSetupName;
            config.DwgExportViewNamingSetupName = settings.ViewNamingSetupName;
            config.DwgExportDwgSetupName = settings.DwgSetupName;
            config.DwgExportSheetSetName = settings.SheetSetName;
            config.DwgExportUseCurrentWindow = settings.UseCurrentWindow;
            config.DwgExportDwgOff = !settings.ExportDwg;
            config.DwgExportPdfOn = settings.ExportPdf;
            config.DwgExportCopyMissingSetups = settings.CopyMissingSetups;
            config.DwgExportZipMode = settings.ZipMode;
            // Kept in step so a downgrade, or another reader of the old key, still sees it.
            config.DwgExportCreateZip = settings.ZipMode != TransmittalMode.None;
        }
    }
}
