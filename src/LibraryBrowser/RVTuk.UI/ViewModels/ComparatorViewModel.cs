using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using RVTuk.Core.Comparison;
using RVTuk.Core.Models.Comparison;
using RVTuk.Core.Reporting;
using RVTuk.UI.Views;

namespace RVTuk.UI.ViewModels
{
    /// <summary>Top-level Comparator view model. All Revit access is injected as delegates that
    /// return Core types, so this project never references the Revit API.</summary>
    public class ComparatorViewModel : ViewModelBase
    {
        public const string ActiveDocLabel = "(active document)";
        public const string StandardLabel = "The Standard";

        private readonly Func<IReadOnlyList<string>> _getOpenDocuments;
        private readonly Func<string?, CapturedSnapshot> _captureOpenDoc; // null = active
        private readonly Func<string, CapturedSnapshot> _captureFile;
        private readonly Func<string?> _pickFile;
        private readonly Action<string> _saveReportHtml;
        private readonly Func<StandardSnapshot> _loadStandard;
        private readonly Action<StandardSnapshot> _saveStandard;
        private readonly Action<CapturedSnapshot> _saveProjectSnapshot;
        private readonly Func<IReadOnlyList<SnapshotMeta>> _listSavedSnapshots;
        private readonly Func<long, CapturedSnapshot> _loadSavedSnapshot;
        private readonly Action<long> _deleteSavedSnapshot;

        // A saved snapshot's combo entry is labeled "name (captured date)" and resolves to its DB
        // id through this map — so CaptureSide works off whatever text is currently selected with
        // no separate per-slot state to keep in sync (retyping/re-picking just misses the lookup
        // and falls through to the normal live-capture heuristics).
        private readonly Dictionary<string, long> _savedSnapshotIdsByLabel = new();

        private readonly ComparisonEngine _engine;
        private readonly StandardCurator _curator;
        private StandardSnapshot _standard;

        private CapturedSnapshot? _capturedA;
        private CapturedSnapshot? _capturedB;
        private ComparisonResult? _lastResult;
        private bool _busy;

        public ComparatorViewModel(
            Func<IReadOnlyList<string>> getOpenDocuments,
            Func<string?, CapturedSnapshot> captureOpenDoc,
            Func<string, CapturedSnapshot> captureFile,
            Func<string?> pickFile,
            Action<string> saveReportHtml,
            Func<StandardSnapshot> loadStandard,
            Action<StandardSnapshot> saveStandard,
            Action<CapturedSnapshot> saveProjectSnapshot,
            Func<IReadOnlyList<SnapshotMeta>> listSavedSnapshots,
            Func<long, CapturedSnapshot> loadSavedSnapshot,
            Action<long> deleteSavedSnapshot)
        {
            _getOpenDocuments = getOpenDocuments;
            _captureOpenDoc = captureOpenDoc;
            _captureFile = captureFile;
            _pickFile = pickFile;
            _saveReportHtml = saveReportHtml;
            _loadStandard = loadStandard;
            _saveStandard = saveStandard;
            _saveProjectSnapshot = saveProjectSnapshot;
            _listSavedSnapshots = listSavedSnapshots;
            _loadSavedSnapshot = loadSavedSnapshot;
            _deleteSavedSnapshot = deleteSavedSnapshot;

            var registry = new CategoryRegistry();
            registry.Register(new ViewTemplateComparer());
            _engine = new ComparisonEngine(registry);
            _curator = new StandardCurator(new ICategoryMerger[] { new ViewTemplateMerger() });
            _standard = _loadStandard();

            ViewTemplates = new ViewTemplatesCategoryViewModel();
            Categories = new ObservableCollection<CategoryViewModelBase>
            {
                ViewTemplates,
                new PlaceholderCategoryViewModel("BrowserOrg", "Browser Organization"),
                new PlaceholderCategoryViewModel("Parameters", "Parameters"),
                new PlaceholderCategoryViewModel("Schedules", "Schedules"),
                new PlaceholderCategoryViewModel("Sheets", "Sheets"),
            };
            SelectedCategory = ViewTemplates;

            SourceAOptions = new ObservableCollection<string>();
            SourceBOptions = new ObservableCollection<string>();
            RefreshDocuments();
            SourceA = ActiveDocLabel;
            SourceB = StandardLabel;

            CompareCommand = new RelayCommand(RunCompare, () => !_busy);
            ExportCommand = new RelayCommand(ExportReport, () => _lastResult != null && !_busy);
            RefreshDocumentsCommand = new RelayCommand(RefreshDocuments, () => !_busy);
            BrowseACommand = new RelayCommand(() => BrowseInto(v => SourceA = v), () => !_busy);
            BrowseBCommand = new RelayCommand(() => BrowseInto(v => SourceB = v), () => !_busy);
            // Gate on the captured snapshot's own metadata (not the combo's current text): "The
            // Standard" already persists through its own save path, and saveProjectSnapshot does
            // not force SourceKind="Project" the way saveStandard forces "Standard" — without
            // this check, saving Source B in its default state (Standard) would silently insert
            // a hidden, permanently-orphaned "Standard" row invisible to the saved-snapshot picker.
            SaveSnapshotACommand = new RelayCommand(() => SaveSlot(_capturedA), () => !_busy && _capturedA != null && _capturedA.Meta.SourceKind != "Standard");
            SaveSnapshotBCommand = new RelayCommand(() => SaveSlot(_capturedB), () => !_busy && _capturedB != null && _capturedB.Meta.SourceKind != "Standard");
            LoadSavedSnapshotACommand = new RelayCommand(() => PickSaved(label => SourceA = label), () => !_busy);
            LoadSavedSnapshotBCommand = new RelayCommand(() => PickSaved(label => SourceB = label), () => !_busy);

            StatusText = "Select two sources and press Compare.";
        }

        public ObservableCollection<CategoryViewModelBase> Categories { get; }
        public ViewTemplatesCategoryViewModel ViewTemplates { get; }

        public ObservableCollection<string> SourceAOptions { get; }
        public ObservableCollection<string> SourceBOptions { get; }

        public Array Modes => Enum.GetValues(typeof(ComparatorMode));

        private ComparatorMode _mode = ComparatorMode.BuildTemplate;
        public ComparatorMode Mode { get => _mode; set => SetProperty(ref _mode, value); }

        private CategoryViewModelBase? _selectedCategory;
        public CategoryViewModelBase? SelectedCategory
        {
            get => _selectedCategory;
            set => SetProperty(ref _selectedCategory, value);
        }

        private string _sourceA = ActiveDocLabel;
        public string SourceA { get => _sourceA; set => SetProperty(ref _sourceA, value); }

        private string _sourceB = StandardLabel;
        public string SourceB { get => _sourceB; set => SetProperty(ref _sourceB, value); }

        private string _statusText = "";
        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

        public ICommand CompareCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand RefreshDocumentsCommand { get; }
        public ICommand BrowseACommand { get; }
        public ICommand BrowseBCommand { get; }
        public ICommand SaveSnapshotACommand { get; }
        public ICommand SaveSnapshotBCommand { get; }
        public ICommand LoadSavedSnapshotACommand { get; }
        public ICommand LoadSavedSnapshotBCommand { get; }

        private void RefreshDocuments()
        {
            var docs = _getOpenDocuments();
            SourceAOptions.Clear();
            SourceBOptions.Clear();
            SourceAOptions.Add(ActiveDocLabel);
            SourceBOptions.Add(StandardLabel);
            foreach (var d in docs)
            {
                SourceAOptions.Add(d);
                SourceBOptions.Add(d);
            }
        }

        private void BrowseInto(Action<string> set)
        {
            var path = _pickFile();
            if (!string.IsNullOrEmpty(path))
            {
                if (!SourceAOptions.Contains(path!)) SourceAOptions.Add(path!);
                if (!SourceBOptions.Contains(path!)) SourceBOptions.Add(path!);
                set(path!);
            }
        }

        private void RunCompare()
        {
            _busy = true;
            StatusText = "Comparing…";
            Task.Run(() =>
            {
                try
                {
                    var a = CaptureSide(SourceA);
                    var b = CaptureSide(SourceB);
                    if (!string.IsNullOrEmpty(a.Error) || !string.IsNullOrEmpty(b.Error))
                    {
                        SetStatusOnUi("Error: " + (a.Error ?? b.Error));
                        return;
                    }

                    _capturedA = a;
                    _capturedB = b;
                    var result = _engine.Compare(a.Meta, b.Meta, a.Categories, b.Categories);
                    _lastResult = result;
                    OnUi(() => Populate(result));
                }
                catch (Exception ex)
                {
                    SetStatusOnUi("Error: " + ex.Message);
                }
                finally
                {
                    _busy = false;
                }
            });
        }

        private CapturedSnapshot CaptureSide(string selection)
        {
            if (selection == StandardLabel)
                return new CapturedSnapshot { Meta = _standard.Meta, Categories = _standard.Categories };
            if (_savedSnapshotIdsByLabel.TryGetValue(selection, out var savedId))
                return _loadSavedSnapshot(savedId);
            if (selection == ActiveDocLabel)
                return _captureOpenDoc(null);
            if (LooksLikeFile(selection))
                return _captureFile(selection);
            return _captureOpenDoc(selection);
        }

        /// <summary>Persists a captured slot (already resolved by a Compare run) as a named,
        /// reloadable snapshot. Does nothing to Revit — same "report-only" guarantee as the rest
        /// of the Comparator.</summary>
        private void SaveSlot(CapturedSnapshot? captured)
        {
            if (captured == null) return;
            _saveProjectSnapshot(captured);
            StatusText = $"Saved snapshot \"{captured.Meta.SourceName}\" (captured {FormatCapturedUtc(captured.Meta.CapturedUtc)}).";
        }

        /// <summary>Opens the saved-snapshot picker; on a pick, registers its display label so
        /// <see cref="CaptureSide"/> resolves it, then hands the label to the caller to assign to
        /// SourceA/SourceB.</summary>
        private void PickSaved(Action<string> assignLabel)
        {
            var picker = new SnapshotPickerWindow(_listSavedSnapshots(), _deleteSavedSnapshot);
            if (picker.ShowDialog() != true || picker.SelectedMeta == null) return;

            var meta = picker.SelectedMeta;
            var label = $"{meta.SourceName} ({FormatCapturedUtc(meta.CapturedUtc)})";
            _savedSnapshotIdsByLabel[label] = meta.Id;

            if (!SourceAOptions.Contains(label)) SourceAOptions.Add(label);
            if (!SourceBOptions.Contains(label)) SourceBOptions.Add(label);
            assignLabel(label);
        }

        private static string FormatCapturedUtc(string capturedUtc) =>
            DateTime.TryParse(capturedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : capturedUtc;

        private static bool LooksLikeFile(string s) =>
            s.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)
            || s.EndsWith(".rte", StringComparison.OrdinalIgnoreCase)
            || s.Contains(":\\");

        private void Populate(ComparisonResult result)
        {
            var vt = result.Categories.FirstOrDefault(c => c.CategoryId == ViewTemplatesSnapshot.Category);
            if (vt != null)
                ViewTemplates.Load(vt, AcceptItem);
            else
                ViewTemplates.Items.Clear();

            var s = vt?.Summary;
            StatusText = s == null
                ? "No comparable View Templates found."
                : $"View Templates: {s.Changed} changed, {s.Added} only in A, {s.Removed} only in B, {s.Unchanged} identical.";
        }

        private void AcceptItem(ItemDiffViewModel itemVm)
        {
            var dec = itemVm.Decision;
            CapturedSnapshot? src =
                dec == DecisionOption.AcceptA ? _capturedA :
                dec == DecisionOption.AcceptB ? _capturedB :
                itemVm.Kind == DiffKind.Added ? _capturedA : _capturedB;

            if (src == null)
            {
                StatusText = "Run a comparison before accepting.";
                return;
            }

            var cat = src.Categories.FirstOrDefault(c => c.CategoryId == ViewTemplatesSnapshot.Category);
            if (cat == null)
            {
                StatusText = "Nothing to accept for this item.";
                return;
            }

            var result = _curator.Accept(_standard, src.Meta, cat, itemVm.Key, new DependencyClosure(), replace: true);
            if (result.Applied)
            {
                _saveStandard(_standard);
                itemVm.Decision = src == _capturedA ? DecisionOption.AcceptA : DecisionOption.AcceptB;
                StatusText = $"Accepted \"{itemVm.DisplayName}\" into the Standard (revision {_standard.Meta.Revision}).";
            }
            else
            {
                StatusText = "Accept failed: " + result.Conflict;
            }
        }

        private void ExportReport()
        {
            if (_lastResult == null) return;
            var html = HtmlReportWriter.Write(_lastResult);
            _saveReportHtml(html);
            StatusText = "Report exported.";
        }

        private void SetStatusOnUi(string text) => OnUi(() => StatusText = text);

        private static void OnUi(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.Invoke(action);
            else
                action();
        }
    }
}
