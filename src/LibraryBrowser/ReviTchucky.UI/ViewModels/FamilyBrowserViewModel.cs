using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ReviTchucky.Core.Config;
using ReviTchucky.Core.Database;
using ReviTchucky.Core.Models;
using ReviTchucky.Core.Util;

namespace ReviTchucky.UI.ViewModels
{
    public class FamilyBrowserViewModel : ViewModelBase, IDisposable
    {
        private readonly AppConfig _config;
        private readonly BrowserRepository _repo;
        private readonly Func<IReadOnlyList<string>> _getProjectFamilies;
        private readonly Func<string, (bool Success, string? Error)> _loadFamily;
        private readonly Action _deepScan;
        private readonly Func<long, string, bool> _rescanFamily;
        private readonly Dispatcher _dispatcher;
        private readonly object _loadLock = new object();

        private List<FamilyBrowserItemViewModel> _allItems = new();
        private string _searchText = string.Empty;
        private string _selectedCategory = "";
        private FamilyBrowserItemViewModel? _selectedItem;
        private bool _isSyncing;
        private bool _isRescanning;
        private bool _isShowingSettings;
        private int _outdatedCount;
        private string? _instructionsXaml;
        private List<ParameterModel> _parameters = new();
        private string _parameterFilter = string.Empty;
        public ObservableCollection<ParameterModel> FilteredParameters { get; } = new();
        public ObservableCollection<GalleryItemViewModel> GalleryItems { get; } = new();

        public ObservableCollection<FamilyBrowserItemViewModel> FilteredItems { get; } = new();
        public ObservableCollection<string> Categories { get; } = new();

        public string SearchText
        {
            get => _searchText;
            set { SetProperty(ref _searchText, value); ApplyFilter(); }
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set { SetProperty(ref _selectedCategory, value ?? ""); ApplyFilter(); }
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
                OnPropertyChanged(nameof(ShowUpdateInProject));
            }
        }

        public bool HasSelection => _selectedItem != null;
        public bool ShowUpdateInProject => _selectedItem?.VersionStatus == VersionStatus.UpdateAvailable;
        public bool ShowFamilyDetail => HasSelection && !_isShowingSettings;
        public string LibraryFolderPath => _config.LibraryFolderPath;

        public bool IsShowingSettings
        {
            get => _isShowingSettings;
            set
            {
                SetProperty(ref _isShowingSettings, value);
                OnPropertyChanged(nameof(ShowFamilyDetail));
            }
        }

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
            var q = _parameterFilter.Trim();
            IEnumerable<ParameterModel> src = _parameters;
            if (!string.IsNullOrEmpty(q))
                src = src.Where(p =>
                    (p.ParameterName?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (p.ParamGroup?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (p.Kind?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));

            FilteredParameters.Clear();
            foreach (var p in src) FilteredParameters.Add(p);
        }

        public BrowserRepository Repo => _repo;

        public ICommand SyncCommand { get; }
        public ICommand ToggleSettingsCommand { get; }
        public ICommand DeepScanCommand { get; }
        public ICommand UpdateAllCommand { get; }
        public ICommand LoadFamilyCommand { get; }
        public ICommand UpdateInProjectCommand { get; }
        public ICommand EditInfoCommand { get; }
        public ICommand RescanFamilyCommand { get; }

        public event Action<FamilyBrowserItemViewModel>? EditInfoRequested;

        public FamilyBrowserViewModel(
            AppConfig config,
            BrowserRepository repo,
            Func<IReadOnlyList<string>> getProjectFamilies,
            Func<string, (bool Success, string? Error)> loadFamily,
            Action deepScan,
            Func<long, string, bool> rescanFamily)
        {
            _config = config;
            _repo = repo;
            _getProjectFamilies = getProjectFamilies;
            _loadFamily = loadFamily;
            _deepScan = deepScan;
            _rescanFamily = rescanFamily;
            _dispatcher = Dispatcher.CurrentDispatcher;

            SyncCommand           = new RelayCommand(Sync, () => !IsSyncing);
            ToggleSettingsCommand = new RelayCommand(() => IsShowingSettings = !IsShowingSettings);
            DeepScanCommand       = new RelayCommand(() => { IsShowingSettings = false; _deepScan(); });
            UpdateAllCommand      = new RelayCommand(UpdateAll,     () => OutdatedCount > 0);
            LoadFamilyCommand     = new RelayCommand(LoadSelected,  () => SelectedItem != null);
            UpdateInProjectCommand= new RelayCommand(UpdateSelected,() => ShowUpdateInProject);
            EditInfoCommand       = new RelayCommand(RequestEditInfo, () => SelectedItem != null);
            RescanFamilyCommand   = new RelayCommand(RescanSelected, () => SelectedItem != null && !IsRescanning);

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
            Categories.Clear();
            Categories.Add(""); // "" = "All" sentinel (avoids null items in WPF 4.x CollectionView)
            foreach (var cat in _repo.GetCategories().Where(c => c != null))
                Categories.Add(cat!);
            SelectedCategory = "";
        }

        private void ApplyFilter()
        {
            // Multi-word search: split on whitespace; every token must appear somewhere in the
            // family name, in any order/position. e.g. "door single" matches "single - door".
            var tokens = _searchText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var filtered = _allItems.AsEnumerable();
            if (tokens.Length > 0)
                filtered = filtered.Where(i =>
                    tokens.All(t => i.DisplayName.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0));
            if (!string.IsNullOrEmpty(_selectedCategory))
                filtered = filtered.Where(i => i.Category == _selectedCategory);

            FilteredItems.Clear();
            foreach (var item in filtered.OrderBy(i => i.DisplayName))
                FilteredItems.Add(item);
        }

        private void LoadDetailAsync(FamilyBrowserItemViewModel? item)
        {
            if (item == null) { InstructionsXaml = null; Parameters = new List<ParameterModel>(); GalleryItems.Clear(); return; }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var xaml = _repo.GetInstructionsXaml(item.Id);
                    var prms = _repo.GetParameters(item.Id);
                    var images = _repo.GetImages(item.Id)
                        .Select(im => new GalleryItemViewModel(im.Id, im.Caption, _repo.GetGalleryPath(item.Id, im.FileName)))
                        .ToList();
                    _dispatcher.Invoke(() =>
                    {
                        InstructionsXaml = xaml;
                        Parameters = prms;
                        GalleryItems.Clear();
                        foreach (var g in images) GalleryItems.Add(g);
                    });
                }
                catch { /* swallow — detail load failure is non-fatal */ }
            });
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

                    foreach (var fullPath in PathUtil.SafeEnumerateFiles(root, "*.rfa"))
                    {
                        if (fullPath.Length >= 260) continue; // unusable on .NET Framework; skip
                        var relativePath = PathUtil.GetRelativePath(root, fullPath);
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

                    IReadOnlyList<string> projectFamilies = _getProjectFamilies();
                    var projectSet = new HashSet<string>(projectFamilies, StringComparer.OrdinalIgnoreCase);
                    foreach (var item in newAllItems)
                    {
                        var nameNoExt = Path.GetFileNameWithoutExtension(item.FileName);
                        if (!projectSet.Contains(nameNoExt)) continue;
                        var fullPath = Path.Combine(root, item.RelativePath);
                        bool isNewer = File.Exists(fullPath) &&
                            new FileInfo(fullPath).LastWriteTimeUtc > item.Model.ModifiedDate.AddSeconds(1);
                        item.VersionStatus = isNewer ? VersionStatus.UpdateAvailable : VersionStatus.UpToDate;
                        if (isNewer) outdated++;
                    }
                }
                catch (Exception ex)
                {
                    _dispatcher.BeginInvoke(new Action(() =>
                        MessageBox.Show($"Sync failed: {ex.Message}", "ReviTchucky",
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
                                MessageBox.Show($"Failed to load family: {error}", "ReviTchucky");
                        });
                    }
                    catch (Exception ex)
                    {
                        _dispatcher.BeginInvoke(new Action(() =>
                            MessageBox.Show($"Failed to load family: {ex.Message}", "ReviTchucky",
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
                _dispatcher.Invoke(() =>
                {
                    IsRescanning = false;
                    if (ok) LoadDetailAsync(item);
                    else MessageBox.Show("Could not rescan this family.", "ReviTchucky");
                });
            });
        }

        public void Dispose() => _repo.Dispose();
    }
}
