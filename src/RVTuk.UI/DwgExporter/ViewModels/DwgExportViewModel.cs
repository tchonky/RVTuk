using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private readonly Func<DwgExportRequest, DwgExportExamples> _evaluateExample;
        private readonly Func<DwgExportRequest, DwgExportPlan> _planExport;
        private readonly Func<DwgExportRequest, Action<int, int, string>, DwgExportResult> _runExport;
        /// <summary>Identifies the active model (document path, or title while unsaved) so the
        /// output folders can be remembered per model.</summary>
        private readonly string _activeModelKey;

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
        /// <summary>The sheets list plus a "&lt;View Name&gt;" entry at the top.</summary>
        public IReadOnlyList<PdfSetupItem> ViewNamingOptions { get; }
        public IReadOnlyList<string> DwgSetupNames { get; }
        public IReadOnlyList<SheetSetItem> SheetSets { get; }
        public string CurrentViewLabel { get; }
        public ObservableCollection<ModelSelectionItem> Models { get; }

        /// <summary>Only meaningful when more than one model is open.</summary>
        public bool HasOtherModels => Models.Count > 1;

        public RelayCommand ExportCommand { get; }

        public DwgExportViewModel(
            IReadOnlyList<PdfSetupItem> pdfSetups,
            IReadOnlyList<string> dwgSetupNames,
            IReadOnlyList<SheetSetItem> sheetSets,
            string currentViewLabel,
            IReadOnlyList<ModelSetupInventory> models,
            string activeModelKey,
            Func<DwgExportRequest, DwgExportExamples> evaluateExample,
            Func<DwgExportRequest, DwgExportPlan> planExport,
            Func<DwgExportRequest, Action<int, int, string>, DwgExportResult> runExport)
        {
            PdfSetups = pdfSetups;
            ViewNamingOptions = new[]
                {
                    new PdfSetupItem
                    {
                        Name = DwgExportDefaults.ViewNameNamingName,
                        Pattern = DwgExportDefaults.ViewNameNamingName,
                    },
                }
                .Concat(pdfSetups)
                .ToList();
            DwgSetupNames = dwgSetupNames;
            SheetSets = sheetSets;
            CurrentViewLabel = currentViewLabel;
            Models = new ObservableCollection<ModelSelectionItem>(
                models.Select(m => new ModelSelectionItem(m, isActive: m.Key == activeModelKey)));
            _activeModelKey = activeModelKey;
            _evaluateExample = evaluateExample;
            _planExport = planExport;
            _runExport = runExport;

            ExportCommand = new RelayCommand(Export, () => CanExport);

            // Restore last-used choices; unknown names fall back to the first entry.
            var settings = DwgExportSettingsStore.Read(ConfigManager.LoadConfig(), activeModelKey);
            _outputFolder = settings.OutputFolder;
            _pdfOutputFolder = settings.PdfOutputFolder;
            _separatePdfFolder = settings.SeparatePdfFolder;
            _copyMissingSetups = settings.CopyMissingSetups;
            _createTransmittalZip = settings.ZipMode != TransmittalMode.None;
            _zipPerDrawing = settings.ZipMode == TransmittalMode.PerDrawing;
            _useCurrentWindow = settings.UseCurrentWindow || sheetSets.Count == 0;
            _selectedSheetNaming =
                pdfSetups.FirstOrDefault(s => s.Name == settings.SheetNamingSetupName) ?? pdfSetups.FirstOrDefault();
            _selectedViewNaming =
                ViewNamingOptions.FirstOrDefault(s => s.Name == settings.ViewNamingSetupName)
                ?? ViewNamingOptions.FirstOrDefault();
            _selectedDwgSetup =
                dwgSetupNames.FirstOrDefault(n => n == settings.DwgSetupName) ?? dwgSetupNames.FirstOrDefault();
            _selectedSheetSet =
                sheetSets.FirstOrDefault(s => s.Name == settings.SheetSetName) ?? sheetSets.FirstOrDefault();
            _exportDwgFormat = settings.ExportDwg;
            _exportPdfFormat = settings.ExportPdf;

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
                OnPropertyChanged(nameof(MultiModelEnabled));
                RefreshExample();
            }
        }

        // Inverse binding target for the second radio button.
        public bool UseSheetSet
        {
            get => !_useCurrentWindow;
            set => UseCurrentWindow = !value;
        }

        /// <summary>Other models can only join a saved-set run — "current window" is by
        /// definition the active model's.</summary>
        public bool MultiModelEnabled => !_useCurrentWindow && HasOtherModels;

        private bool _exportDwgFormat;
        public bool ExportDwgFormat
        {
            get => _exportDwgFormat;
            set
            {
                SetProperty(ref _exportDwgFormat, value);
                OnPropertyChanged(nameof(CanExport));
                OnPropertyChanged(nameof(CanBundle));
            }
        }

        private bool _createTransmittalZip;
        /// <summary>Bundle the DWGs and what they depend on. The shape is
        /// <see cref="ZipPerDrawing"/>.</summary>
        public bool CreateTransmittalZip
        {
            get => _createTransmittalZip;
            set { SetProperty(ref _createTransmittalZip, value); OnPropertyChanged(nameof(ZipOptionsVisible)); }
        }

        private bool _zipPerDrawing;
        /// <summary>One archive per drawing rather than one for the whole run.</summary>
        public bool ZipPerDrawing
        {
            get => _zipPerDrawing;
            set { SetProperty(ref _zipPerDrawing, value); OnPropertyChanged(nameof(ZipOneBundle)); }
        }

        // Inverse binding target for the "one zip for the run" radio.
        public bool ZipOneBundle
        {
            get => !_zipPerDrawing;
            set => ZipPerDrawing = !value;
        }

        public bool ZipOptionsVisible => CreateTransmittalZip;

        private TransmittalMode ZipMode =>
            !CreateTransmittalZip || !ExportDwgFormat ? TransmittalMode.None
            : ZipPerDrawing ? TransmittalMode.PerDrawing
            : TransmittalMode.OneBundle;

        /// <summary>A PDF-only run has nothing to bundle.</summary>
        public bool CanBundle => ExportDwgFormat;

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

        private PdfSetupItem? _selectedSheetNaming;
        public PdfSetupItem? SelectedSheetNaming
        {
            get => _selectedSheetNaming;
            set
            {
                SetProperty(ref _selectedSheetNaming, value);
                OnPropertyChanged(nameof(SheetPatternText));
                RefreshExample();
            }
        }

        private PdfSetupItem? _selectedViewNaming;
        public PdfSetupItem? SelectedViewNaming
        {
            get => _selectedViewNaming;
            set
            {
                SetProperty(ref _selectedViewNaming, value);
                OnPropertyChanged(nameof(ViewPatternText));
                RefreshExample();
            }
        }

        public string SheetPatternText => _selectedSheetNaming?.Pattern ?? "";
        public string ViewPatternText => _selectedViewNaming?.Pattern ?? "";

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

        private bool _separatePdfFolder;
        public bool SeparatePdfFolder
        {
            get => _separatePdfFolder;
            set => SetProperty(ref _separatePdfFolder, value);
        }

        private string _pdfOutputFolder = "";
        public string PdfOutputFolder
        {
            get => _pdfOutputFolder;
            set => SetProperty(ref _pdfOutputFolder, value);
        }

        private bool _copyMissingSetups;
        /// <summary>True creates a missing setup in the other model; false skips that model.</summary>
        public bool CopyMissingSetups
        {
            get => _copyMissingSetups;
            set { SetProperty(ref _copyMissingSetups, value); OnPropertyChanged(nameof(SkipMissingSetups)); }
        }

        // Inverse binding target for the "skip" radio button.
        public bool SkipMissingSetups
        {
            get => !_copyMissingSetups;
            set => CopyMissingSetups = !value;
        }

        private string _sheetExampleText = "";
        public string SheetExampleText
        {
            get => _sheetExampleText;
            private set => SetProperty(ref _sheetExampleText, value);
        }

        private string _viewExampleText = "";
        public string ViewExampleText
        {
            get => _viewExampleText;
            private set => SetProperty(ref _viewExampleText, value);
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
            && (!SeparatePdfFolder || !ExportPdfFormat || !string.IsNullOrWhiteSpace(PdfOutputFolder))
            && SelectedSheetNaming != null
            && SelectedViewNaming != null
            && SelectedDwgSetup != null
            && (ExportDwgFormat || ExportPdfFormat)
            && (UseCurrentWindow || SelectedSheetSet != null);

        private DwgExportRequest BuildRequest() => new DwgExportRequest
        {
            CurrentWindow = UseCurrentWindow,
            SheetSetName = SelectedSheetSet?.Name ?? "",
            SheetNamingSetupName = SelectedSheetNaming?.Name ?? "",
            ViewNamingSetupName = SelectedViewNaming?.Name ?? DwgExportDefaults.ViewNameNamingName,
            DwgSetupName = SelectedDwgSetup ?? "",
            OutputFolder = OutputFolder.Trim(),
            PdfOutputFolder = PdfOutputFolder.Trim(),
            SeparatePdfFolder = SeparatePdfFolder,
            ExportDwg = ExportDwgFormat,
            ExportPdf = ExportPdfFormat,
            CopyMissingSetups = CopyMissingSetups,
            ZipMode = ZipMode,
            ExtraModelKeys = MultiModelEnabled
                ? Models.Where(m => m.IsSelected && !m.IsActive).Select(m => m.Key).ToList()
                : new List<string>(),
        };

        private void RefreshExample()
        {
            try
            {
                var examples = _evaluateExample(BuildRequest());
                SheetExampleText = examples.Sheet;
                ViewExampleText = examples.View;
            }
            catch (Exception ex)
            {
                SheetExampleText = ViewExampleText = "(example unavailable: " + ex.Message + ")";
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
            if (plan.Duplicates.Count > 0)
            {
                ShowError?.Invoke(
                    "These filenames would be produced by more than one view, so files would " +
                    "overwrite each other. Fix the naming rule or the view parameters:\n\n  " +
                    string.Join("\n  ", plan.Duplicates.Select(
                        d => d.FileName + "  ←  " + string.Join(", ", d.Sources))));
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

                var lines = new List<string>
                {
                    result.Errors.Count == 0
                        ? "Exported " + result.ExportedCount + " file(s) to " + request.OutputFolder
                        : "Exported " + result.ExportedCount + ", failed " + result.Errors.Count + ":",
                };
                lines.AddRange(result.Errors);
                lines.AddRange(result.Notes);
                StatusText = string.Join("\n", lines);

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
                DwgExportSettingsStore.Write(config, _activeModelKey, new DwgExportSettings
                {
                    OutputFolder = OutputFolder.Trim(),
                    PdfOutputFolder = PdfOutputFolder.Trim(),
                    SeparatePdfFolder = SeparatePdfFolder,
                    SheetNamingSetupName = SelectedSheetNaming?.Name ?? "",
                    ViewNamingSetupName = SelectedViewNaming?.Name ?? DwgExportDefaults.ViewNameNamingName,
                    DwgSetupName = SelectedDwgSetup ?? "",
                    SheetSetName = SelectedSheetSet?.Name ?? "",
                    UseCurrentWindow = UseCurrentWindow,
                    ExportDwg = ExportDwgFormat,
                    ExportPdf = ExportPdfFormat,
                    CopyMissingSetups = CopyMissingSetups,
                    ZipMode = ZipMode,
                });
                ConfigManager.SaveConfig(config);
            }
            catch
            {
                // Remembering settings is best-effort; never fail an export over it.
            }
        }
    }
}
