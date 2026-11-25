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
using RVTuk.Core.FamilyBrowser.Requests;
using RVTuk.Core.FamilyBrowser.Util;
using RVTuk.Core.Shared.Util;

using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.FamilyBrowser.ViewModels
{
    public enum FamilyBrowserRightView { Detail, Settings, Help, Requests }

    /// <summary>What the request window is asked to show.</summary>
    public sealed class RequestDialogInput
    {
        public bool IsFix { get; set; }
        /// <summary>A view-only user's Save to Library: the family is always attached, the note optional.</summary>
        public bool IsSuggestion { get; set; }
        public string? FamilyName { get; set; }
        /// <summary>The family is loaded in the open project, so its project version can be attached.</summary>
        public bool CanAttach { get; set; }
    }

    /// <summary>What the user typed in the request window (null result = cancelled).</summary>
    public sealed class RequestDialogResult
    {
        public string Text { get; set; } = string.Empty;
        public bool Attach { get; set; }
    }

    public class FamilyBrowserViewModel : ViewModelBase, IDisposable
    {
        // The user-facing help page, served from the repo's master branch.
        private const string HelpMarkdownUrl = "https://raw.githubusercontent.com/tchonky/RVTuk/master/docs/tools/family-browser/help.md";

        // Filter option for families without a category (never scanned, or none in the project).
        private const string NoCategory = "(No category)";

        private readonly AppConfig _config;
        private readonly BrowserRepository _repo;
        private readonly PersonalFavorites? _favorites;
        private string _libraryStatus = string.Empty;
        private string? _libraryStatusDetail;
        private readonly Func<ProjectSnapshot> _getProjectFamilies;
        private readonly Func<string, string?, (bool Success, string? Error)> _loadFamily;
        private readonly Func<long, string, (bool Success, string? Error)> _rescanFamily;
        private readonly Func<string, (bool Success, string? Error)> _openInFamilyEditor;
        private readonly Func<string, (bool Success, string? Error)> _openModelFamilyInEditor;
        private readonly Func<string, string, (bool Success, string? Error)> _saveFamilyToLibrary;
        private readonly Func<IReadOnlyList<string>, IReadOnlyDictionary<string, byte[]>> _getFamilyPreviews;
        private readonly Dispatcher _dispatcher;
        private readonly object _loadLock = new object();
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        private List<FamilyBrowserItemViewModel> _allItems = new();
        private string _searchText = string.Empty;
        private FamilyBrowserItemViewModel? _selectedItem;
        private bool _showFavoriteFamilies;
        private bool _isGridView;
        private bool _isSearchPopupOpen;
        private bool _isSyncing;
        private bool _isRescanning;
        private bool _isLoading;
        private bool _bulkCategoryEdit;
        private bool _rebuildingList;
        private int _outdatedCount;
        private ProjectComparison? _lastComparison;
        private string? _comparedDocumentKey;
        private string? _comparedProjectTitle;
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

        // ── The search box ─────────────────────────────────────────────────────────────
        // One box for words, categories and tags. Ticked categories and chosen tags show as
        // chips in it (in the order they were added, so Backspace takes the last one back).
        // Categories OR, tags AND, words AND, ⭐ AND — the rule lives in Core's FamilyFilter.
        public ObservableCollection<FilterChip> FilterChips { get; } = new();

        /// <summary>The dropdown's categories: the ones whose name contains the typed text.</summary>
        public ObservableCollection<CategoryFilterOption> VisibleCategoryOptions { get; } = new();

        /// <summary>The dropdown's tags: the built-in ones, and while typing every tag containing the text.</summary>
        public ObservableCollection<TagSuggestion> TagSuggestions { get; } = new();

        public string SearchText
        {
            get => _searchText;
            set
            {
                SetProperty(ref _searchText, value ?? string.Empty);
                SearchChanged();
                ApplyFilter();
            }
        }

        private void SearchChanged()
        {
            OnPropertyChanged(nameof(HasAnyFilter));
            OnPropertyChanged(nameof(SearchHint));
        }

        public bool IsSearchPopupOpen
        {
            get => _isSearchPopupOpen;
            set => SetProperty(ref _isSearchPopupOpen, value);
        }

        public bool HasAnyFilter => _searchText.Length > 0 || FilterChips.Count > 0;
        public bool HasSelectedCategories => FilterChips.Any(c => c.IsCategory);
        public bool ShowTagSuggestions => TagSuggestions.Count > 0;
        public bool ShowNoSearchMatch => _searchText.Trim().Length > 0 && VisibleCategoryOptions.Count == 0 && TagSuggestions.Count == 0;

        /// <summary>The hint inside the empty box; none once chips fill it.</summary>
        public string SearchHint => _searchText.Length == 0 && FilterChips.Count == 0
            ? "Search, or pick categories and tags" : string.Empty;

        public bool ShowFavoriteFamilies
        {
            get => _showFavoriteFamilies;
            set { SetProperty(ref _showFavoriteFamilies, value); ApplyFilter(); }
        }

        /// <summary>Adds a tag chip and clears the typed text (the word was only there to find the tag).</summary>
        public void AddTagFilter(string? tag)
        {
            var t = tag?.Trim();
            if (string.IsNullOrEmpty(t)) return;
            if (!FilterChips.Any(c => c.IsTag && string.Equals(c.Value, t, StringComparison.OrdinalIgnoreCase)))
                FilterChips.Add(new FilterChip(isCategory: false, t!));
            ChipsChanged();
            _searchText = string.Empty;
            OnPropertyChanged(nameof(SearchText));
            SearchChanged();
            ApplyFilter();
        }

        /// <summary>Enter in the search box: adds the highlighted tag. False when there is none.</summary>
        public bool AddFirstSuggestion()
        {
            var first = TagSuggestions.FirstOrDefault(s => s.IsFirst);
            if (first == null) return false;
            AddTagFilter(first.Name);
            return true;
        }

        /// <summary>Backspace in the empty search box: takes the last chip back. False when there is none.</summary>
        public bool RemoveLastChip()
        {
            if (FilterChips.Count == 0) return false;
            RemoveChip(FilterChips[FilterChips.Count - 1]);
            return true;
        }

        private void RemoveChip(FilterChip? chip)
        {
            if (chip == null) return;
            if (chip.IsCategory)
            {
                var opt = CategoryOptions.FirstOrDefault(o => o.Name == chip.Value);
                if (opt != null && opt.IsSelected) { opt.IsSelected = false; return; } // its handler removes the chip
            }
            FilterChips.Remove(chip);
            ChipsChanged();
            ApplyFilter();
        }

        private void ClearCategories()
        {
            _bulkCategoryEdit = true;
            try { foreach (var o in CategoryOptions) o.IsSelected = false; }
            finally { _bulkCategoryEdit = false; }
            foreach (var c in FilterChips.Where(c => c.IsCategory).ToList()) FilterChips.Remove(c);
            ChipsChanged();
            ApplyFilter();
        }

        private void ClearSearchAndChips()
        {
            _bulkCategoryEdit = true;
            try { foreach (var o in CategoryOptions) o.IsSelected = false; }
            finally { _bulkCategoryEdit = false; }
            FilterChips.Clear();
            ChipsChanged();
            SearchText = string.Empty;
        }

        private void ChipsChanged()
        {
            OnPropertyChanged(nameof(HasAnyFilter));
            OnPropertyChanged(nameof(HasSelectedCategories));
            OnPropertyChanged(nameof(SearchHint));
        }

        // Toggles the family list between the compact row list and the 2-column large-thumbnail
        // card grid. In-memory only (no persistence) — both views bind the same FilteredItems /
        // SelectedItem, so selection and keyboard navigation carry across the switch.
        public bool IsGridView
        {
            get => _isGridView;
            set => SetProperty(ref _isGridView, value);
        }

        public FamilyBrowserItemViewModel? SelectedItem
        {
            get => _selectedItem;
            set
            {
                // ApplyFilter's Clear() makes both ListBoxes push null; it restores the
                // selection itself, so the detail pane doesn't vanish on every keystroke.
                if (_rebuildingList || ReferenceEquals(_selectedItem, value)) return;
                _selectedItem = value;
                OnPropertyChanged();
                RebuildTagChips();
                UpdateSelectedRequests();
                LoadDetailAsync(value);
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(ShowDetailPane));
                OnPropertyChanged(nameof(ShowUpdateInProject));
                if (value != null) RightView = FamilyBrowserRightView.Detail;
            }
        }

        public bool HasSelection => _selectedItem != null;
        public bool ShowUpdateInProject => _selectedItem?.VersionStatus == VersionStatus.UpdateAvailable;
        public bool ShowDetailPane => IsShowingDetail && HasSelection;

        public bool IsSyncing
        {
            get => _isSyncing;
            set { SetProperty(ref _isSyncing, value); BusyChanged(); }
        }

        public bool IsRescanning
        {
            get => _isRescanning;
            set { SetProperty(ref _isRescanning, value); BusyChanged(); }
        }

        private bool _isSavingToLibrary;
        public bool IsSavingToLibrary
        {
            get => _isSavingToLibrary;
            set { SetProperty(ref _isSavingToLibrary, value); BusyChanged(); }
        }

        // A Load/Update or Open-in-editor round-trip with Revit is running.
        public bool IsLoading
        {
            get => _isLoading;
            set { SetProperty(ref _isLoading, value); BusyChanged(); }
        }

        // A request is being written (with an attachment, Revit saves the family first).
        private bool _isSendingRequest;
        public bool IsSendingRequest
        {
            get => _isSendingRequest;
            set { SetProperty(ref _isSendingRequest, value); BusyChanged(); }
        }

        public bool IsBusy => _isSyncing || _isRescanning || _isSavingToLibrary || _isLoading || _isSendingRequest;

        private void BusyChanged()
        {
            OnPropertyChanged(nameof(IsBusy));
            // RelayCommand only re-queries CanExecute on input; these flags flip back when
            // background work finishes, so nudge the buttons to re-enable now.
            CommandManager.InvalidateRequerySuggested();
        }

        public int OutdatedCount
        {
            get => _outdatedCount;
            set { SetProperty(ref _outdatedCount, value); OnPropertyChanged(nameof(ShowUpdateAll)); }
        }

        public bool ShowUpdateAll => _outdatedCount > 0;

        /// <summary>The project the last ⟳ compared against (shown on the ⟳ button's tooltip).</summary>
        public string? ComparedProjectTitle
        {
            get => _comparedProjectTitle;
            private set { SetProperty(ref _comparedProjectTitle, value); OnPropertyChanged(nameof(SyncToolTip)); }
        }

        public string SyncToolTip => string.IsNullOrEmpty(_comparedProjectTitle)
            ? "Re-check which families are in the active project and which are outdated"
            : $"Compared with {_comparedProjectTitle}. Press to re-check the active project.";

        public string? InstructionsXaml
        {
            get => _instructionsXaml;
            set => SetProperty(ref _instructionsXaml, value);
        }

        // The selected family's tags: its own, then the built-in ones (which can't be removed).
        public ObservableCollection<TagChip> SelectedTagChips { get; } = new();
        public bool ShowTags => SelectedTagChips.Count > 0;

        private void RebuildTagChips()
        {
            var tags = _selectedItem?.AllTags ?? Array.Empty<string>();
            if (SelectedTagChips.Select(c => c.Name).SequenceEqual(tags)) return;
            SelectedTagChips.Clear();
            foreach (var t in tags) SelectedTagChips.Add(new TagChip(t));
            OnPropertyChanged(nameof(ShowTags));
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

        /// <summary>False for users with view-only access to the library: they browse a local
        /// copy, so scan, rescan, Edit Info and Save to Library are off.</summary>
        public bool CanEdit { get; }

        /// <summary>Footer note on which copy of the library is shown (empty for the maintainer).</summary>
        public string LibraryStatus
        {
            get => _libraryStatus;
            set => SetProperty(ref _libraryStatus, value);
        }

        public string? LibraryStatusDetail
        {
            get => _libraryStatusDetail;
            set => SetProperty(ref _libraryStatusDetail, value);
        }

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
                OnPropertyChanged(nameof(IsShowingRequests));
                OnPropertyChanged(nameof(ShowDetailPane));
                if (value == FamilyBrowserRightView.Help) _ = LoadHelpDocumentAsync();
            }
        }

        public bool IsShowingDetail => _rightView == FamilyBrowserRightView.Detail;
        public bool IsShowingSettings => _rightView == FamilyBrowserRightView.Settings;
        public bool IsShowingHelp => _rightView == FamilyBrowserRightView.Help;
        public bool IsShowingRequests => _rightView == FamilyBrowserRightView.Requests;

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
        public ICommand EditInfoCommand { get; }
        public ICommand RescanFamilyCommand { get; }
        public ICommand OpenFamilyEditorCommand { get; }
        public ICommand SaveToLibraryCommand { get; }
        public ICommand FilterByTagCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }
        public ICommand ToggleSettingsCommand { get; }
        public ICommand ToggleHelpCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand RemoveChipCommand { get; }
        public ICommand ClearCategoriesCommand { get; }
        public ICommand ToggleRequestsCommand { get; }
        public ICommand RequestFixCommand { get; }
        public ICommand RequestNewFamilyCommand { get; }
        public ICommand MarkRequestDoneCommand { get; }
        public ICommand WithdrawRequestCommand { get; }
        public ICommand ShowRequestFamilyCommand { get; }
        public ICommand ApproveSuggestionCommand { get; }

        public string SaveToLibraryLabel => CanEdit ? "Save to Library" : "Suggest for Library";
        public string SaveToLibraryToolTip => CanEdit
            ? "Save this family from the model into the library folder"
            : "Send this family from the model to the library maintainer, who decides whether to add it";

        /// <summary>Shows the request window (set by the browser window); null result = cancelled.</summary>
        public Func<RequestDialogInput, RequestDialogResult?>? RequestDialog { get; set; }

        public event Action<FamilyBrowserItemViewModel>? EditInfoRequested;

        /// <summary>The browser window: message boxes are owned by it so they never open behind it.</summary>
        public Window? Owner { get; set; }

        /// <summary>Answers the browser's message boxes instead of showing them (tests).</summary>
        public Func<string, MessageBoxButton, MessageBoxResult>? AnswerMessage { get; set; }

        private MessageBoxResult Warn(string text, MessageBoxButton buttons = MessageBoxButton.OK,
            MessageBoxImage icon = MessageBoxImage.Warning, string caption = "RVTuk")
            => AnswerMessage != null ? AnswerMessage(text, buttons)
            : Owner != null
                ? MessageBox.Show(Owner, text, caption, buttons, icon)
                : MessageBox.Show(text, caption, buttons, icon);

        public FamilyBrowserViewModel(
            AppConfig config,
            BrowserRepository repo,
            Func<ProjectSnapshot> getProjectFamilies,
            Func<string, string?, (bool Success, string? Error)> loadFamily,
            Func<long, string, (bool Success, string? Error)> rescanFamily,
            Action<bool, bool> scan,
            Func<string, (bool Success, string? Error)> openInFamilyEditor,
            Func<string, (bool Success, string? Error)> openModelFamilyInEditor,
            Func<string, string, (bool Success, string? Error)> saveFamilyToLibrary,
            Func<IReadOnlyList<string>, IReadOnlyDictionary<string, byte[]>> getFamilyPreviews,
            Action? onLibraryFolderChanged = null,
            bool canEdit = true,
            PersonalFavorites? favorites = null,
            string? currentUser = null)
        {
            _config = config;
            _repo = repo;
            CanEdit = canEdit;
            _favorites = favorites;
            _currentUser = string.IsNullOrWhiteSpace(currentUser) ? Environment.UserName : currentUser!;
            _requestStore = string.IsNullOrWhiteSpace(config.LibraryFolderPath) ? null : new RequestStore(config.LibraryFolderPath);
            _getProjectFamilies = getProjectFamilies;
            _loadFamily = loadFamily;
            _rescanFamily = rescanFamily;
            _openInFamilyEditor = openInFamilyEditor;
            _openModelFamilyInEditor = openModelFamilyInEditor;
            _saveFamilyToLibrary = saveFamilyToLibrary;
            _getFamilyPreviews = getFamilyPreviews;
            _dispatcher = Dispatcher.CurrentDispatcher;

            Settings = new ConfigViewModel(config, scan, onLibraryFolderChanged) { IsReadOnlyLibrary = !canEdit };
            // Editing the ignore lists hides/shows families right away (both share _config).
            Settings.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ConfigViewModel.IgnoredSubfoldersText)
                                   or nameof(ConfigViewModel.IgnoredFilePatternsText))
                {
                    ApplyFilter();
                    RecountOutdated();
                }
            };

            SyncCommand            = new RelayCommand(Sync, () => !IsSyncing);
            UpdateAllCommand       = new RelayCommand(UpdateAll,
                () => OutdatedCount > 0 && !IsLoading && !IsComparisonHidden && _comparedDocumentKey != null);
            CancelBatchCommand     = new RelayCommand(() => _batchStopReason ??= "Cancelled."); // only visible while a batch runs
            // Model-only rows have no .rfa and no DB row behind them, so library-backed
            // actions stay disabled for them; their own actions are open-from-model
            // (OpenFamilyEditorCommand) and Save to Library (below). Load also serves as
            // Update — the button just relabels itself when a newer version exists.
            LoadFamilyCommand      = new RelayCommand(LoadSelected,  () => SelectedItem != null && !SelectedItem.IsModelOnly && !IsLoading);
            EditInfoCommand        = new RelayCommand(RequestEditInfo, () => CanEdit && SelectedItem != null && !SelectedItem.IsModelOnly);
            RescanFamilyCommand    = new RelayCommand(RescanSelected, () => CanEdit && SelectedItem != null && !SelectedItem.IsModelOnly && !IsRescanning);
            // Open-in-editor works for model-only rows too (via EditFamily on the loaded family).
            OpenFamilyEditorCommand= new RelayCommand(OpenInFamilyEditor, () => SelectedItem != null && !IsLoading);
            // The maintainer saves into the library; a view-only user suggests the family instead.
            SaveToLibraryCommand   = new RelayCommand(SaveToLibrary,
                () => SelectedItem != null && SelectedItem.IsModelOnly && !IsSavingToLibrary
                      && (CanEdit || (_requestStore != null && !IsSendingRequest)));
            ApproveSuggestionCommand = new RelayCommand<RequestItemViewModel>(ApproveSuggestion, r => r != null && r.CanApprove);
            FilterByTagCommand     = new RelayCommand<string>(AddTagFilter);
            RemoveChipCommand      = new RelayCommand<FilterChip>(RemoveChip);
            ClearCategoriesCommand = new RelayCommand(ClearCategories);
            ToggleRequestsCommand  = new RelayCommand(() => RightView = RightView == FamilyBrowserRightView.Requests
                ? FamilyBrowserRightView.Detail : FamilyBrowserRightView.Requests);
            RequestFixCommand      = new RelayCommand(RequestFix,
                () => _requestStore != null && SelectedItem != null && !SelectedItem.IsModelOnly && !IsSendingRequest);
            RequestNewFamilyCommand= new RelayCommand(RequestNewFamily, () => _requestStore != null && !IsSendingRequest);
            MarkRequestDoneCommand = new RelayCommand<RequestItemViewModel>(MarkRequestDone, r => r != null && r.CanMarkDone);
            WithdrawRequestCommand = new RelayCommand<RequestItemViewModel>(WithdrawRequest, r => r != null && r.CanWithdraw);
            ShowRequestFamilyCommand = new RelayCommand<RequestItemViewModel>(ShowRequestFamily, r => r != null && r.IsFix);
            ToggleFavoriteCommand  = new RelayCommand<FamilyBrowserItemViewModel>(ToggleFavorite);
            ToggleSettingsCommand  = new RelayCommand(() => RightView = RightView == FamilyBrowserRightView.Settings
                ? FamilyBrowserRightView.Detail : FamilyBrowserRightView.Settings);
            ToggleHelpCommand      = new RelayCommand(() => RightView = RightView == FamilyBrowserRightView.Help
                ? FamilyBrowserRightView.Detail : FamilyBrowserRightView.Help);
            ClearSearchCommand     = new RelayCommand(ClearSearchAndChips);

            _allItems = _repo.GetAllFamilies().Select(f => new FamilyBrowserItemViewModel(f)).ToList();
            ApplyFavorites(_allItems);
            LoadCategories();
            ApplyFilter();
            ReloadRequests();
        }

        // Options come from the rows themselves, so families with no category and model-only
        // families (whose category may be absent from the library) can always be ticked back on.
        private void LoadCategories()
        {
            // Ticked categories stay ticked across a reload (e.g. after Sync): they are chips.
            // A ticked category that no family has any more (e.g. a model-only family's, once
            // the comparison is hidden) keeps its checkbox, so it can still be unticked there.
            var ticked = FilterChips.Where(c => c.IsCategory).Select(c => c.Value).ToHashSet();
            CategoryOptions.Clear();
            foreach (var cat in _allItems.Select(CategoryKey).Concat(ticked).Distinct()
                         .OrderBy(c => c == NoCategory).ThenBy(c => c, StringComparer.CurrentCultureIgnoreCase))
            {
                var opt = new CategoryFilterOption(cat, cat == NoCategory ? string.Empty : cat)
                    { IsSelected = ticked.Contains(cat) };
                opt.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(CategoryFilterOption.IsSelected) || _bulkCategoryEdit) return;
                    CategoryTicked(opt);
                };
                CategoryOptions.Add(opt);
            }
            VisibleCategoryOptions.Clear(); // rebuilt by the next ApplyFilter
        }

        private void CategoryTicked(CategoryFilterOption opt)
        {
            var chip = FilterChips.FirstOrDefault(c => c.IsCategory && c.Value == opt.Name);
            if (opt.IsSelected && chip == null) FilterChips.Add(new FilterChip(isCategory: true, opt.Name, opt.IconCategory));
            else if (!opt.IsSelected && chip != null) FilterChips.Remove(chip);
            ChipsChanged();
            ApplyFilter();
        }

        private static string CategoryKey(FamilyBrowserItemViewModel i) =>
            string.IsNullOrEmpty(i.Category) ? NoCategory : i.Category!;

        // Families under an ignored subfolder or matching an ignored-file pattern stay in the
        // DB but are hidden — and never used to plan where Save to Library puts a file.
        private Func<FamilyBrowserItemViewModel, bool> IgnoredRule()
        {
            var folders = _config.IgnoredSubfolders;
            var files = new IgnoredFileMatcher(_config.IgnoredFilePatterns);
            return i => (folders != null && folders.Count > 0 && PathUtil.IsUnderIgnoredFolder(i.RelativePath, folders))
                     || (files.HasPatterns && files.IsIgnored(i.FileName));
        }

        private void ApplyFilter()
        {
            // Hidden ignored subfolders and file patterns (e.g. Revit backups, kept in the DB),
            // then the tags (AND) and ⭐: what's left is what the dropdown counts. The words
            // (each in the name or a tag, any order) and the ticked categories (OR) narrow it
            // to the list. Rules and truth tables: Core's FamilyFilter / FamilyFilterTests.
            var ignored = IgnoredRule();
            var required = FilterChips.Where(c => c.IsTag).Select(c => c.Value).ToList();
            var categories = new HashSet<string>(FilterChips.Where(c => c.IsCategory).Select(c => c.Value));
            var tokens = FamilyFilter.Tokens(_searchText);
            var counted = _allItems.Where(i => !ignored(i)
                    && (!_showFavoriteFamilies || i.IsFavorite)
                    && FamilyFilter.HasAllTags(i.AllTags, required))
                .ToList();
            var filtered = counted.Where(i => FamilyFilter.MatchesText(i.DisplayName, i.AllTags, tokens)
                                           && FamilyFilter.MatchesCategory(CategoryKey(i), categories));
            UpdateSearchDropdown(counted, required);

            // Rebuilding the list makes both ListBoxes push a null selection (ignored by the
            // setter); put the user's selection back unless it was just filtered out.
            var keep = _selectedItem;
            _rebuildingList = true;
            try
            {
                FilteredItems.Clear();
                foreach (var item in filtered.OrderBy(i => i.DisplayName))
                    FilteredItems.Add(item);
            }
            finally { _rebuildingList = false; }
            if (keep != null && !FilteredItems.Contains(keep)) SelectedItem = null;
            else OnPropertyChanged(nameof(SelectedItem));
            RebuildTagChips(); // a ⟳ or a request can change the selected family's built-in tags
        }

        // The dropdown under the search box: categories with their counts (narrowed by the typed
        // text), and tags — the built-in ones always, every matching tag while typing.
        private void UpdateSearchDropdown(List<FamilyBrowserItemViewModel> counted, List<string> chosenTags)
        {
            var q = _searchText.Trim();
            var perCategory = counted.GroupBy(CategoryKey).ToDictionary(g => g.Key, g => g.Count());
            foreach (var o in CategoryOptions) o.Count = perCategory.TryGetValue(o.Name, out var n) ? n : 0;
            var visible = CategoryOptions.Where(o => q.Length == 0 || o.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            // Rebuilt only when it changes: recreating the checkboxes under a click would eat it.
            if (!visible.SequenceEqual(VisibleCategoryOptions))
            {
                VisibleCategoryOptions.Clear();
                foreach (var o in visible) VisibleCategoryOptions.Add(o);
            }

            var tagCounts = new Dictionary<string, (string Name, int Count)>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in BuiltInTags.All) tagCounts[b] = (b, 0);
            foreach (var i in counted)
                foreach (var t in i.AllTags)
                    tagCounts[t] = tagCounts.TryGetValue(t, out var e) ? (e.Name, e.Count + 1) : (t, 1);
            var offered = tagCounts.Values
                .Where(t => !chosenTags.Any(c => string.Equals(c, t.Name, StringComparison.OrdinalIgnoreCase)))
                .Where(t => q.Length == 0 ? BuiltInTags.IsBuiltIn(t.Name) : t.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(t => BuiltInTags.IsBuiltIn(t.Name) ? IndexOfBuiltIn(t.Name) : int.MaxValue)
                .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(12)
                .ToList();
            var first = q.Length == 0 ? (string?)null : offered.FirstOrDefault(t => t.Count > 0).Name;
            TagSuggestions.Clear();
            foreach (var t in offered) TagSuggestions.Add(new TagSuggestion(t.Name, t.Count, isFirst: t.Name == first));
            OnPropertyChanged(nameof(ShowTagSuggestions));
            OnPropertyChanged(nameof(ShowNoSearchMatch));
        }

        private static int IndexOfBuiltIn(string tag)
        {
            for (int i = 0; i < BuiltInTags.All.Count; i++)
                if (string.Equals(BuiltInTags.All[i], tag, StringComparison.OrdinalIgnoreCase)) return i;
            return int.MaxValue;
        }

        private void LoadDetailAsync(FamilyBrowserItemViewModel? item)
        {
            // Clear first: a slow or failed read must not leave the previous family's
            // instructions showing under this one.
            InstructionsXaml = null; Parameters = new List<ParameterModel>();
            // Model-only rows have no DB row (Id 0) — nothing to fetch, and Id 0 must never
            // reach the repository queries.
            if (item == null || item.IsModelOnly) return;
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
                        if (!string.Equals(item.Tags, tags, StringComparison.Ordinal))
                        {
                            // Edit Info changed them: the list and its counts follow.
                            item.SetTags(tags);
                            ApplyFilter();
                        }
                        RebuildTagChips();
                    });
                }
                catch { /* swallow — detail load failure is non-fatal */ }
            });
        }

        private async System.Threading.Tasks.Task LoadHelpDocumentAsync()
        {
            if (_helpLoaded) return;
            _helpLoaded = true; // also stops a second fetch while this one runs
            HelpSourceLabel = "Loading…";
            try
            {
                var markdown = await _http.GetStringAsync(HelpMarkdownUrl);
                HelpMarkdownRaw = markdown;
                HelpSourceLabel = "Source: " + HelpMarkdownUrl;
            }
            catch (Exception ex)
            {
                _helpLoaded = false; // try again the next time the panel opens
                HelpSourceLabel = "Couldn't load help — " + ex.Message;
                HelpMarkdownRaw =
                    "# Help unavailable\n\nCouldn't reach the docs repo. Check your network connection, " +
                    "or reach the RVTuk team directly.";
            }
        }

        private void Sync()
        {
            if (IsSyncing) return; // a Scan's refresh can arrive while one already runs
            IsSyncing = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                List<FamilyBrowserItemViewModel>? newAllItems = null; // stays null if the sync fails
                int outdated = 0;
                ProjectComparison? comparison = null;
                ProjectSnapshot? project = null;
                try
                {
                    // Read-only refresh: reload the index and compare it against the open
                    // project. Reconciling the DB with the .rfa files on disk (add new, prune
                    // deleted, flag changed) is the Scan button's job — refresh must never
                    // take write locks on the shared DB (backlog "Read Only DB").
                    var items = _repo.GetAllFamilies()
                        .Select(f => new FamilyBrowserItemViewModel(f))
                        .ToList();
                    ApplyFavorites(items);

                    // Version check compares the _Version shared parameter: the library value is
                    // captured into the index by the deep scan, the project value is read off the
                    // loaded family's symbols (or a placed instance when the parameter is
                    // instance-level). Ignored rows are not part of the library, and a name with
                    // several library copies gets no verdict (ProjectComparer, tested in Core).
                    var snapshot = _getProjectFamilies();
                    var ignored = IgnoredRule();
                    comparison = ProjectComparer.Compare(items.Select(i => new LibraryEntry
                    {
                        Id = i.Id, FileName = i.FileName, RelativePath = i.RelativePath,
                        Version = i.Version, IsIgnored = ignored(i),
                    }).ToList(), snapshot.Families);
                    foreach (var item in items)
                    {
                        if (comparison.Matches.TryGetValue(item.Id, out var pf))
                        {
                            // The library file as it is now, for the size and date noted at load
                            // time (FamilySourceCheck). Metadata only: Desktop Connector downloads nothing.
                            long? size = null;
                            DateTime? modified = null;
                            try
                            {
                                var file = new FileInfo(Path.Combine(_config.LibraryFolderPath, item.RelativePath));
                                if (file.Exists) { size = file.Length; modified = file.LastWriteTimeUtc; }
                            }
                            catch { /* unreadable: the version alone decides */ }
                            item.CompareWithProject(pf, size, modified);
                        }
                        else if (comparison.Duplicates.TryGetValue(item.Id, out var paths))
                            item.MarkDuplicate(projectFamily: snapshot.Families.First(f =>
                                string.Equals(f.Name, FamilyFileName.WithoutRfaExtension(item.FileName), StringComparison.OrdinalIgnoreCase)), paths);
                        if (item.VersionStatus == VersionStatus.UpdateAvailable) outdated++;
                    }
                    project = snapshot;

                    // Project families with no library counterpart get a synthetic "model only"
                    // row so the browser shows the whole picture, not just the library. The
                    // ".rfa" suffix keeps DisplayName's extension-stripping from eating part of
                    // a family name that contains a dot.
                    var modelOnly = comparison.ModelOnly;
                    if (modelOnly.Count > 0)
                    {
                        // No .rfa on disk to read a thumbnail from — render the loaded type's
                        // preview instead. Best-effort: rows without one just stay imageless.
                        IReadOnlyDictionary<string, byte[]> previews;
                        try { previews = _getFamilyPreviews(modelOnly.Select(pf => pf.Name).ToList()); }
                        catch { previews = new Dictionary<string, byte[]>(); }
                        foreach (var pf in modelOnly)
                            items.Add(new FamilyBrowserItemViewModel(new FamilyBrowserItem
                            {
                                Id = 0,
                                FileName = pf.Name + ".rfa",
                                RelativePath = string.Empty,
                                Category = pf.Category,
                                Version = pf.Version,
                                VersionIsInstance = pf.VersionIsInstance,
                                VersionStatus = VersionStatus.ModelOnly,
                                ThumbnailPng = previews.TryGetValue(pf.Name, out var png) ? png : null,
                            }));
                    }
                    newAllItems = items;
                }
                catch (Exception ex)
                {
                    _dispatcher.BeginInvoke(new Action(() => Warn($"Sync failed: {ex.Message}")));
                }
                finally
                {
                    var finalItems = newAllItems;
                    int finalOutdated = outdated;
                    var finalComparison = comparison;
                    var finalProject = project;
                    // Dispatcher.Invoke rethrows delegate exceptions on THIS pool thread, where
                    // anything unhandled kills the whole Revit process — degrade to the same
                    // warning the sync-failure path shows instead.
                    try
                    {
                        _dispatcher.Invoke(() =>
                        {
                            IsSyncing = false;
                            if (finalItems == null) return; // failed: keep showing the last good list

                            // The re-read rows are new objects: carry the selection over to the
                            // same family so the detail pane stays put.
                            var selected = _selectedItem;
                            if (selected != null)
                                _selectedItem = finalItems.FirstOrDefault(i => i.Id == selected.Id
                                    && (i.Id != 0 || string.Equals(i.FileName, selected.FileName, StringComparison.OrdinalIgnoreCase)))
                                    ?? selected;
                            _allItems = finalItems;
                            ApplyRequestFlags();
                            _stash = null;
                            IsComparisonHidden = false;
                            _lastComparison = finalComparison;
                            _comparedDocumentKey = finalProject?.DocumentKey;
                            ComparedProjectTitle = finalProject?.Title;
                            LoadCategories();
                            OutdatedCount = finalOutdated;
                            // The user switched while ⟳ ran: this comparison is already about
                            // a project that isn't active.
                            if (_activeDocumentKey != null && _comparedDocumentKey != null
                                && _activeDocumentKey != _comparedDocumentKey)
                                HideComparison();
                            ApplyFilter();
                            OnPropertyChanged(nameof(ShowUpdateInProject));
                            ReloadRequests(); // ⟳ also picks up requests sent since the browser opened
                        });
                    }
                    catch (Exception ex)
                    {
                        _dispatcher.BeginInvoke(new Action(() =>
                        {
                            IsSyncing = false;
                            Warn($"Sync failed: {ex.Message}");
                        }));
                    }
                }
            });
        }

        private void LoadSelected()
        {
            if (SelectedItem != null) LoadFamiliesSequentially(new[] { SelectedItem });
        }

        private void RecountOutdated()
        {
            var ignored = IgnoredRule();
            OutdatedCount = _allItems.Count(i => i.VersionStatus == VersionStatus.UpdateAvailable && !ignored(i));
            OnPropertyChanged(nameof(ShowUpdateInProject));
        }

        // Twin: LoadFamilyEventHandler.ProjectChangedMessage in RVTuk.Revit (not visible from here).
        private const string ProjectChangedMessage = "The active project changed. Press ⟳ and try again.";
        private const string StoppedProjectChanged = "Stopped: another project became active.";

        private void UpdateAll()
        {
            if (IsComparisonHidden || _comparedDocumentKey == null) return;
            var ignored = IgnoredRule();
            var items = _allItems.Where(i => i.VersionStatus == VersionStatus.UpdateAvailable && !ignored(i))
                .OrderBy(i => i.DisplayName).ToList();
            if (items.Count == 0) return;

            // ponytail: a plain message box, not a custom dark dialog — swap in a window if the
            // list ever needs scrolling or per-row choices.
            const int shown = 25;
            var text = new System.Text.StringBuilder();
            text.AppendLine($"Update {items.Count} families in \"{ComparedProjectTitle}\"?");
            text.AppendLine("Project values are kept.");
            text.AppendLine();
            foreach (var i in items.Take(shown))
                text.AppendLine($"{i.DisplayName}    v{i.ProjectVersion} → v{i.Version}");
            if (items.Count > shown) text.AppendLine($"…and {items.Count - shown} more.");
            var skipped = _lastComparison?.SkippedDuplicates ?? Array.Empty<SkippedDuplicate>();
            if (skipped.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Skipped — more than one copy in the library:");
                foreach (var d in skipped) text.AppendLine($"{d.Name}  ({d.RelativePaths.Count} copies)");
            }
            if (Warn(text.ToString(), MessageBoxButton.OKCancel, MessageBoxImage.Question, "Update All") != MessageBoxResult.OK)
                return;
            LoadFamiliesSequentially(items, _comparedDocumentKey, batch: true);
        }

        // ── Project switch ──────────────────────────────────────────────────────────────
        // The comparison belongs to the project the last ⟳ looked at. While another document
        // is active it is hidden (stashed), and coming back restores it without a new Sync.
        private List<(FamilyBrowserItemViewModel Item, VersionStatus Status)>? _stash;
        private bool _isComparisonHidden;
        public bool IsComparisonHidden
        {
            get => _isComparisonHidden;
            private set => SetProperty(ref _isComparisonHidden, value);
        }

        private string? _activeDocumentKey; // last one Revit reported active (null until a switch)

        public void ActiveDocumentChanged(string documentKey)
        {
            _activeDocumentKey = documentKey;
            if (_comparedDocumentKey == null) return; // nothing compared yet
            bool isCompared = documentKey == _comparedDocumentKey;
            if (!isCompared && _stash == null) HideComparison();
            else if (isCompared && _stash != null) RestoreComparison();
        }

        public void DocumentClosing(string documentKey)
        {
            if (_comparedDocumentKey == null || documentKey != _comparedDocumentKey) return;
            if (_stash == null) HideComparison();
            _stash = null; // that project is gone: nothing to come back to
            _comparedDocumentKey = null;
            ComparedProjectTitle = null;
        }

        private void HideComparison()
        {
            if (IsBatchRunning) _batchStopReason ??= StoppedProjectChanged;
            _stash = _allItems.Where(i => i.VersionStatus != VersionStatus.None)
                .Select(i => (i, i.VersionStatus)).ToList();
            _allItems = _allItems.Where(i => !i.IsModelOnly).ToList();
            foreach (var (item, _) in _stash) if (item.Id != 0) item.VersionStatus = VersionStatus.None;
            IsComparisonHidden = true;
            RecountOutdated();
            LoadCategories();
            ApplyFilter();
        }

        // While hidden, the visible rows describe no project: a load into another document
        // shows nothing, and a batch load into the compared project updates the stash.
        private void MarkLoaded(FamilyBrowserItemViewModel item, string? documentKey)
        {
            if (_stash == null) { item.VersionStatus = VersionStatus.UpToDate; return; }
            if (documentKey == null || documentKey != _comparedDocumentKey) return;
            int at = _stash.FindIndex(e => ReferenceEquals(e.Item, item));
            if (at >= 0) _stash[at] = (item, VersionStatus.UpToDate);
        }

        private void RestoreComparison()
        {
            foreach (var (item, status) in _stash!)
            {
                item.VersionStatus = status;
                if (status == VersionStatus.ModelOnly) _allItems.Add(item);
            }
            _stash = null;
            IsComparisonHidden = false;
            RecountOutdated();
            LoadCategories();
            ApplyFilter();
        }

        // Update All progress in the footer; null when no batch runs.
        private string? _batchProgressText;
        public string? BatchProgressText
        {
            get => _batchProgressText;
            private set { SetProperty(ref _batchProgressText, value); OnPropertyChanged(nameof(IsBatchRunning)); }
        }
        public bool IsBatchRunning => _batchProgressText != null;

        // Set by Cancel or a project switch; checked between families (the one in progress finishes).
        private volatile string? _batchStopReason;
        public ICommand CancelBatchCommand { get; private set; } = null!;

        // Loads families one at a time on a single background worker. LoadFamilyHandler is a
        // shared singleton and ExternalEvent.Raise() coalesces, so firing several loads at once
        // (Update All) would race on its state — _loadLock serialises every ping-pong, even
        // across overlapping batches.
        // expectedDocumentKey: Update All passes the project it confirmed, so a load never lands
        // in another project; single-row Load passes null and loads into whatever is active.
        private void LoadFamiliesSequentially(IReadOnlyList<FamilyBrowserItemViewModel> items,
            string? expectedDocumentKey = null, bool batch = false)
        {
            IsLoading = true;
            _batchStopReason = null;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                // One report at the end, not a dialog per family (Update All can be dozens).
                var failures = new List<string>();
                int done = 0;
                for (int n = 0; n < items.Count; n++)
                {
                    if (_batchStopReason != null) break;
                    var item = items[n];
                    if (batch)
                    {
                        var progress = $"Updating {n + 1} of {items.Count} — {item.DisplayName}";
                        _dispatcher.Invoke(() => BatchProgressText = progress);
                    }
                    var fullPath = Path.Combine(_config.LibraryFolderPath, item.RelativePath);
                    try
                    {
                        bool success;
                        string? error;
                        lock (_loadLock)
                        {
                            (success, error) = _loadFamily(fullPath, expectedDocumentKey);
                        }
                        if (success)
                        {
                            done++;
                            _dispatcher.Invoke(() => MarkLoaded(item, expectedDocumentKey));
                        }
                        else if (error == ProjectChangedMessage) _batchStopReason = StoppedProjectChanged;
                        else failures.Add($"{item.DisplayName}: {error ?? "Revit did not load the family."}");
                    }
                    catch (RevitBusyException ex)
                    {
                        // Every remaining family would wait the same 10 s and fail the same way.
                        _batchStopReason = ex.Message;
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{item.DisplayName}: {ex.Message}");
                    }
                }
                var stopReason = _batchStopReason;
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    IsLoading = false;
                    BatchProgressText = null;
                    RecountOutdated();
                    var report = new System.Text.StringBuilder();
                    if (stopReason != null)
                        report.Append(batch ? $"Updated {done} of {items.Count}. {stopReason}" : stopReason);
                    if (failures.Count > 0)
                        report.Append((report.Length > 0 ? "\n\n" : "") + "Failed to load:\n\n" +
                            string.Join("\n", failures.Take(10)) +
                            (failures.Count > 10 ? $"\n…and {failures.Count - 10} more." : ""));
                    if (report.Length > 0) Warn(report.ToString());
                }));
            });
        }

        private void RequestEditInfo()
        {
            if (SelectedItem != null)
                EditInfoRequested?.Invoke(SelectedItem);
        }

        // Favourites are personal: the row's shared IsFavorite column is replaced by this
        // user's own list when one is in use.
        private void ApplyFavorites(IEnumerable<FamilyBrowserItemViewModel> items)
        {
            if (_favorites == null) return;
            foreach (var i in items) i.IsFavorite = _favorites.Contains(i.RelativePath);
        }

        private void ToggleFavorite(FamilyBrowserItemViewModel? item)
        {
            if (item == null || item.IsModelOnly) return; // no DB row to persist a favourite on
            item.IsFavorite = !item.IsFavorite;
            if (_favorites != null) _favorites.Set(item.RelativePath, item.IsFavorite); // false: this session only
            else
            {
                try { _repo.SetFavorite(item.Id, item.IsFavorite); }
                catch { /* read-only share; favourite stays in-memory only */ }
            }
            if (_showFavoriteFamilies) ApplyFilter(); // only the ⭐ filter cares
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
                string? error;
                try { (ok, error) = _rescanFamily(item.Id, fullPath); }
                catch (Exception ex) { ok = false; error = ex.Message; }
                FamilyBrowserItem? fresh = null;
                if (ok)
                {
                    try { fresh = _repo.GetFamily(item.Id); }
                    catch { /* the row refresh is best-effort; the detail pane still reloads */ }
                }
                // Same guard as Sync: an exception rethrown by Invoke on this pool thread
                // would take down Revit.
                try
                {
                    _dispatcher.Invoke(() =>
                    {
                        IsRescanning = false;
                        if (ok)
                        {
                            // The point of a rescan is usually a fixed _Version or category:
                            // show it in the row and re-judge it against the project now.
                            if (fresh != null)
                            {
                                item.Refresh(fresh);
                                if (_stash != null) item.VersionStatus = VersionStatus.None;
                                LoadCategories();
                                ApplyFilter();
                                RecountOutdated();
                            }
                            LoadDetailAsync(item);
                        }
                        else Warn("Could not rescan this family." +
                                  (string.IsNullOrEmpty(error) ? "" : "\n\n" + error));
                    });
                }
                catch
                {
                    _dispatcher.BeginInvoke(new Action(() => IsRescanning = false));
                }
            });
        }

        // Opens the .rfa directly in Revit's Family Editor. Runs off the UI thread since it
        // blocks on the same ExternalEvent ping-pong pattern as load/rescan.
        private void OpenInFamilyEditor()
        {
            var item = SelectedItem;
            if (item == null) return;
            Func<(bool Success, string? Error)> open;
            if (item.IsModelOnly)
            {
                // No .rfa behind this row: edit the family loaded in the model. The handler
                // detours through a temp file named exactly after the family, which requires
                // a path-legal name.
                var name = item.DisplayName;
                if (!FamilyFileName.IsSafeFileName(name))
                {
                    Warn($"'{name}' can't be opened for editing from here because its name contains " +
                         "characters Windows doesn't allow in file names. Rename the family in the " +
                         "project first.");
                    return;
                }
                open = () => _openModelFamilyInEditor(name);
            }
            else
            {
                var fullPath = Path.Combine(_config.LibraryFolderPath, item.RelativePath);
                open = () => _openInFamilyEditor(fullPath);
            }

            IsLoading = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string? error = null;
                try
                {
                    // _loadLock: the open handlers are shared singletons (EditProjectFamily also
                    // serves Save to Library) and ExternalEvent.Raise() coalesces — serialize.
                    lock (_loadLock)
                    {
                        var (ok, err) = open();
                        if (!ok) error = err ?? "Unknown error.";
                    }
                }
                catch (Exception ex) { error = ex.Message; }
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    IsLoading = false;
                    if (error != null) Warn($"Could not open family editor: {error}");
                }));
            });
        }

        // Saves a model-only family's .rfa into the library folder (most common folder of its
        // category, else a folder named after the category, else the root). The DB is not
        // touched — reconciling disk with the index is the Scan's job, so the row stays
        // "model only" until the next Scan picks the new file up.
        private void SaveToLibrary()
        {
            var item = SelectedItem;
            if (item == null || !item.IsModelOnly) return;
            if (!CanEdit) { SuggestToLibrary(item); return; }

            // Ignored folders (archives, backups) must not decide where the file goes: the scan
            // never walks them, so a family saved there would never be indexed.
            var ignored = IgnoredRule();
            var libraryItems = _allItems.Where(i => !i.IsModelOnly && !ignored(i))
                .Select(i => (i.Category, i.RelativePath));
            var (relativePath, error) = SaveToLibraryPlanner.Plan(item.DisplayName, item.Category, libraryItems);
            if (error != null)
            {
                Warn(error);
                return;
            }

            var fullPath = Path.Combine(_config.LibraryFolderPath, relativePath!);
            if (File.Exists(fullPath))
            {
                var answer = Warn($"{fullPath} already exists (not indexed yet). Overwrite it?",
                    MessageBoxButton.YesNo, caption: "RVTuk — Save to Library");
                if (answer != MessageBoxResult.Yes) return;
            }

            var name = item.DisplayName;
            IsSavingToLibrary = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ok;
                string? err;
                try
                {
                    lock (_loadLock) // shared handler with the model-only editor path — serialize
                    {
                        (ok, err) = _saveFamilyToLibrary(name, fullPath);
                    }
                }
                catch (Exception ex) { ok = false; err = ex.Message; }
                // Same guard as Sync: Invoke rethrows delegate exceptions on this pool thread,
                // where anything unhandled kills the whole Revit process.
                try
                {
                    _dispatcher.Invoke(() =>
                    {
                        IsSavingToLibrary = false;
                        if (ok)
                            Warn($"Saved to:\n{fullPath}\n\nIt will appear in the library after the next Scan.",
                                icon: MessageBoxImage.Information, caption: "RVTuk — Save to Library");
                        else
                            Warn($"Could not save to library: {err}");
                    });
                }
                catch
                {
                    _dispatcher.BeginInvoke(new Action(() => IsSavingToLibrary = false));
                }
            });
        }

        // ── Requests ────────────────────────────────────────────────────────────────────
        // Users ask the maintainer to fix a family or add one. Each request is its own small
        // file in the library's .Requests folder (Core's RequestStore), which everyone can add
        // files to; the maintainer marks them done. Read when the browser opens and on ⟳.
        private readonly RequestStore? _requestStore;
        private readonly string _currentUser;
        private List<FamilyRequest> _openRequests = new();
        private int _doneRequestCount;
        private int _requestsGeneration; // a stale background read never overwrites a newer list

        public ObservableCollection<RequestItemViewModel> NewFamilyRequests { get; } = new();
        public ObservableCollection<RequestItemViewModel> FixRequests { get; } = new();
        public ObservableCollection<RequestItemViewModel> SuggestedFamilyRequests { get; } = new();
        public ObservableCollection<RequestItemViewModel> SelectedFamilyRequests { get; } = new();

        public int OpenRequestCount => _openRequests.Count;
        public bool HasOpenRequests => _openRequests.Count > 0;
        public bool HasSelectedFamilyRequests => SelectedFamilyRequests.Count > 0;
        public bool HasNoNewFamilyRequests => NewFamilyRequests.Count == 0;
        public bool HasNoFixRequests => FixRequests.Count == 0;
        public bool HasNoSuggestedFamilyRequests => SuggestedFamilyRequests.Count == 0;
        public string SuggestedFamilyRequestsTitle => $"SUGGESTED FAMILIES ({SuggestedFamilyRequests.Count})";
        public string NewFamilyRequestsTitle => $"NEW FAMILIES ({NewFamilyRequests.Count})";
        public string FixRequestsTitle => $"FIXES ({FixRequests.Count})";
        public string SelectedRequestsTitle => SelectedFamilyRequests.All(r => r.IsSuggestion)
            ? "Suggested for the library"
            : SelectedFamilyRequests.Count == 1 ? "A fix is requested" : $"{SelectedFamilyRequests.Count} fixes are requested";
        public string SelectedRequestsIcon => SelectedFamilyRequests.All(r => r.IsSuggestion) ? "box" : "tool";
        public string RequestsButtonToolTip => _openRequests.Count == 0 ? "Requests" : $"Requests ({_openRequests.Count} open)";
        public string RequestsIntro => CanEdit
            ? "What users asked for. Approve adds a suggested family to the library; Done and Reject move a request to .Requests\\Done, so you keep a history."
            : "Everyone can see these, so check here before asking for the same thing twice. You can withdraw your own requests.";
        public string DoneRequestsLine => _doneRequestCount == 0 ? string.Empty
            : $"{_doneRequestCount} done request{(_doneRequestCount == 1 ? "" : "s")} kept in .Requests\\Done.";

        private void ReloadRequests()
        {
            var store = _requestStore;
            if (store == null) return;
            int generation = ++_requestsGeneration;
            bool tidy = CanEdit;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                List<FamilyRequest> open;
                int done;
                try
                {
                    if (tidy)
                    {
                        // The maintainer's browser makes the folder (a view-only user's would
                        // never upload), then moves withdrawn requests out of the way.
                        try { store.EnsureFolder(); } catch { /* read-only after all: users see "no folder yet" */ }
                        store.TidyWithdrawn();
                    }
                    open = store.LoadOpen().ToList();
                    done = store.CountDone();
                }
                catch { return; } // requests are extras: never break the browser over them
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    if (generation != _requestsGeneration) return;
                    _openRequests = open;
                    _doneRequestCount = done;
                    RequestsChanged();
                }));
            });
        }

        private void RequestsChanged()
        {
            ApplyRequestFlags();
            Fill(NewFamilyRequests, _openRequests.Where(r => r.Kind == FamilyRequest.KindNewFamily));
            Fill(SuggestedFamilyRequests, _openRequests.Where(r => r.Kind == FamilyRequest.KindSuggestion));
            Fill(FixRequests, _openRequests.Where(IsFixKind));
            UpdateSelectedRequests();
            OnPropertyChanged(nameof(OpenRequestCount));
            OnPropertyChanged(nameof(HasOpenRequests));
            OnPropertyChanged(nameof(HasNoNewFamilyRequests));
            OnPropertyChanged(nameof(HasNoFixRequests));
            OnPropertyChanged(nameof(HasNoSuggestedFamilyRequests));
            OnPropertyChanged(nameof(SuggestedFamilyRequestsTitle));
            OnPropertyChanged(nameof(NewFamilyRequestsTitle));
            OnPropertyChanged(nameof(FixRequestsTitle));
            OnPropertyChanged(nameof(RequestsButtonToolTip));
            OnPropertyChanged(nameof(DoneRequestsLine));
            ApplyFilter(); // "Fix requested" is a tag: the list and the counts follow
        }

        private void Fill(ObservableCollection<RequestItemViewModel> target, IEnumerable<FamilyRequest> requests)
        {
            target.Clear();
            foreach (var r in requests) target.Add(new RequestItemViewModel(r, CanEdit, _currentUser));
        }

        // Any kind this add-in doesn't know is shown as a fix, so a newer add-in's request still shows.
        private static bool IsFixKind(FamilyRequest r) =>
            r.Kind != FamilyRequest.KindNewFamily && r.Kind != FamilyRequest.KindSuggestion;

        private void ApplyRequestFlags()
        {
            var paths = new HashSet<string>(_openRequests
                .Where(r => IsFixKind(r) && !string.IsNullOrEmpty(r.RelativePath))
                .Select(r => r.RelativePath!), StringComparer.OrdinalIgnoreCase);
            foreach (var i in _allItems) i.IsFixRequested = !i.IsModelOnly && paths.Contains(i.RelativePath);
        }

        private void UpdateSelectedRequests()
        {
            SelectedFamilyRequests.Clear();
            var item = _selectedItem;
            // A library family shows its fix requests; a family only in the model, its suggestion.
            var matching = item == null ? Enumerable.Empty<FamilyRequest>()
                : item.IsModelOnly
                    ? _openRequests.Where(r => r.Kind == FamilyRequest.KindSuggestion
                        && string.Equals(r.FamilyName, item.DisplayName, StringComparison.OrdinalIgnoreCase))
                    : _openRequests.Where(r => IsFixKind(r)
                        && string.Equals(r.RelativePath, item.RelativePath, StringComparison.OrdinalIgnoreCase));
            foreach (var r in matching) SelectedFamilyRequests.Add(new RequestItemViewModel(r, CanEdit, _currentUser));
            OnPropertyChanged(nameof(HasSelectedFamilyRequests));
            OnPropertyChanged(nameof(SelectedRequestsTitle));
            OnPropertyChanged(nameof(SelectedRequestsIcon));
        }

        private void RequestFix()
        {
            var item = SelectedItem;
            if (item == null || item.IsModelOnly) return;
            bool inProject = !IsComparisonHidden && item.VersionStatus is VersionStatus.UpToDate
                or VersionStatus.UpdateAvailable or VersionStatus.DuplicateInLibrary or VersionStatus.Unknown;
            var answer = RequestDialog?.Invoke(new RequestDialogInput
                { IsFix = true, FamilyName = item.DisplayName, CanAttach = inProject });
            if (answer == null) return;
            SendRequest(FamilyRequest.KindFix, item, answer.Text, answer.Attach && inProject);
        }

        private void RequestNewFamily()
        {
            var answer = RequestDialog?.Invoke(new RequestDialogInput { IsFix = false });
            if (answer == null) return;
            SendRequest(FamilyRequest.KindNewFamily, null, answer.Text, attach: false);
        }

        // A view-only user's Save to Library: the family goes to the maintainer as a suggestion.
        private void SuggestToLibrary(FamilyBrowserItemViewModel item)
        {
            if (_requestStore == null) return;
            var name = item.DisplayName;
            if (!FamilyFileName.IsSafeFileName(name))
            {
                Warn($"'{name}' can't be suggested because its name contains characters Windows doesn't " +
                     "allow in file names. Rename the family in the project first.");
                return;
            }
            if (_openRequests.Any(r => r.Kind == FamilyRequest.KindSuggestion
                    && string.Equals(r.FamilyName, name, StringComparison.OrdinalIgnoreCase)))
            {
                Warn($"'{name}' is already suggested for the library. The inbox at the bottom lists it.",
                    icon: MessageBoxImage.Information, caption: "RVTuk — Suggest for Library");
                return;
            }
            var answer = RequestDialog?.Invoke(new RequestDialogInput { IsSuggestion = true, FamilyName = name });
            if (answer == null) return;
            SendRequest(FamilyRequest.KindSuggestion, item, answer.Text, attach: true);
        }

        private void SendRequest(string kind, FamilyBrowserItemViewModel? item, string text, bool attach)
        {
            var store = _requestStore;
            bool suggestion = kind == FamilyRequest.KindSuggestion;
            // A suggestion's note is optional: the family itself is the point.
            if (store == null || (!suggestion && string.IsNullOrWhiteSpace(text))) return;
            var now = DateTime.UtcNow;
            var request = new FamilyRequest
            {
                Id = RequestStore.NewId(_currentUser, now),
                Kind = kind,
                RelativePath = suggestion ? null : item?.RelativePath,
                FamilyName = item?.DisplayName,
                Category = suggestion ? item?.Category : null,
                User = _currentUser,
                CreatedUtc = now.ToString("o"),
                Text = text.Trim(),
            };
            IsSendingRequest = true;
            ++_requestsGeneration; // a reload already under way must not drop this request
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string? attachError = null, error = null;
                bool folderReady;
                try { folderReady = store.FolderExists; }
                catch { folderReady = false; }
                if (!folderReady) error = RequestStore.MissingFolderMessage;
                else if (attach && item != null)
                {
                    try
                    {
                        var path = store.AttachmentPath(request.Id);
                        bool ok;
                        string? err;
                        lock (_loadLock) // shared Revit handler with Save to Library — serialize
                        {
                            (ok, err) = _saveFamilyToLibrary(item.ProjectFamilyName, path);
                        }
                        if (ok) request.Attachment = Path.GetFileName(path);
                        else attachError = err ?? "Revit did not save the family.";
                    }
                    catch (Exception ex) { attachError = ex.Message; }
                }
                // Without the family file a suggestion is nothing to approve: don't send it.
                if (error == null && suggestion && attachError != null)
                    error = "Revit could not save the family from the project:\n" + attachError;
                if (error == null)
                {
                    try { store.Save(request); }
                    catch (Exception ex) { error = ex.Message; }
                }
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    IsSendingRequest = false;
                    if (error != null)
                    {
                        Warn("The request was not sent:\n\n" + error + "\n\n" +
                             "Check that Autodesk Desktop Connector is running, and that you can add files to the library's .Requests folder.");
                        return;
                    }
                    if (!_openRequests.Any(r => r.Id == request.Id)) _openRequests.Add(request); // a newer reload may have it
                    RequestsChanged();
                    var msg = kind == FamilyRequest.KindFix ? $"Fix requested for {request.FamilyName}."
                        : suggestion ? $"'{request.FamilyName}' was sent to the library maintainer, who decides whether to add it."
                        : "Family request sent.";
                    msg += "\n\nThe library maintainer sees it once Desktop Connector has uploaded it, usually within a minute.";
                    if (attachError != null) msg += "\n\nYour project's version could not be attached, so it was sent without it:\n" + attachError;
                    Warn(msg, icon: MessageBoxImage.Information, caption: "RVTuk — Request");
                }));
            });
        }

        private void MarkRequestDone(RequestItemViewModel? item)
        {
            var store = _requestStore;
            if (item == null || store == null || !CanEdit) return;
            ChangeRequestInBackground(() => store.MarkDone(item.Request), "Could not mark the request as done:", () =>
            {
                _openRequests.RemoveAll(r => r.Id == item.Request.Id); // by id: a reload makes new objects
                _doneRequestCount++;
            });
        }

        private void WithdrawRequest(RequestItemViewModel? item)
        {
            var store = _requestStore;
            if (item == null || store == null || !item.CanWithdraw) return;
            if (Warn("Withdraw your request?", MessageBoxButton.OKCancel, MessageBoxImage.Question, "RVTuk — Request")
                != MessageBoxResult.OK) return;
            ChangeRequestInBackground(() => store.Withdraw(item.Request), "Could not withdraw the request:",
                () => _openRequests.RemoveAll(r => r.Id == item.Request.Id));
        }

        // The maintainer adds a suggested family: to the folder Save to Library would pick (most
        // families of its category), or another one under the library. Its file takes the
        // family's own name, which Revit uses as the family name when it is loaded.
        private void ApproveSuggestion(RequestItemViewModel? item)
        {
            var store = _requestStore;
            if (item == null || store == null || !CanEdit || !item.IsSuggestion) return;
            var name = item.Request.FamilyName;
            if (string.IsNullOrWhiteSpace(name) || !FamilyFileName.IsSafeFileName(name!))
            {
                Warn("This suggestion has no family name that can be a file name. Reject it and ask for it again.");
                return;
            }
            var ignored = IgnoredRule();
            var libraryItems = _allItems.Where(i => !i.IsModelOnly && !ignored(i))
                .Select(i => (i.Category, i.RelativePath));
            var (relativePath, error) = SaveToLibraryPlanner.Plan(name!, item.Request.Category, libraryItems);
            if (error != null) { Warn(error); return; }
            var root = _config.LibraryFolderPath;
            var target = Path.Combine(root, relativePath!);

            var where = Warn($"Add '{name}' to the library as:\n\n{relativePath}\n\n" +
                             "Yes adds it there. No lets you choose another folder.",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question, "RVTuk — Approve");
            if (where == MessageBoxResult.Cancel || where == MessageBoxResult.None) return;
            if (where == MessageBoxResult.No)
            {
                using var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = $"Folder in the library for '{name}'",
                    SelectedPath = Path.GetDirectoryName(target) ?? root,
                };
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                var rel = PathUtil.GetRelativePath(root, dialog.SelectedPath);
                var own = new[] { AppConfig.DbFolderName, RequestStore.FolderName };
                if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel)
                    || PathUtil.IsUnderIgnoredFolder(rel, own.Concat(_config.IgnoredSubfolders ?? new List<string>())))
                {
                    Warn("Choose a folder inside the library, outside .DB, .Requests and the ignored subfolders: " +
                         "a family anywhere else is never scanned.");
                    return;
                }
                target = Path.Combine(dialog.SelectedPath, name + ".rfa");
            }

            bool overwrite = false;
            if (File.Exists(target))
            {
                if (Warn($"{target} already exists. Replace it with the suggested family?",
                        MessageBoxButton.YesNo, caption: "RVTuk — Approve") != MessageBoxResult.Yes) return;
                overwrite = true;
            }
            ChangeRequestInBackground(() => store.ApproveSuggestion(item.Request, target, overwrite),
                "Could not add the family to the library:", () =>
                {
                    _openRequests.RemoveAll(r => r.Id == item.Request.Id);
                    _doneRequestCount++;
                },
                $"Added to:\n{target}\n\nIt will appear in the library after the next Scan.");
        }

        // The folder is on Desktop Connector, which can stall: file work stays off the UI thread.
        private void ChangeRequestInBackground(Action fileWork, string failure, Action onDone, string? doneMessage = null)
        {
            ++_requestsGeneration; // a reload already under way must not undo this change
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string? error = null;
                try { fileWork(); }
                catch (Exception ex) { error = ex.Message; }
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    if (error != null) { Warn(failure + "\n\n" + error); return; }
                    onDone();
                    RequestsChanged();
                    if (doneMessage != null)
                        Warn(doneMessage, icon: MessageBoxImage.Information, caption: "RVTuk — Approve");
                }));
            });
        }

        // From the Requests list to the family: clears the search so it can't be filtered out.
        private void ShowRequestFamily(RequestItemViewModel? item)
        {
            if (item == null || !item.IsFix) return;
            var family = _allItems.FirstOrDefault(i => !i.IsModelOnly
                && string.Equals(i.RelativePath, item.Request.RelativePath, StringComparison.OrdinalIgnoreCase));
            if (family == null)
            {
                Warn($"'{item.Title}' is no longer in the library.");
                return;
            }
            _showFavoriteFamilies = false;
            OnPropertyChanged(nameof(ShowFavoriteFamilies));
            ClearSearchAndChips();
            SelectedItem = family;
        }

        public void Dispose() => _repo.Dispose();
    }
}
