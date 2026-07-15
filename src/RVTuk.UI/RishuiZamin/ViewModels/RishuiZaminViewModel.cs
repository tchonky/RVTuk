using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Threading;
using RVTuk.Core.RishuiZamin;
using RVTuk.Core.Shared.Config;

using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.RishuiZamin.ViewModels
{
    public enum SubmissionPane { Areas, Config }

    /// <summary>
    /// View model for the Area Calc (Rishui Zamin) window. Top toolbar switches the bottom pane
    /// between the Config fields and the area tree; Revit behaviour is injected as delegates so
    /// this project takes no Revit dependency.
    /// </summary>
    public class RishuiZaminViewModel : ViewModelBase
    {
        private readonly Func<IReadOnlyList<(long Id, AreaRecord Rec)>> _extract;
        private readonly Action<long> _selectInModel;
        private readonly Func<IReadOnlyList<AreaRecord>, RishuiZaminConfig, (bool ok, string msg)> _export;
        private readonly Func<(bool ok, string msg)>? _setupUsageKeys;
        private readonly Dispatcher _dispatcher;

        private SubmissionPane _currentPane = SubmissionPane.Areas;
        private AreaRowViewModel? _selectedRow;
        private string _buildingNoText = "";
        private string _fileBaseNameText = "";
        private bool _scaleMismatch;
        private bool _isRefreshing;

        /// <param name="extract">Reads the areas on the open sheet (id + record).</param>
        /// <param name="selectInModel">Selects an Area in the model by element id.</param>
        /// <param name="export">Validates + writes the DXF/DAT for the given records; returns (ok, message).</param>
        /// <param name="setupUsageKeys">Binds the robot's area text parameters and creates/tops-up
        /// the usage key schedules in the project; returns (ok, message). Blocking — run off the
        /// UI thread.</param>
        public RishuiZaminViewModel(
            Func<IReadOnlyList<(long Id, AreaRecord Rec)>> extract,
            Action<long> selectInModel,
            Func<IReadOnlyList<AreaRecord>, RishuiZaminConfig, (bool ok, string msg)> export,
            Func<(bool ok, string msg)>? setupUsageKeys = null)
        {
            _extract = extract;
            _selectInModel = selectInModel;
            _export = export;
            _setupUsageKeys = setupUsageKeys;
            _dispatcher = Dispatcher.CurrentDispatcher;

            RefreshCommand    = new RelayCommand(Refresh);
            ExportCommand     = new RelayCommand(Export);
            BrowseOutputCommand = new RelayCommand(BrowseOutput);
            SetupUsageKeysCommand = new RelayCommand(SetupUsageKeys);

            var savedConfig = ConfigManager.LoadConfig();
            if (!string.IsNullOrWhiteSpace(savedConfig.AreaCalcOutputFolder))
                Config.OutputFolder = savedConfig.AreaCalcOutputFolder;
            Config.MarkerForm = savedConfig.AreaCalcMarkerForm;

            _buildingNoText = Config.BuildingNo.ToString();
            _fileBaseNameText = Config.FileBaseName;
        }

        public RishuiZaminConfig Config { get; } = new RishuiZaminConfig();
        public ObservableCollection<AreaLevelGroupViewModel> Levels { get; } = new();

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
                OnPropertyChanged(nameof(CurrentPaneIndex));
            }
        }

        /// <summary>Zero-based index mirror of <see cref="CurrentPane"/> for two-way binding to
        /// the Settings/Areas <c>TabControl.SelectedIndex</c> (so clicking a tab keeps
        /// <see cref="CurrentPane"/> in sync, and code that sets <see cref="CurrentPane"/>
        /// programmatically — e.g. <see cref="Refresh"/> — still switches the visible tab).</summary>
        public int CurrentPaneIndex
        {
            get => (int)_currentPane;
            set => CurrentPane = (SubmissionPane)value;
        }

        /// <summary>Notifying wrapper over <see cref="RishuiZaminConfig.OutputFolder"/> so the
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

        /// <summary>Notifying wrapper over <see cref="RishuiZaminConfig.BuildingNo"/> so the
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

        /// <summary>The DWFX_SCALE value (e.g. 100 for 1:100), read automatically off the open
        /// sheet's area-plan viewport(s) during <see cref="Refresh"/> — no longer user-entered.</summary>
        public int DetectedScale => Config.Scale;

        /// <summary>True when the open sheet's area-plan viewports don't all share the same plot
        /// scale, so <see cref="DetectedScale"/> can't be trusted for the export. Drives a
        /// warning on the Settings tab.</summary>
        public bool ScaleMismatch
        {
            get => _scaleMismatch;
            private set
            {
                SetProperty(ref _scaleMismatch, value);
                OnPropertyChanged(nameof(HasInvalidSettings));
            }
        }

        /// <summary>Notifying wrapper over <see cref="RishuiZaminConfig.FileBaseName"/> so the
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
            BuildingNoInvalid || ScaleMismatch || FileBaseNameInvalid || OutputFolderInvalid;

        /// <summary>Count of area rows currently flagged with an error — drives the Areas tab's
        /// count badge and its summary banner. Recomputed (via <see cref="OnPropertyChanged"/>)
        /// whenever <see cref="Refresh"/> repopulates <see cref="Levels"/>.</summary>
        public int FlaggedCount => Levels.SelectMany(g => g.Rows).Count(r => r.HasError);

        /// <summary>Total area rows currently loaded — used by the Areas tab's summary banner
        /// text ("N of M areas flagged").</summary>
        public int TotalAreaCount => Levels.SelectMany(g => g.Rows).Count();

        /// <summary>"Official" marker radio — Form A, the spec's block/ATTRIB encoding
        /// (docs/tools/rishui-zamin/rules.md §5). Mutually exclusive with
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
                    SaveMarkerForm();
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
                    SaveMarkerForm();
                }
            }
        }

        private void SaveMarkerForm()
        {
            var appConfig = ConfigManager.LoadConfig();
            appConfig.AreaCalcMarkerForm = Config.MarkerForm;
            ConfigManager.SaveConfig(appConfig);
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
            if (_isRefreshing) return;   // a second click while one runs would just double-populate
            _isRefreshing = true;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                IReadOnlyList<(long Id, AreaRecord Rec)> extracted;
                try { extracted = _extract(); }
                catch (Exception ex)
                {
                    _dispatcher.Invoke(() =>
                    {
                        _isRefreshing = false;
                        ExportCompleted?.Invoke(false, "Could not read the open sheet: " + ex.Message);
                    });
                    return;
                }

                var groups = extracted
                    .GroupBy(e => e.Rec.Floor ?? string.Empty)
                    .OrderBy(g => g.First().Rec.LevelElevation)
                    .Select(g =>
                    {
                        var vm = new AreaLevelGroupViewModel(g.Key);
                        foreach (var (id, rec) in g.OrderBy(e => e.Rec.Number, StringComparer.OrdinalIgnoreCase))
                            vm.Rows.Add(new AreaRowViewModel(id, rec));
                        return vm;
                    })
                    .ToList();

                var scales = extracted.Select(e => e.Rec.Scale).Where(s => s > 0).Distinct().ToList();

                _dispatcher.Invoke(() =>
                {
                    _isRefreshing = false;
                    Levels.Clear();
                    foreach (var g in groups) Levels.Add(g);
                    OnPropertyChanged(nameof(FlaggedCount));
                    OnPropertyChanged(nameof(TotalAreaCount));

                    if (scales.Count > 0)
                    {
                        Config.Scale = scales[0];
                        ScaleMismatch = scales.Count > 1;
                        OnPropertyChanged(nameof(DetectedScale));
                    }

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
