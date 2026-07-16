using System;
using System.Collections.Generic;
using System.Linq;
using RVTuk.Core.DwgExporter;
using RVTuk.Core.Shared.Config;
using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.DwgExporter.ViewModels
{
    /// <summary>
    /// State + orchestration for the modal DWG Export dialog. Revit-free: the three delegates
    /// run against the Revit API inside the command's context (the dialog is modal, so the
    /// nested message pump keeps that context alive while handlers run on the UI thread).
    /// </summary>
    public class DwgExportViewModel : ViewModelBase
    {
        private readonly Func<DwgExportRequest, string> _evaluateExample;
        private readonly Func<DwgExportRequest, DwgExportPlan> _planExport;
        private readonly Func<DwgExportRequest, Action<int, int, string>, DwgExportResult> _runExport;
        /// <summary>Identifies the open model (document path, or title while unsaved) so the
        /// output folder can be remembered per model.</summary>
        private readonly string _modelKey;

        /// <summary>Asks the user to confirm overwriting N existing files. Wired by the window.</summary>
        public Func<string, bool>? ConfirmOverwrite { get; set; }
        /// <summary>Shows a blocking error message. Wired by the window.</summary>
        public Action<string>? ShowError { get; set; }
        /// <summary>Lets the WPF dispatcher repaint during the synchronous export loop. Wired by the window.</summary>
        public Action? PumpUi { get; set; }

        /// <summary>Hand-off to a native Revit dialog ("pdf" = PDF Export for naming rules,
        /// "sets" = Publish Settings for view/sheet sets (falls back to PDF Export),
        /// "dwgsetups" = Modify DWG/DXF Export Setup). Wired by the command; returns true when
        /// the command was posted (the window then closes — posted commands run only after
        /// this modal command ends).</summary>
        public Func<string, bool>? OpenNativeDialog { get; set; }

        public IReadOnlyList<PdfSetupItem> PdfSetups { get; }
        public IReadOnlyList<string> DwgSetupNames { get; }
        public IReadOnlyList<SheetSetItem> SheetSets { get; }
        public string CurrentViewLabel { get; }

        public RelayCommand ExportCommand { get; }

        public DwgExportViewModel(
            IReadOnlyList<PdfSetupItem> pdfSetups,
            IReadOnlyList<string> dwgSetupNames,
            IReadOnlyList<SheetSetItem> sheetSets,
            string currentViewLabel,
            string modelKey,
            Func<DwgExportRequest, string> evaluateExample,
            Func<DwgExportRequest, DwgExportPlan> planExport,
            Func<DwgExportRequest, Action<int, int, string>, DwgExportResult> runExport)
        {
            PdfSetups = pdfSetups;
            DwgSetupNames = dwgSetupNames;
            SheetSets = sheetSets;
            CurrentViewLabel = currentViewLabel;
            _modelKey = modelKey;
            _evaluateExample = evaluateExample;
            _planExport = planExport;
            _runExport = runExport;

            ExportCommand = new RelayCommand(Export, () => CanExport);

            // Restore last-used choices; unknown names fall back to the first entry.
            var config = ConfigManager.LoadConfig();
            _outputFolder = config.GetDwgExportFolder(modelKey);
            _useCurrentWindow = config.DwgExportUseCurrentWindow || sheetSets.Count == 0;
            _selectedPdfSetup =
                pdfSetups.FirstOrDefault(s => s.Name == config.DwgExportPdfSetupName) ?? pdfSetups.FirstOrDefault();
            _selectedDwgSetup =
                dwgSetupNames.FirstOrDefault(n => n == config.DwgExportDwgSetupName) ?? dwgSetupNames.FirstOrDefault();
            _selectedSheetSet =
                sheetSets.FirstOrDefault(s => s.Name == config.DwgExportSheetSetName) ?? sheetSets.FirstOrDefault();
            _exportDwgFormat = !config.DwgExportDwgOff;
            _exportPdfFormat = config.DwgExportPdfOn;

            RefreshExample();
        }

        private bool _useCurrentWindow;
        public bool UseCurrentWindow
        {
            get => _useCurrentWindow;
            set
            {
                SetProperty(ref _useCurrentWindow, value);
                OnPropertyChanged(nameof(UseSheetSet)); // keep the inverse radio in sync
                RefreshExample();
            }
        }

        // Inverse binding target for the second radio button.
        public bool UseSheetSet
        {
            get => !_useCurrentWindow;
            set => UseCurrentWindow = !value;
        }

        private bool _exportDwgFormat;
        public bool ExportDwgFormat
        {
            get => _exportDwgFormat;
            set { SetProperty(ref _exportDwgFormat, value); OnPropertyChanged(nameof(CanExport)); }
        }

        private bool _exportPdfFormat;
        public bool ExportPdfFormat
        {
            get => _exportPdfFormat;
            set { SetProperty(ref _exportPdfFormat, value); OnPropertyChanged(nameof(CanExport)); }
        }

        private SheetSetItem? _selectedSheetSet;
        public SheetSetItem? SelectedSheetSet
        {
            get => _selectedSheetSet;
            set { SetProperty(ref _selectedSheetSet, value); RefreshExample(); }
        }

        private PdfSetupItem? _selectedPdfSetup;
        public PdfSetupItem? SelectedPdfSetup
        {
            get => _selectedPdfSetup;
            set
            {
                SetProperty(ref _selectedPdfSetup, value);
                OnPropertyChanged(nameof(PatternText));
                RefreshExample();
            }
        }

        public string PatternText => _selectedPdfSetup?.Pattern ?? "";

        private string? _selectedDwgSetup;
        public string? SelectedDwgSetup
        {
            get => _selectedDwgSetup;
            set => SetProperty(ref _selectedDwgSetup, value);
        }

        private string _outputFolder;
        public string OutputFolder
        {
            get => _outputFolder;
            set => SetProperty(ref _outputFolder, value);
        }

        private string _exampleText = "";
        public string ExampleText
        {
            get => _exampleText;
            private set => SetProperty(ref _exampleText, value);
        }

        private bool _isExporting;
        public bool IsExporting
        {
            get => _isExporting;
            private set { SetProperty(ref _isExporting, value); OnPropertyChanged(nameof(CanExport)); }
        }

        private int _progressValue;
        public int ProgressValue { get => _progressValue; private set => SetProperty(ref _progressValue, value); }

        private int _progressMax = 1;
        public int ProgressMax { get => _progressMax; private set => SetProperty(ref _progressMax, value); }

        private string _statusText = "";
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        public bool CanExport =>
            !IsExporting
            && !string.IsNullOrWhiteSpace(OutputFolder)
            && SelectedPdfSetup != null
            && SelectedDwgSetup != null
            && (ExportDwgFormat || ExportPdfFormat)
            && (UseCurrentWindow || SelectedSheetSet != null);

        private DwgExportRequest BuildRequest() => new DwgExportRequest
        {
            CurrentWindow = UseCurrentWindow,
            SheetSetName = SelectedSheetSet?.Name ?? "",
            PdfSetupName = SelectedPdfSetup?.Name ?? "",
            DwgSetupName = SelectedDwgSetup ?? "",
            OutputFolder = OutputFolder.Trim(),
            ExportDwg = ExportDwgFormat,
            ExportPdf = ExportPdfFormat,
        };

        private void RefreshExample()
        {
            try
            {
                ExampleText = _evaluateExample(BuildRequest());
            }
            catch (Exception ex)
            {
                ExampleText = "(example unavailable: " + ex.Message + ")";
            }
        }

        private void Export()
        {
            var request = BuildRequest();

            DwgExportPlan plan;
            try
            {
                plan = _planExport(request);
            }
            catch (Exception ex)
            {
                ShowError?.Invoke("Could not prepare the export:\n\n" + ex.Message);
                return;
            }

            if (plan.Files.Count == 0)
            {
                ShowError?.Invoke("Nothing to export — the selected range contains no sheets or views.");
                return;
            }
            if (plan.DuplicateNames.Count > 0)
            {
                ShowError?.Invoke(
                    "These filenames would be produced by more than one sheet, so sheets would " +
                    "overwrite each other. Fix the naming rule or the sheet parameters:\n\n  " +
                    string.Join("\n  ", plan.DuplicateNames));
                return;
            }
            if (plan.ExistingFileNames.Count > 0)
            {
                var ok = ConfirmOverwrite?.Invoke(
                    plan.ExistingFileNames.Count + " file(s) already exist in the output folder — overwrite?") ?? true;
                if (!ok) return;
            }

            IsExporting = true;
            ProgressMax = plan.Files.Count;
            ProgressValue = 0;
            StatusText = "Exporting…";
            try
            {
                var result = _runExport(request, (done, total, label) =>
                {
                    ProgressValue = done;
                    StatusText = done + "/" + total + "  " + label;
                    PumpUi?.Invoke();
                });

                StatusText = result.Errors.Count == 0
                    ? "Exported " + result.ExportedCount + " file(s) to " + request.OutputFolder
                    : "Exported " + result.ExportedCount + ", failed " + result.Errors.Count + ":\n" +
                      string.Join("\n", result.Errors);

                SaveLastUsed();
            }
            catch (Exception ex)
            {
                StatusText = "Export failed: " + ex.Message;
            }
            finally
            {
                IsExporting = false;
            }
        }

        private void SaveLastUsed()
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                config.SetDwgExportFolder(_modelKey, OutputFolder.Trim());
                config.DwgExportPdfSetupName = SelectedPdfSetup?.Name ?? "";
                config.DwgExportDwgSetupName = SelectedDwgSetup ?? "";
                config.DwgExportSheetSetName = SelectedSheetSet?.Name ?? "";
                config.DwgExportUseCurrentWindow = UseCurrentWindow;
                config.DwgExportDwgOff = !ExportDwgFormat;
                config.DwgExportPdfOn = ExportPdfFormat;
                ConfigManager.SaveConfig(config);
            }
            catch
            {
                // Remembering settings is best-effort; never fail an export over it.
            }
        }
    }
}
