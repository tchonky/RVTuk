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
    public enum SubmissionPane { Areas, Table, Config }

    /// <summary>
    /// View model for the Area Calc (Rishui Zamin) window. Top toolbar switches the bottom pane
    /// between the Config fields and the area tree; Revit behaviour is injected as delegates so
    /// this project takes no Revit dependency.
    /// </summary>
    public class RishuiZaminViewModel : ViewModelBase
    {
        private readonly Func<IReadOnlyList<(long Id, AreaRecord Rec)>> _extract;
        private readonly Action<IReadOnlyList<long>> _selectInModel;
        private readonly Func<IReadOnlyList<AreaRecord>, RishuiZaminConfig, (bool ok, string msg)> _export;
        private readonly Func<(bool ok, string msg)>? _setupUsageKeys;
        private readonly Func<UsageKeySlot, int?, IReadOnlyList<long>, bool, UsageAssignResult>? _assignUsage;
        private readonly Func<(string DocumentKey, PlotLimits Limits)>? _readPlotLimits;
        private readonly Func<PlotLimits, (bool ok, string msg)>? _savePlotLimits;
        private readonly Dispatcher _dispatcher;

        private SubmissionPane _currentPane = SubmissionPane.Areas;
        private IReadOnlyList<AreaRowViewModel> _selectedRows = Array.Empty<AreaRowViewModel>();
        private AreaSelectionSummary _selection = AreaSelectionSummary.From(Array.Empty<AreaRecord>());
        private UsageValueDisplay _newUsageDisplay = UsageValueDisplay.From(new UsageValue(UsageValueKind.None, null));
        private UsageValueDisplay _oldUsageDisplay = UsageValueDisplay.From(new UsageValue(UsageValueKind.None, null));
        private UsageTotals _sheetTotals = UsageTotals.Sum(Array.Empty<AreaRecord>());
        private string? _statusText;
        private bool _isAssigning;
        private string _buildingNoText = "";
        private string _fileBaseNameText = "";
        private bool _scaleMismatch;
        private bool _isRefreshing;
        private bool _isSavingLimits;
        private bool _commitAgain;
        private AreaTable _table = AreaTable.Empty;
        private PlotLimits _appliedLimits = PlotLimits.Empty;
        private PlotLimits _lastKnownLimits = PlotLimits.Empty;
        private string? _limitsDocKey;
        private bool _limitsUnsaved;
        private IReadOnlyList<PlotLimitChip> _limitChips = Array.Empty<PlotLimitChip>();
        private string? _plotHint;
        private string? _tableStatusText;

        /// <param name="extract">Reads the areas on the open sheet (id + record).</param>
        /// <param name="selectInModel">Selects Areas in the model by element id.</param>
        /// <param name="export">Validates + writes the DXF/DAT for the given records; returns (ok, message).</param>
        /// <param name="setupUsageKeys">Binds the robot's area text parameters and creates/tops-up
        /// the usage key schedules in the project; returns (ok, message). Blocking — run off the
        /// UI thread.</param>
        /// <param name="assignUsage">Sets the New/Old usage key (code, or null for none) on the
        /// areas, running Setup Usage Keys first when the last argument is true; one undo step.
        /// Blocking — run off the UI thread.</param>
        /// <param name="readPlotLimits">The Table tab's plot area and limits stored in the project
        /// the last extract read, with a key identifying that project. Called right after
        /// <paramref name="extract"/>, on the same background thread.</param>
        /// <param name="savePlotLimits">Stores the plot area and limits in that project (one undo
        /// step); returns (ok, message). Blocking — run off the UI thread.</param>
        public RishuiZaminViewModel(
            Func<IReadOnlyList<(long Id, AreaRecord Rec)>> extract,
            Action<IReadOnlyList<long>> selectInModel,
            Func<IReadOnlyList<AreaRecord>, RishuiZaminConfig, (bool ok, string msg)> export,
            Func<(bool ok, string msg)>? setupUsageKeys = null,
            Func<UsageKeySlot, int?, IReadOnlyList<long>, bool, UsageAssignResult>? assignUsage = null,
            Func<(string DocumentKey, PlotLimits Limits)>? readPlotLimits = null,
            Func<PlotLimits, (bool ok, string msg)>? savePlotLimits = null)
        {
            _extract = extract;
            _selectInModel = selectInModel;
            _export = export;
            _setupUsageKeys = setupUsageKeys;
            _assignUsage = assignUsage;
            _readPlotLimits = readPlotLimits;
            _savePlotLimits = savePlotLimits;
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

        /// <summary>Every row in list order (level by level), for the grouped list.</summary>
        public ObservableCollection<AreaRowViewModel> AllRows { get; } = new();

        public ICommand RefreshCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand BrowseOutputCommand { get; }
        public ICommand SetupUsageKeysCommand { get; }

        /// <summary>Raised after an export attempt so the window can show a result dialog.</summary>
        public event Action<bool, string>? ExportCompleted;

        /// <summary>Raised when setting a usage changed nothing, with the reason.</summary>
        public event Action<string>? AssignFailed;

        /// <summary>Asks the user whether to run Setup Usage Keys first (message from the
        /// result); set by the window. Null means no.</summary>
        public Func<string, bool>? ConfirmRunSetup { get; set; }

        /// <summary>m² per usage group for every area on the sheet.</summary>
        public UsageTotals SheetTotals
        {
            get => _sheetTotals;
            private set
            {
                SetProperty(ref _sheetTotals, value);
                OnPropertyChanged(nameof(HasUncoded));
            }
        }

        /// <summary>True while some area has no usage group — shows the red "No code" box.</summary>
        public bool HasUncoded => _sheetTotals.NoCodeCount > 0;

        /// <summary>The selected areas, in list order.</summary>
        public IReadOnlyList<AreaRowViewModel> SelectedRows
        {
            get => _selectedRows;
            private set => SetProperty(ref _selectedRows, value);
        }

        /// <summary>What the detail strip says about <see cref="SelectedRows"/>.</summary>
        public AreaSelectionSummary Selection
        {
            get => _selection;
            private set => SetProperty(ref _selection, value);
        }

        /// <summary>The New usage picker button's text and colour.</summary>
        public UsageValueDisplay NewUsageDisplay
        {
            get => _newUsageDisplay;
            private set => SetProperty(ref _newUsageDisplay, value);
        }

        /// <summary>The Old usage picker button's text and colour.</summary>
        public UsageValueDisplay OldUsageDisplay
        {
            get => _oldUsageDisplay;
            private set => SetProperty(ref _oldUsageDisplay, value);
        }

        /// <summary>The status line under the detail strip, after a usage was set.</summary>
        public string? StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        /// <summary>True while a refresh or a usage assignment runs.</summary>
        public bool IsBusy => _isRefreshing || _isAssigning || _isSavingLimits;

        public bool CanAssign => _assignUsage != null && SelectedRows.Count > 0 && !IsBusy;

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

        /// <summary>The sheet's plot scale (e.g. 100 for 1:100), read automatically off the open
        /// sheet's area-plan viewport(s) during <see cref="Refresh"/> — no longer user-entered.
        /// Not the .dat's DWFX_SCALE, which is fixed (see <c>DatWriter</c>).</summary>
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

        /// <summary>Call after the list's selection changed (the rows' <see cref="AreaRowViewModel.IsSelected"/>):
        /// updates the detail strip and selects the same areas in the model.</summary>
        public void OnSelectionChanged()
        {
            UpdateSelection();
            try { _selectInModel(SelectedRows.Select(r => r.ElementId).ToList()); }
            catch { /* selection is best-effort; never crash the window */ }
        }

        private void UpdateSelection()
        {
            SelectedRows = Levels.SelectMany(g => g.Rows).Where(r => r.IsSelected).ToList();
            Selection = AreaSelectionSummary.From(SelectedRows.Select(r => r.Record).ToList());
            NewUsageDisplay = UsageValueDisplay.From(Selection.New);
            OldUsageDisplay = UsageValueDisplay.From(Selection.Old);
            OnPropertyChanged(nameof(CanAssign));
        }

        private void SetBusy(ref bool flag, bool value)
        {
            flag = value;
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(CanAssign));
        }

        // ── Table tab ────────────────────────────────────────────────────────

        /// <summary>The Table tab's floors × codes, rebuilt on every read of the sheet.</summary>
        public AreaTable Table
        {
            get => _table;
            private set => SetProperty(ref _table, value);
        }

        public PlotLimitBoxViewModel PlotAreaBox { get; } = new();
        public PlotLimitBoxViewModel PrimaryLimitBox { get; } = new();
        public PlotLimitBoxViewModel ServiceLimitBox { get; } = new();
        public PlotLimitBoxViewModel TotalLimitBox { get; } = new();

        /// <summary>The plot area the table's % cells and the chips use (the last committed or
        /// loaded value, saved or not).</summary>
        public double? PlotAreaM2 => _appliedLimits.PlotAreaM2;

        /// <summary>Primary, Service and Total against the plan's allowed % of plot.</summary>
        public IReadOnlyList<PlotLimitChip> LimitChips
        {
            get => _limitChips;
            private set => SetProperty(ref _limitChips, value);
        }

        /// <summary>"Type the plot area…" while there is no plot area; otherwise null.</summary>
        public string? PlotHint
        {
            get => _plotHint;
            private set => SetProperty(ref _plotHint, value);
        }

        /// <summary>The Table tab's status line: saved, or why not.</summary>
        public string? TableStatusText
        {
            get => _tableStatusText;
            private set => SetProperty(ref _tableStatusText, value);
        }

        /// <summary>True while the boxes hold values that could not be saved to the project.</summary>
        public bool LimitsUnsaved
        {
            get => _limitsUnsaved;
            private set => SetProperty(ref _limitsUnsaved, value);
        }

        /// <summary>Raised after the table, the plot area or the chips change, so the view can
        /// rebuild its grid.</summary>
        public event Action? TableChanged;

        private PlotLimits TypedLimits() => new PlotLimits(PlotAreaBox.Value, PrimaryLimitBox.Value,
            ServiceLimitBox.Value, TotalLimitBox.Value);

        private void RecomputeLimits()
        {
            LimitChips = PlotLimitCheck.Chips(Table, _appliedLimits);
            PlotHint = _appliedLimits.PlotAreaM2 is > 0 ? null : PlotLimitCheck.NoPlotHint;
            OnPropertyChanged(nameof(PlotAreaM2));
            TableChanged?.Invoke();
        }

        /// <summary>Takes the values stored in the project just read — unless the boxes hold
        /// unsaved (or still saving) values typed for that same project. Another project's values
        /// always replace them, so one project's numbers never show against another.</summary>
        private void ApplyStoredLimits(string documentKey, PlotLimits stored)
        {
            var sameDocument = documentKey == _limitsDocKey;
            _limitsDocKey = documentKey;
            if (sameDocument && (LimitsUnsaved || _isSavingLimits)) return;

            if (!sameDocument) TableStatusText = null;
            PlotAreaBox.Load(stored.PlotAreaM2);
            PrimaryLimitBox.Load(stored.PrimaryPercent);
            ServiceLimitBox.Load(stored.ServicePercent);
            TotalLimitBox.Load(stored.TotalPercent);
            _appliedLimits = stored;
            _lastKnownLimits = stored;
            LimitsUnsaved = false;
            RecomputeLimits();
        }

        /// <summary>Applies the boxes (Enter or leaving a box) and, when they changed, saves them
        /// in the project. Does nothing while a box is invalid.</summary>
        public void CommitPlotLimits()
        {
            if (PlotAreaBox.IsInvalid || PrimaryLimitBox.IsInvalid || ServiceLimitBox.IsInvalid || TotalLimitBox.IsInvalid)
                return;
            var typed = TypedLimits();
            if (typed != _appliedLimits)
            {
                _appliedLimits = typed;
                RecomputeLimits();
            }

            if (_savePlotLimits == null || typed == _lastKnownLimits) return;
            if (_isSavingLimits) { _commitAgain = true; return; }

            SetBusy(ref _isSavingLimits, true);
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                (bool ok, string msg) result;
                try { result = _savePlotLimits(typed); }
                catch (Exception ex) { result = (false, ex.Message); }

                _dispatcher.Invoke(() =>
                {
                    SetBusy(ref _isSavingLimits, false);
                    if (result.ok)
                    {
                        _lastKnownLimits = typed;
                        LimitsUnsaved = false;
                        TableStatusText = "Plot area and limits saved in the project · one undo step in Revit";
                    }
                    else
                    {
                        LimitsUnsaved = true;
                        TableStatusText = "Not saved to the project: " + result.msg;
                    }

                    if (_commitAgain)
                    {
                        _commitAgain = false;
                        CommitPlotLimits();
                    }
                });
            });
        }

        /// <summary>Fills the list from extracted areas (on the UI thread), keeping the rows that
        /// were selected — by element id — selected.</summary>
        public void Populate(IReadOnlyList<(long Id, AreaRecord Rec)> extracted)
        {
            var keep = new HashSet<long>(SelectedRows.Select(r => r.ElementId));
            var collapsed = new HashSet<string>(Levels.Where(l => !l.IsExpanded).Select(l => l.LevelName));

            var groups = extracted
                .GroupBy(e => e.Rec.Floor ?? string.Empty)
                .OrderBy(g => g.First().Rec.LevelElevation)
                .Select(g =>
                {
                    var vm = new AreaLevelGroupViewModel(g.Key);
                    vm.IsExpanded = !collapsed.Contains(vm.LevelName);
                    foreach (var (id, rec) in g.OrderBy(e => e.Rec.Number, StringComparer.OrdinalIgnoreCase))
                        vm.Rows.Add(new AreaRowViewModel(id, rec) { IsSelected = keep.Contains(id), Level = vm });
                    return vm;
                })
                .ToList();

            Levels.Clear();
            foreach (var g in groups) Levels.Add(g);
            AllRows.Clear();
            foreach (var row in groups.SelectMany(g => g.Rows)) AllRows.Add(row);
            OnPropertyChanged(nameof(FlaggedCount));
            OnPropertyChanged(nameof(TotalAreaCount));
            SheetTotals = UsageTotals.Sum(extracted.Select(e => e.Rec));
            Table = AreaTable.Build(extracted.Select(e => e.Rec));
            RecomputeLimits();
            UpdateSelection();

            var scales = extracted.Select(e => e.Rec.Scale).Where(s => s > 0).Distinct().ToList();
            if (scales.Count > 0)
            {
                Config.Scale = scales[0];
                ScaleMismatch = scales.Count > 1;
                OnPropertyChanged(nameof(DetectedScale));
            }
        }

        // Extraction goes through a Revit ExternalEvent ping-pong that blocks the calling thread
        // until Revit's main thread services it. This window lives ON the main thread, so we must
        // run the extract off-thread (else the main thread blocks waiting for itself) and marshal
        // the results back to the Dispatcher — same pattern as the Family Browser's Sync.
        private void Refresh()
        {
            if (_isRefreshing) return;   // a second click while one runs would just double-populate
            SetBusy(ref _isRefreshing, true);
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                IReadOnlyList<(long Id, AreaRecord Rec)> extracted;
                (string DocumentKey, PlotLimits Limits)? stored = null;
                try
                {
                    extracted = _extract();
                    stored = _readPlotLimits?.Invoke();
                }
                catch (Exception ex)
                {
                    _dispatcher.Invoke(() =>
                    {
                        SetBusy(ref _isRefreshing, false);
                        ExportCompleted?.Invoke(false, "Could not read the open sheet: " + ex.Message);
                    });
                    return;
                }

                _dispatcher.Invoke(() =>
                {
                    if (stored is { } read) ApplyStoredLimits(read.DocumentKey, read.Limits);
                    Populate(extracted);
                    SetBusy(ref _isRefreshing, false);
                    if (CurrentPane == SubmissionPane.Config) CurrentPane = SubmissionPane.Areas;
                    if (extracted.Count == 0)
                        ExportCompleted?.Invoke(false, "No areas found on the open sheet's area plan(s).");
                });
            });
        }

        /// <summary>Sets the New or Old usage (a code, or null for none) on the selected areas,
        /// then refreshes the sheet. When the key schedules are missing, asks
        /// <see cref="ConfirmRunSetup"/> whether to set them up first.</summary>
        public void Assign(UsageKeySlot slot, int? code)
        {
            if (!CanAssign) return;
            var ids = SelectedRows.Select(r => r.ElementId).ToList();
            StatusText = null;   // never leave an earlier "done" line next to this attempt's outcome
            SetBusy(ref _isAssigning, true);
            RunAssign(slot, code, ids, runSetupFirst: false);
        }

        private void RunAssign(UsageKeySlot slot, int? code, IReadOnlyList<long> ids, bool runSetupFirst)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                UsageAssignResult result;
                try { result = _assignUsage!(slot, code, ids, runSetupFirst); }
                catch (Exception ex) { result = new UsageAssignResult(UsageAssignStatus.Failed, "Setting the usage failed: " + ex.Message); }

                _dispatcher.Invoke(() => HandleAssignResult(result, slot, code, ids, runSetupFirst));
            });
        }

        private void HandleAssignResult(UsageAssignResult result, UsageKeySlot slot, int? code,
            IReadOnlyList<long> ids, bool ranSetup)
        {
            if (result.Status == UsageAssignStatus.KeysMissing && !ranSetup)
            {
                if (ConfirmRunSetup?.Invoke(result.Message) == true)
                    RunAssign(slot, code, ids, runSetupFirst: true);
                else
                    SetBusy(ref _isAssigning, false);
                return;
            }

            SetBusy(ref _isAssigning, false);
            if (result.Status == UsageAssignStatus.Done)
                StatusText = FormatAssignStatus(slot, code, ids.Count);
            else
                AssignFailed?.Invoke(result.Message);
            Refresh();
        }

        /// <summary>The status line after a usage was set, e.g. "New usage 110 - מרפסת on 2
        /// areas · one undo step in Revit".</summary>
        public static string FormatAssignStatus(UsageKeySlot slot, int? code, int count)
        {
            var what = slot == UsageKeySlot.New ? "New usage" : "Old usage";
            var value = code == null
                ? " cleared"
                : " " + code.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " - " +
                  (UsageCatalog.ByCode(code.Value)?.HebrewName ?? "");
            var areas = count == 1 ? "1 area" : count + " areas";
            return $"{what}{value} on {areas} · one undo step in Revit";
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
