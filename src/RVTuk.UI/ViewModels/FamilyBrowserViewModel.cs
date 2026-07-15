using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using RVTuk.Core.Shared.Config;
using RVTuk.Core.FamilyBrowser.Database;
using RVTuk.Core.FamilyBrowser.Models;
using RVTuk.Core.FamilyBrowser.Util;
using RVTuk.Core.Shared.Util;

namespace RVTuk.UI.ViewModels
{
    public enum FamilyBrowserRightView { Detail, Settings, Help }

    public class FamilyBrowserViewModel : ViewModelBase, IDisposable
    {
        // Points at the team's docs repo. Update this once the real repo/path is known.
        private const string HelpMarkdownUrl = "https://raw.githubusercontent.com/knafo-klimor/rvtuk-docs/main/help.md";

        private readonly AppConfig _config;
        private readonly BrowserRepository _repo;
        private readonly Func<IReadOnlyList<ProjectFamilyInfo>> _getProjectFamilies;
        private readonly Func<string, (bool Success, string? Error)> _loadFamily;
        private readonly Func<long, string, bool> _rescanFamily;
        private readonly Action<string> _openInFamilyEditor;
        private readonly Dispatcher _dispatcher;
        private readonly object _loadLock = new object();
        private static readonly HttpClient _http = new HttpClient();

        private List<FamilyBrowserItemViewModel> _allItems = new();
        private string _searchText = string.Empty;
        private FamilyBrowserItemViewModel? _selectedItem;
        private bool _showInProjectFamilies;
        private bool _showFavoriteFamilies;
        private bool _showLibraryOnlyFamilies;
        private bool _isGridView;
        private bool? _isAllCategoriesSelected = true;
        private bool _isSyncing;
        private bool _isRescanning;
        private int _outdatedCount;
        private string? _instructionsXaml;
        private List<ParameterModel> _parameters = new();
        private string _parameterFilter = string.Empty;
        private FamilyBrowserRightView _rightView = FamilyBrowserRightView.Detail;
        private bool _helpLoaded;
        private string _helpSourceLabel = string.Empty;
        private string? _helpMarkdownRaw;

        public ObservableCollection<ParameterModel> FilteredParameters { get; } = new();
        public ObservableCollection<FamilyBrowserItemViewModel> FilteredItems { get; } = new();
        public ObservableCollection<CategoryFilterOption> CategoryOptions { get; } = new();

        public string SearchText
        {
            get => _searchText;
            set { SetProperty(ref _searchText, value); ApplyFilter(); }
        }

        // All default off (unfiltered — nothing hidden). Pressing one or more filters the list
        // to families matching ANY pressed toggle (OR/union), not all of them — otherwise
        // pressing just Favourites while Project/Library-only were exclusion toggles would AND
        // against that complementary, exhaustive pair and always yield nothing. With no toggle
        // pressed, the list is unfiltered by source/status.
        public bool ShowInProjectFamilies
        {
            get => _showInProjectFamilies;
            set { SetProperty(ref _showInProjectFamilies, value); ApplyFilter(); }
        }

        public bool ShowFavoriteFamilies
        {
            get => _showFavoriteFamilies;
            set { SetProperty(ref _showFavoriteFamilies, value); ApplyFilter(); }
        }

        public bool ShowLibraryOnlyFamilies
        {
            get => _showLibraryOnlyFamilies;
            set { SetProperty(ref _showLibraryOnlyFamilies, value); ApplyFilter(); }
        }

        // Toggles the family list between the compact row list and the 2-column large-thumbnail
        // card grid. In-memory only (no persistence) — both views bind the same FilteredItems /
        // SelectedItem, so selection and keyboard navigation carry across the switch.
        public bool IsGridView
        {
            get => _isGridView;
            set => SetProperty(ref _isGridView, value);
        }

        // Tri-state master checkbox for CategoryOptions: true = all selected, false = none,
        // null = mixed (indeterminate, display-only — the setter only ever receives true/false,
        // driven by a deterministic Click handler in code-behind rather than WPF's native
        // three-way cycling).
        public bool? IsAllCategoriesSelected
        {
            get => _isAllCategoriesSelected;
            set
            {
                if (value == true || value == false)
                    foreach (var c in CategoryOptions) c.IsSelected = value.Value;
            }
        }

        public string CategorySummary
        {
            get
            {
                if (CategoryOptions.Count == 0) return "All categories";
                var selected = CategoryOptions.Count(o => o.IsSelected);
                if (selected == CategoryOptions.Count) return "All categories";
                if (selected == 0) return "No categories";
                if (selected == 1) return CategoryOptions.First(o => o.IsSelected).Name;
                return $"{selected} categories";
            }
        }

        private void UpdateCategoryAllState()
        {
            bool? next;
            if (CategoryOptions.Count == 0) next = true;
            else
            {
                var selected = CategoryOptions.Count(o => o.IsSelected);
                next = selected == CategoryOptions.Count ? true : selected == 0 ? (bool?)false : null;
            }
            _isAllCategoriesSelected = next;
            OnPropertyChanged(nameof(IsAllCategoriesSelected));
            OnPropertyChanged(nameof(CategorySummary));
        }

        public FamilyBrowserItemViewModel? SelectedItem
        {
            get => _selectedItem;
            set
            {
                SetProperty(ref _selectedItem, value);
                LoadDetailAsync(value);
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(ShowFamilyDetail));
                OnPropertyChanged(nameof(ShowDetailPane));
                OnPropertyChanged(nameof(ShowUpdateInProject));
                if (value != null) RightView = FamilyBrowserRightView.Detail;
            }
        }

        public bool HasSelection => _selectedItem != null;
        public bool ShowUpdateInProject => _selectedItem?.VersionStatus == VersionStatus.UpdateAvailable;
        public bool ShowFamilyDetail => HasSelection;
        public bool ShowDetailPane => IsShowingDetail && HasSelection;

        public bool IsSyncing
        {
            get => _isSyncing;
            set => SetProperty(ref _isSyncing, value);
        }

        public bool IsRescanning
        {
            get => _isRescanning;
            set => SetProperty(ref _isRescanning, value);
        }

        public int OutdatedCount
        {
            get => _outdatedCount;
            set { SetProperty(ref _outdatedCount, value); OnPropertyChanged(nameof(ShowUpdateAll)); }
        }

        public bool ShowUpdateAll => _outdatedCount > 0;

        public string? InstructionsXaml
        {
            get => _instructionsXaml;
            set => SetProperty(ref _instructionsXaml, value);
        }

        private string? _selectedTags;
        public string? SelectedTags
        {
            get => _selectedTags;
            set { SetProperty(ref _selectedTags, value); OnPropertyChanged(nameof(ShowTags)); RebuildTagChips(); }
        }

        public bool ShowTags => !string.IsNullOrWhiteSpace(_selectedTags);

        public ObservableCollection<string> SelectedTagChips { get; } = new();

        private void RebuildTagChips()
        {
            SelectedTagChips.Clear();
            if (string.IsNullOrWhiteSpace(_selectedTags)) return;
            foreach (var t in _selectedTags!.Split(','))
            {
                var s = t.Trim();
                if (s.Length > 0) SelectedTagChips.Add(s);
            }
        }

        public List<ParameterModel> Parameters
        {
            get => _parameters;
            set { SetProperty(ref _parameters, value); ApplyParameterFilter(); }
        }

        public string ParameterFilter
        {
            get => _parameterFilter;
            set { SetProperty(ref _parameterFilter, value ?? string.Empty); ApplyParameterFilter(); }
        }

        private void ApplyParameterFilter()
        {
            // Multi-word: split on whitespace; every token must appear somewhere in the name,
            // group, or kind (any order/position) — matching the same convention as the family
            // search above. A single literal-phrase match would fail as soon as a second word
            // didn't appear verbatim in one field (e.g. "seat length" never matches a parameter
            // named "Length" in group "Dimensions" — those are two different fields).
            var tokens = _parameterFilter.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            IEnumerable<ParameterModel> src = _parameters;
            if (tokens.Length > 0)
                src = src.Where(p =>
                    tokens.All(t =>
                        (p.ParameterName?.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (p.ParamGroup?.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (p.Kind?.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)));

            FilteredParameters.Clear();
            foreach (var p in src) FilteredParameters.Add(p);
        }

        public BrowserRepository Repo => _repo;

        // Right panel: family detail (default), embedded settings, or the help/about page.
        public FamilyBrowserRightView RightView
        {
            get => _rightView;
            set
            {
                SetProperty(ref _rightView, value);
                OnPropertyChanged(nameof(IsShowingDetail));
                OnPropertyChanged(nameof(IsShowingSettings));
                OnPropertyChanged(nameof(IsShowingHelp));
                OnPropertyChanged(nameof(ShowDetailPane));
                if (value == FamilyBrowserRightView.Help) _ = LoadHelpDocumentAsync();
            }
        }

        public bool IsShowingDetail => _rightView == FamilyBrowserRightView.Detail;
        public bool IsShowingSettings => _rightView == FamilyBrowserRightView.Settings;
        public bool IsShowingHelp => _rightView == FamilyBrowserRightView.Help;

        public string? HelpMarkdownRaw
        {
            get => _helpMarkdownRaw;
            private set => SetProperty(ref _helpMarkdownRaw, value);
        }

        public string HelpSourceLabel
        {
            get => _helpSourceLabel;
            private set => SetProperty(ref _helpSourceLabel, value);
        }

        // The embedded settings surface (library folder, scan, ignored subfolders) — composed
        // rather than duplicated, reusing the same ConfigViewModel that used to back the
        // standalone ribbon Config window.
        public ConfigViewModel Settings { get; }

        public ICommand SyncCommand { get; }
        public ICommand UpdateAllCommand { get; }
        public ICommand LoadFamilyCommand { get; }
        public ICommand UpdateInProjectCommand { get; }
        public ICommand EditInfoCommand { get; }
        public ICommand RescanFamilyCommand { get; }
        public ICommand OpenFamilyEditorCommand { get; }
        public ICommand FilterByTagCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }
        public ICommand ToggleSettingsCommand { get; }
        public ICommand ToggleHelpCommand { get; }
        public ICommand ClearSearchCommand { get; }

        public event Action<FamilyBrowserItemViewModel>? EditInfoRequested;

        public FamilyBrowserViewModel(
            AppConfig config,
            BrowserRepository repo,
            Func<IReadOnlyList<ProjectFamilyInfo>> getProjectFamilies,
            Func<string, (bool Success, string? Error)> loadFamily,
            Func<long, string, bool> rescanFamily,
            Action<bool, bool> scan,
            Action<string> openInFamilyEditor,
            Action? onLibraryFolderChanged = null)
        {
            _config = config;
            _repo = repo;
            _getProjectFamilies = getProjectFamilies;
            _loadFamily = loadFamily;
            _rescanFamily = rescanFamily;
            _openInFamilyEditor = openInFamilyEditor;
            _dispatcher = Dispatcher.CurrentDispatcher;

            Settings = new ConfigViewModel(config, scan, onLibraryFolderChanged);

            SyncCommand            = new RelayCommand(Sync, () => !IsSyncing);
            UpdateAllCommand       = new RelayCommand(UpdateAll,     () => OutdatedCount > 0);
            // Model-only rows have no .rfa and no DB row behind them, so every library-backed
            // action stays disabled for them.
            LoadFamilyCommand      = new RelayCommand(LoadSelected,  () => SelectedItem != null && !SelectedItem.IsModelOnly);
            UpdateInProjectCommand = new RelayCommand(UpdateSelected,() => ShowUpdateInProject);
            EditInfoCommand        = new RelayCommand(RequestEditInfo, () => SelectedItem != null && !SelectedItem.IsModelOnly);
            RescanFamilyCommand    = new RelayCommand(RescanSelected, () => SelectedItem != null && !SelectedItem.IsModelOnly && !IsRescanning);
            OpenFamilyEditorCommand= new RelayCommand(OpenInFamilyEditor, () => SelectedItem != null && !SelectedItem.IsModelOnly);
            FilterByTagCommand     = new RelayCommand<string>(t => { if (!string.IsNullOrWhiteSpace(t)) SearchText = t.Trim(); });
            ToggleFavoriteCommand  = new RelayCommand<FamilyBrowserItemViewModel>(ToggleFavorite);
            ToggleSettingsCommand  = new RelayCommand(() => RightView = RightView == FamilyBrowserRightView.Settings
                ? FamilyBrowserRightView.Detail : FamilyBrowserRightView.Settings);
            ToggleHelpCommand      = new RelayCommand(() => RightView = RightView == FamilyBrowserRightView.Help
                ? FamilyBrowserRightView.Detail : FamilyBrowserRightView.Help);
            ClearSearchCommand     = new RelayCommand(() => SearchText = string.Empty);

            LoadFamilies();
            LoadCategories();
        }

        private void LoadFamilies()
        {
            _allItems = _repo.GetAllFamilies()
                .Select(f => new FamilyBrowserItemViewModel(f))
                .ToList();
            ApplyFilter();
        }

        private void LoadCategories()
        {
            // Preserve which categories were deselected across a reload (e.g. after Sync).
            var previouslyDeselected = CategoryOptions.Where(o => !o.IsSelected).Select(o => o.Name).ToHashSet();
            CategoryOptions.Clear();
            foreach (var cat in _repo.GetCategories().Where(c => !string.IsNullOrEmpty(c)))
            {
                var opt = new CategoryFilterOption(cat!) { IsSelected = !previouslyDeselected.Contains(cat!) };
                opt.PropertyChanged += (_, __) => { UpdateCategoryAllState(); ApplyFilter(); };
                CategoryOptions.Add(opt);
            }
            UpdateCategoryAllState();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            // Multi-word search: split on whitespace; every token must appear somewhere in the
            // family name, in any order/position. e.g. "door single" matches "single - door".
            var tokens = _searchText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var filtered = _allItems.AsEnumerable();
            if (tokens.Length > 0)
                filtered = filtered.Where(i =>
                    tokens.All(t =>
                        i.DisplayName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (i.Tags != null && i.Tags.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)));

            if (CategoryOptions.Count > 0)
            {
                var selectedCats = CategoryOptions.Where(o => o.IsSelected).Select(o => o.Name).ToHashSet();
                if (selectedCats.Count < CategoryOptions.Count)
                    filtered = filtered.Where(i => i.Category != null && selectedCats.Contains(i.Category));
            }

            // Source/status toggles: OR, not AND. With none pressed, unfiltered. Pressing any
            // combination shows the union of what they represent — pressing only Favourites
            // must show every favourite regardless of project/library status, which an
            // exclusion/AND model can't express here since in-project and library-only are a
            // complementary, exhaustive pair (excluding both always yields nothing).
            if (_showInProjectFamilies || _showFavoriteFamilies || _showLibraryOnlyFamilies)
                filtered = filtered.Where(i =>
                    (_showInProjectFamilies && i.VersionStatus != VersionStatus.None) ||
                    (_showLibraryOnlyFamilies && i.VersionStatus == VersionStatus.None) ||
                    (_showFavoriteFamilies && i.IsFavorite));

            // Hide families under an ignored subfolder (kept in the DB, just not shown).
            if (_config.IgnoredSubfolders != null && _config.IgnoredSubfolders.Count > 0)
                filtered = filtered.Where(i => !PathUtil.IsUnderIgnoredFolder(i.RelativePath, _config.IgnoredSubfolders));

            // Hide files matching an ignored-file pattern, e.g. Revit backups (same semantics).
            var ignoredFiles = new IgnoredFileMatcher(_config.IgnoredFilePatterns);
            if (ignoredFiles.HasPatterns)
                filtered = filtered.Where(i => !ignoredFiles.IsIgnored(i.FileName));

            FilteredItems.Clear();
            foreach (var item in filtered.OrderBy(i => i.DisplayName))
                FilteredItems.Add(item);
        }

        private void LoadDetailAsync(FamilyBrowserItemViewModel? item)
        {
            // Model-only rows have no DB row (Id 0) — nothing to fetch, and Id 0 must never
            // reach the repository queries.
            if (item == null || item.IsModelOnly) { InstructionsXaml = null; Parameters = new List<ParameterModel>(); SelectedTags = null; return; }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var xaml = _repo.GetInstructionsXaml(item.Id);
                    var prms = _repo.GetParameters(item.Id);
                    var tags = _repo.GetTags(item.Id);
                    _dispatcher.Invoke(() =>
                    {
                        // Selection may have moved on while this load ran; a stale result must
                        // not overwrite the currently selected family's detail pane.
                        if (!ReferenceEquals(SelectedItem, item)) return;

                        InstructionsXaml = xaml;
                        Parameters = prms;
                        item.Model.Tags = tags;
                        SelectedTags = tags;
                    });
                }
                catch { /* swallow — detail load failure is non-fatal */ }
            });
        }

        private async System.Threading.Tasks.Task LoadHelpDocumentAsync()
        {
            if (_helpLoaded) return;
            _helpLoaded = true;
            HelpSourceLabel = "Loading…";
            try
            {
                var markdown = await _http.GetStringAsync(HelpMarkdownUrl);
                HelpMarkdownRaw = markdown;
                HelpSourceLabel = "Source: " + HelpMarkdownUrl;
            }
            catch (Exception ex)
            {
                HelpSourceLabel = "Couldn't load help — " + ex.Message;
                HelpMarkdownRaw =
                    "# Help unavailable\n\nCouldn't reach the docs repo. Check your network connection, " +
                    "or reach the RVTuk team directly.";
            }
        }

        private void Sync()
        {
            IsSyncing = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var newAllItems = new List<FamilyBrowserItemViewModel>();
                int outdated = 0;
                try
                {
                    var root = _config.LibraryFolderPath;
                    var foundRelPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var ignoredFiles = new IgnoredFileMatcher(_config.IgnoredFilePatterns);

                    foreach (var fullPath in PathUtil.SafeEnumerateFiles(root, "*.rfa"))
                    {
                        if (fullPath.Length >= 260) continue; // unusable on .NET Framework; skip
                        var relativePath = PathUtil.GetRelativePath(root, fullPath);

                        if (PathUtil.IsUnderIgnoredFolder(relativePath, _config.IgnoredSubfolders)
                            || ignoredFiles.IsIgnored(Path.GetFileName(fullPath)))
                        {
                            // Ignored: skip upsert but add to foundRelPaths so existing DB rows
                            // for this family are not treated as stale and deleted.
                            foundRelPaths.Add(relativePath);
                            continue;
                        }

                        var fi = new FileInfo(fullPath);
                        foundRelPaths.Add(relativePath);
                        _repo.UpsertFamily(relativePath, fi.Name, fi.LastWriteTimeUtc, fi.Length);
                    }

                    var stale = _repo.GetAllRelativePaths()
                        .Where(p => !foundRelPaths.Contains(p)).ToList();
                    if (stale.Count > 0)
                        _repo.DeleteStaleEntries(stale);

                    newAllItems = _repo.GetAllFamilies()
                        .Select(f => new FamilyBrowserItemViewModel(f))
                        .ToList();

                    // Version check compares the _Version shared parameter: the library value is
                    // captured into the index by the deep scan, the project value is read off the
                    // loaded family's symbols. A family missing the parameter on either side is
                    // simply "in project" with no update verdict (FamilyVersionCheck returns false).
                    // (Families can share a name across categories; first occurrence wins.)
                    var projectFamilies = new Dictionary<string, ProjectFamilyInfo>(StringComparer.OrdinalIgnoreCase);
                    foreach (var pf in _getProjectFamilies())
                        if (!projectFamilies.ContainsKey(pf.Name))
                            projectFamilies[pf.Name] = pf;

                    var libraryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in newAllItems)
                    {
                        var nameNoExt = Path.GetFileNameWithoutExtension(item.FileName);
                        libraryNames.Add(nameNoExt);
                        if (!projectFamilies.TryGetValue(nameNoExt, out var pf)) continue;
                        bool isNewer = FamilyVersionCheck.IsUpdateAvailable(item.Model.Version, pf.Version);
                        item.VersionStatus = isNewer ? VersionStatus.UpdateAvailable : VersionStatus.UpToDate;
                        if (isNewer) outdated++;
                    }

                    // Project families with no library counterpart get a synthetic "model only"
                    // row so the browser shows the whole picture, not just the library. The
                    // ".rfa" suffix keeps DisplayName's extension-stripping from eating part of
                    // a family name that contains a dot.
                    foreach (var pf in projectFamilies.Values)
                        if (!libraryNames.Contains(pf.Name))
                            newAllItems.Add(new FamilyBrowserItemViewModel(new FamilyBrowserItem
                            {
                                Id = 0,
                                FileName = pf.Name + ".rfa",
                                RelativePath = string.Empty,
                                Category = pf.Category,
                                Version = pf.Version,
                                VersionStatus = VersionStatus.ModelOnly,
                            }));
                }
                catch (Exception ex)
                {
                    _dispatcher.BeginInvoke(new Action(() =>
                        MessageBox.Show($"Sync failed: {ex.Message}", "RVTuk",
                            MessageBoxButton.OK, MessageBoxImage.Warning)));
                }
                finally
                {
                    var finalItems = newAllItems;
                    int finalOutdated = outdated;
                    _dispatcher.Invoke(() =>
                    {
                        _allItems = finalItems;
                        LoadCategories();
                        OutdatedCount = finalOutdated;
                        OnPropertyChanged(nameof(ShowUpdateInProject));
                        IsSyncing = false;
                        ApplyFilter();
                    });
                }
            });
        }

        private void LoadSelected()
        {
            if (SelectedItem != null) LoadFamiliesSequentially(new[] { SelectedItem });
        }

        private void UpdateSelected() => LoadSelected();

        private void UpdateAll()
        {
            var items = _allItems.Where(i => i.VersionStatus == VersionStatus.UpdateAvailable).ToList();
            if (items.Count > 0) LoadFamiliesSequentially(items);
        }

        // Loads families one at a time on a single background worker. LoadFamilyHandler is a
        // shared singleton and ExternalEvent.Raise() coalesces, so firing several loads at once
        // (Update All) would race on its state — _loadLock serialises every ping-pong, even
        // across overlapping batches.
        private void LoadFamiliesSequentially(IReadOnlyList<FamilyBrowserItemViewModel> items)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                foreach (var item in items)
                {
                    var fullPath = Path.Combine(_config.LibraryFolderPath, item.RelativePath);
                    try
                    {
                        bool success;
                        string? error;
                        lock (_loadLock)
                        {
                            (success, error) = _loadFamily(fullPath);
                        }
                        _dispatcher.Invoke(() =>
                        {
                            if (success)
                                item.VersionStatus = VersionStatus.UpToDate;
                            else if (!string.IsNullOrEmpty(error))
                                MessageBox.Show($"Failed to load family: {error}", "RVTuk");
                        });
                    }
                    catch (Exception ex)
                    {
                        _dispatcher.BeginInvoke(new Action(() =>
                            MessageBox.Show($"Failed to load family: {ex.Message}", "RVTuk",
                                            MessageBoxButton.OK, MessageBoxImage.Warning)));
                    }
                }
                _dispatcher.BeginInvoke(new Action(() => OnPropertyChanged(nameof(ShowUpdateInProject))));
            });
        }

        private void RequestEditInfo()
        {
            if (SelectedItem != null)
                EditInfoRequested?.Invoke(SelectedItem);
        }

        private void ToggleFavorite(FamilyBrowserItemViewModel? item)
        {
            if (item == null || item.IsModelOnly) return; // no DB row to persist a favourite on
            item.IsFavorite = !item.IsFavorite;
            try { _repo.SetFavorite(item.Id, item.IsFavorite); }
            catch { /* read-only share; favourite stays in-memory only */ }
            ApplyFilter();
        }

        // Re-extracts metadata (category, parameters, thumbnail) for just the selected family,
        // via the same Revit ping-pong the full deep scan uses — but for one family, so it takes
        // seconds. Runs on a background thread so WaitForCompletion can't deadlock the UI thread.
        private void RescanSelected()
        {
            var item = SelectedItem;
            if (item == null) return;
            var fullPath = Path.Combine(_config.LibraryFolderPath, item.RelativePath);
            IsRescanning = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ok;
                try { ok = _rescanFamily(item.Id, fullPath); }
                catch { ok = false; }
                byte[]? freshThumb = null;
                if (ok)
                {
                    try { freshThumb = _repo.GetResolvedThumbnail(item.Id); }
                    catch { /* thumbnail refresh is best-effort */ }
                }
                _dispatcher.Invoke(() =>
                {
                    IsRescanning = false;
                    if (ok)
                    {
                        item.UpdateThumbnail(freshThumb);
                        LoadDetailAsync(item);
                    }
                    else MessageBox.Show("Could not rescan this family.", "RVTuk");
                });
            });
        }

        // Opens the .rfa directly in Revit's Family Editor. Runs off the UI thread since it
        // blocks on the same ExternalEvent ping-pong pattern as load/rescan.
        private void OpenInFamilyEditor()
        {
            var item = SelectedItem;
            if (item == null) return;
            var fullPath = Path.Combine(_config.LibraryFolderPath, item.RelativePath);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string? error = null;
                try { _openInFamilyEditor(fullPath); }
                catch (Exception ex) { error = ex.Message; }
                if (error != null)
                    _dispatcher.Invoke(() =>
                        MessageBox.Show($"Could not open family editor: {error}", "RVTuk"));
            });
        }

        public void Dispose() => _repo.Dispose();
    }
}
