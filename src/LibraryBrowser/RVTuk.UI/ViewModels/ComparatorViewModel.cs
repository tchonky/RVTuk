using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using RVTuk.Core.Comparison;
using RVTuk.Core.Models.Comparison;
using RVTuk.Core.Reporting;

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
            Action<StandardSnapshot> saveStandard)
        {
            _getOpenDocuments = getOpenDocuments;
            _captureOpenDoc = captureOpenDoc;
            _captureFile = captureFile;
            _pickFile = pickFile;
            _saveReportHtml = saveReportHtml;
            _loadStandard = loadStandard;
            _saveStandard = saveStandard;

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
            if (selection == ActiveDocLabel)
                return _captureOpenDoc(null);
            if (LooksLikeFile(selection))
                return _captureFile(selection);
            return _captureOpenDoc(selection);
        }

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
