using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using RVTuk.Core.AreaSubmission;
using RVTuk.Core.Config;

namespace RVTuk.UI.ViewModels
{
    public enum SubmissionPane { Config, Areas }

    /// <summary>
    /// View model for the Area Calc (Rishui Zamin) window. Top toolbar switches the bottom pane
    /// between the Config fields and the area tree; Revit behaviour is injected as delegates so
    /// this project takes no Revit dependency.
    /// </summary>
    public class AreaSubmissionViewModel : ViewModelBase
    {
        private readonly Func<IReadOnlyList<(long Id, AreaRecord Rec)>> _extract;
        private readonly Action<long> _selectInModel;
        private readonly Func<IReadOnlyList<AreaRecord>, AreaSubmissionConfig, (bool ok, string msg)> _export;
        private readonly Func<(bool ok, string msg)>? _setupUsageKeys;
        private readonly Dispatcher _dispatcher;

        private SubmissionPane _currentPane = SubmissionPane.Config;
        private AreaRowViewModel? _selectedRow;
        private string _buildingNoText = "";
        private string _scaleText = "";
        private string _fileBaseNameText = "";

        /// <param name="extract">Reads the areas on the open sheet (id + record).</param>
        /// <param name="selectInModel">Selects an Area in the model by element id.</param>
        /// <param name="export">Validates + writes the DXF/DAT for the given records; returns (ok, message).</param>
        /// <param name="setupUsageKeys">Binds the robot's area text parameters and creates/tops-up
        /// the usage key schedules in the project; returns (ok, message). Blocking — run off the
        /// UI thread.</param>
        public AreaSubmissionViewModel(
            Func<IReadOnlyList<(long Id, AreaRecord Rec)>> extract,
            Action<long> selectInModel,
            Func<IReadOnlyList<AreaRecord>, AreaSubmissionConfig, (bool ok, string msg)> export,
            Func<(bool ok, string msg)>? setupUsageKeys = null)
        {
            _extract = extract;
            _selectInModel = selectInModel;
            _export = export;
            _setupUsageKeys = setupUsageKeys;
            _dispatcher = Dispatcher.CurrentDispatcher;

            ShowConfigCommand = new RelayCommand(() => CurrentPane = SubmissionPane.Config);
            RefreshCommand    = new RelayCommand(Refresh);
            ExportCommand     = new RelayCommand(Export);
            BrowseOutputCommand = new RelayCommand(BrowseOutput);
            SetupUsageKeysCommand = new RelayCommand(SetupUsageKeys);

            var savedOutputFolder = ConfigManager.LoadConfig().AreaCalcOutputFolder;
            if (!string.IsNullOrWhiteSpace(savedOutputFolder))
                Config.OutputFolder = savedOutputFolder;

            _buildingNoText = Config.BuildingNo.ToString();
            _scaleText = Config.Scale.ToString();
            _fileBaseNameText = Config.FileBaseName;
        }

        public AreaSubmissionConfig Config { get; } = new AreaSubmissionConfig();
        public ObservableCollection<AreaLevelGroupViewModel> Levels { get; } = new();

        public ICommand ShowConfigCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand SetupUsageKeysCommand { get; }

        /// <summary>Raised after an export attempt so the window can show a result dialog.</summary>
        public event Action<bool, string>? ExportCompleted;

        public SubmissionPane CurrentPane
        {
            get => _currentPane;
            set
            {
                SetProperty(ref _currentPane, value);
                OnPropertyChanged(nameof(IsConfigPane));
                OnPropertyChanged(nameof(IsAreasPane));
                OnPropertyChanged(nameof(CurrentPaneIndex));
            }
        }

        public bool IsConfigPane => _currentPane == SubmissionPane.Config;
        public bool IsAreasPane  => _currentPane == SubmissionPane.Areas;

        /// <summary>Zero-based index mirror of <see cref="CurrentPane"/> for two-way binding to
        /// the Settings/Areas <c>TabControl.SelectedIndex</c> (so clicking a tab keeps
        /// <see cref="CurrentPane"/> in sync, and code that sets <see cref="CurrentPane"/>
        /// programmatically — e.g. <see cref="Refresh"/> — still switches the visible tab).</summary>
        public int CurrentPaneIndex
        {
            get => (int)_currentPane;
            set => CurrentPane = (SubmissionPane)value;
        }

        /// <summary>Notifying wrapper over <see cref="AreaSubmissionConfig.OutputFolder"/> so the
        /// Browse button can update the bound textbox.</summary>
        public string OutputFolder
        {
            get => Config.OutputFolder;
            set
            {
                if (Config.OutputFolder == value) return;
                Config.OutputFolder = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OutputFolderInvalid));
                OnPropertyChanged(nameof(HasInvalidSettings));

                var appConfig = ConfigManager.LoadConfig();
                appConfig.AreaCalcOutputFolder = value;
                ConfigManager.SaveConfig(appConfig);
            }
        }

        /// <summary>True when no output folder is set — blocks export (mirrors
        /// <c>AreaValidator.CheckConfig</c>'s "Output folder is not set" rule).</summary>
        public bool OutputFolderInvalid => string.IsNullOrWhiteSpace(OutputFolder);

        /// <summary>Notifying wrapper over <see cref="AreaSubmissionConfig.BuildingNo"/> so the
        /// Settings tab can highlight it red when blank or non-positive. Kept as a string (rather
        /// than binding <c>Config.BuildingNo</c> directly) so an in-progress edit — e.g. the field
        /// briefly empty while retyping — doesn't fight WPF's built-in int type-conversion.</summary>
        public string BuildingNoText
        {
            get => _buildingNoText;
            set
            {
                SetProperty(ref _buildingNoText, value);
                Config.BuildingNo = int.TryParse(value, out var n) ? n : 0;
                OnPropertyChanged(nameof(BuildingNoInvalid));
                OnPropertyChanged(nameof(HasInvalidSettings));
            }
        }

        /// <summary>Mirrors <c>AreaValidator.CheckConfig</c>'s "Building number must be at least
        /// 1" rule.</summary>
        public bool BuildingNoInvalid => !int.TryParse(_buildingNoText, out var n) || n < 1;

        /// <summary>Notifying wrapper over <see cref="AreaSubmissionConfig.Scale"/> (the
        /// DWFX_SCALE value) so the Settings tab can highlight it red when blank or non-positive.</summary>
        public string ScaleText
        {
            get => _scaleText;
            set
            {
                SetProperty(ref _scaleText, value);
                Config.Scale = int.TryParse(value, out var n) ? n : 0;
                OnPropertyChanged(nameof(ScaleInvalid));
                OnPropertyChanged(nameof(HasInvalidSettings));
            }
        }

        /// <summary>Mirrors <c>AreaValidator.CheckConfig</c>'s "Scale must be greater than zero"
        /// rule.</summary>
        public bool ScaleInvalid => !int.TryParse(_scaleText, out var n) || n <= 0;

        /// <summary>Notifying wrapper over <see cref="AreaSubmissionConfig.FileBaseName"/> so the
        /// Settings tab can highlight it red when blank.</summary>
        public string FileBaseNameText
        {
            get => _fileBaseNameText;
            set
            {
                SetProperty(ref _fileBaseNameText, value);
                Config.FileBaseName = value;
                OnPropertyChanged(nameof(FileBaseNameInvalid));
                OnPropertyChanged(nameof(HasInvalidSettings));
            }
        }

        /// <summary>Mirrors <c>AreaValidator.CheckConfig</c>'s "File base name is not set" rule.</summary>
        public bool FileBaseNameInvalid => string.IsNullOrWhiteSpace(_fileBaseNameText);

        /// <summary>True when any Settings field required for export is missing or invalid —
        /// drives the Settings tab's warning badge. Deliberately mirrors the same four checks as
        /// <c>AreaValidator.CheckConfig</c> so the UI and the export-time check never disagree
        /// about what counts as incomplete.</summary>
        public bool HasInvalidSettings =>
            BuildingNoInvalid || ScaleInvalid || FileBaseNameInvalid || OutputFolderInvalid;

        /// <summary>Count of area rows currently flagged with an error — drives the Areas tab's
        /// count badge and its summary banner. Recomputed (via <see cref="OnPropertyChanged"/>)
        /// whenever <see cref="Refresh"/> repopulates <see cref="Levels"/>.</summary>
        public int FlaggedCount => Levels.SelectMany(g => g.Rows).Count(r => r.HasError);

        /// <summary>Total area rows currently loaded — used by the Areas tab's summary banner
        /// text ("N of M areas flagged").</summary>
        public int TotalAreaCount => Levels.SelectMany(g => g.Rows).Count();

        /// <summary>"Official" marker radio — Form A, the spec's block/ATTRIB encoding
        /// (docs/autoarea/rishui-zamin-rules.md §5). Mutually exclusive with
        /// <see cref="UseOldMarkers"/>.</summary>
        public bool UseOfficialMarkers
        {
            get => Config.MarkerForm == MarkerForm.FormA;
            set
            {
                if (value && Config.MarkerForm != MarkerForm.FormA)
                {
                    Config.MarkerForm = MarkerForm.FormA;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UseOldMarkers));
                }
            }
        }

        /// <summary>"Old" marker radio — Form B, the tekenplus plain-TEXT encoding RVTuk emitted
        /// before the Official option existed (kept for comparison/fallback).</summary>
        public bool UseOldMarkers
        {
            get => Config.MarkerForm == MarkerForm.FormB;
            set
            {
                if (value && Config.MarkerForm != MarkerForm.FormB)
                {
                    Config.MarkerForm = MarkerForm.FormB;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UseOfficialMarkers));
                }
            }
        }

        public AreaRowViewModel? SelectedRow
        {
            get => _selectedRow;
            set
            {
                SetProperty(ref _selectedRow, value);
                if (value != null)
                {
                    try { _selectInModel(value.ElementId); }
                    catch { /* selection is best-effort; never crash the window */ }
                }
            }
        }

        // Extraction goes through a Revit ExternalEvent ping-pong that blocks the calling thread
        // until Revit's main thread services it. This window lives ON the main thread, so we must
        // run the extract off-thread (else the main thread blocks waiting for itself) and marshal
        // the results back to the Dispatcher — same pattern as the Family Browser's Sync.
        private void Refresh()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                IReadOnlyList<(long Id, AreaRecord Rec)> extracted;
                try { extracted = _extract(); }
                catch (Exception ex)
                {
                    _dispatcher.Invoke(() => ExportCompleted?.Invoke(false, "Could not read the open sheet: " + ex.Message));
                    return;
                }

                var groups = extracted
                    .GroupBy(e => e.Rec.Floor ?? string.Empty)
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g =>
                    {
                        var vm = new AreaLevelGroupViewModel(g.Key);
                        foreach (var (id, rec) in g.OrderBy(e => e.Rec.Number, StringComparer.OrdinalIgnoreCase))
                            vm.Rows.Add(new AreaRowViewModel(id, rec));
                        return vm;
                    })
                    .ToList();

                _dispatcher.Invoke(() =>
                {
                    Levels.Clear();
                    foreach (var g in groups) Levels.Add(g);
                    OnPropertyChanged(nameof(FlaggedCount));
                    OnPropertyChanged(nameof(TotalAreaCount));
                    CurrentPane = SubmissionPane.Areas;
                    if (extracted.Count == 0)
                        ExportCompleted?.Invoke(false, "No areas found on the open sheet's area plan(s).");
                });
            });
        }

        private void Export()
        {
            var records = Levels.SelectMany(g => g.Rows).Select(r => r.Record).ToList();
            if (records.Count == 0)
            {
                ExportCompleted?.Invoke(false, "No areas loaded — click Refresh first.");
                return;
            }
            var (ok, msg) = _export(records, Config);
            ExportCompleted?.Invoke(ok, msg);
        }

        /// <summary>Creates the usage key schedules / area parameters in the project. Blocks on
        /// Revit's main thread, so it runs on the thread pool like <see cref="Refresh"/>.</summary>
        private void SetupUsageKeys()
        {
            if (_setupUsageKeys == null)
            {
                ExportCompleted?.Invoke(false, "Usage key setup is not available in this context.");
                return;
            }

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                (bool ok, string msg) result;
                try { result = _setupUsageKeys(); }
                catch (Exception ex) { result = (false, "Usage key setup failed: " + ex.Message); }

                _dispatcher.Invoke(() => ExportCompleted?.Invoke(result.ok, result.msg));
            });
        }

        private void BrowseOutput()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the output folder for the .dxf / .dat files",
                SelectedPath = System.IO.Directory.Exists(OutputFolder) ? OutputFolder : string.Empty
            };
            if (dialog.ShowDialog() == DialogResult.OK)
                OutputFolder = dialog.SelectedPath;
        }
    }
}
