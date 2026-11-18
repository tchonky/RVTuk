using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;
using System.Collections.Generic;
using RVTuk.Core.Shared.Config;
using RVTuk.Core.FamilyBrowser.Database;
using RVTuk.Core.FamilyBrowser.Models;
using RVTuk.UI.FamilyBrowser.ViewModels;
using RVTuk.UI.Shared.Controls;

namespace RVTuk.UI.FamilyBrowser.Views
{
    public partial class FamilyBrowserWindow : Window
    {
        private AppConfig _config = null!;
        private readonly Func<ProjectSnapshot> _getProjectFamilies;
        private readonly Func<string, string?, (bool Success, string? Error)> _loadFamily;
        private readonly Func<long, string, (bool Success, string? Error)> _rescanFamily;
        private readonly Action<bool, bool> _scan;
        private readonly Func<string, (bool Success, string? Error)> _openInFamilyEditor;
        private readonly Func<string, (bool Success, string? Error)> _openModelFamilyInEditor;
        private readonly Func<string, string, (bool Success, string? Error)> _saveFamilyToLibrary;
        private readonly Func<IReadOnlyList<string>, IReadOnlyDictionary<string, byte[]>> _getFamilyPreviews;

        // The single Edit-Info editor currently on screen, if any. Edit Info is only ever triggered
        // from within this FamilyBrowserWindow, so a per-window field (not an app-wide singleton) is
        // enough to enforce "one editor at a time". _openEditorItem is the family that editor is for,
        // used to detect a re-open of the same family.
        private InstructionsEditorWindow? _openEditor;
        private FamilyBrowserItemViewModel? _openEditorItem;

        public FamilyBrowserViewModel ViewModel { get; private set; } = null!;

        public FamilyBrowserWindow(
            AppConfig config,
            Func<ProjectSnapshot> getProjectFamilies,
            Func<string, string?, (bool Success, string? Error)> loadFamily,
            Func<long, string, (bool Success, string? Error)> rescanFamily,
            Action<bool, bool> scan,
            Func<string, (bool Success, string? Error)> openInFamilyEditor,
            Func<string, (bool Success, string? Error)> openModelFamilyInEditor,
            Func<string, string, (bool Success, string? Error)> saveFamilyToLibrary,
            Func<IReadOnlyList<string>, IReadOnlyDictionary<string, byte[]>> getFamilyPreviews)
        {
            InitializeComponent();
            _getProjectFamilies = getProjectFamilies;
            _loadFamily = loadFamily;
            _rescanFamily = rescanFamily;
            _scan = scan;
            _openInFamilyEditor = openInFamilyEditor;
            _openModelFamilyInEditor = openModelFamilyInEditor;
            _saveFamilyToLibrary = saveFamilyToLibrary;
            _getFamilyPreviews = getFamilyPreviews;
            // Links in the instructions and the Help page open in the web browser.
            AddHandler(Hyperlink.RequestNavigateEvent, new RequestNavigateEventHandler(RichTextBoxHelper.OpenLink));
            LoadWithConfig(config);
        }

        /// <summary>
        /// Re-loads the browser (and its embedded Settings) against the current saved config.
        /// Called after the library folder changes in the embedded Settings panel, so the
        /// browser doesn't keep showing a stale library.
        /// </summary>
        public void ReloadConfig()
        {
            // Runs from a Settings click: an exception here would reach the dispatcher's
            // last-chance handler, which closes the whole browser.
            try { LoadWithConfig(ConfigManager.LoadConfig()); }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not open the library:\n\n{ex.Message}", "RVTuk",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LoadWithConfig(AppConfig config)
        {
            // The library sits on Autodesk Desktop Connector. Its maintainer works on the shared
            // database in place; everyone else has view-only access and browses a local copy,
            // so they never hold the synced file open or read a half-synced one.
            bool canEdit = LibraryCache.CanWrite(config.DatabasePath);
            string dbPath;
            CacheRefreshResult? copy = null;
            if (canEdit)
            {
                AppConfig.MigrateLegacyDbFolder(config.LibraryFolderPath);
                Directory.CreateDirectory(Path.Combine(config.LibraryFolderPath, AppConfig.DbFolderName));
                dbPath = config.DatabasePath;
            }
            else
            {
                var cacheDir = config.GetDatabaseCacheFolder();
                copy = LibraryCache.Current(cacheDir);
                // First open on this PC: there is nothing to show until a copy is taken.
                if (copy.DatabasePath == null) copy = LibraryCache.Refresh(config.DatabasePath, cacheDir);
                dbPath = copy.DatabasePath ?? throw new IOException(
                    "Could not read the family library database:\n" + config.DatabasePath + "\n\n" +
                    (copy.Error ?? string.Empty) + "\n\n" +
                    "Check that Autodesk Desktop Connector is running and signed in, then try again.");
            }

            var repo = new BrowserRepository(dbPath);
            FamilyBrowserViewModel vm;
            try
            {
                vm = new FamilyBrowserViewModel(
                    config, repo, _getProjectFamilies, _loadFamily, _rescanFamily,
                    _scan, _openInFamilyEditor, _openModelFamilyInEditor, _saveFamilyToLibrary,
                    _getFamilyPreviews, onLibraryFolderChanged: ReloadConfig,
                    canEdit: canEdit, favorites: new PersonalFavorites(config.GetFavoritesPath()));
            }
            catch
            {
                repo.Dispose();
                throw;
            }
            vm.Owner = this;
            vm.EditInfoRequested += OnEditInfoRequested;

            if (ViewModel != null)
            {
                var old = ViewModel;
                old.EditInfoRequested -= OnEditInfoRequested;
                // An open editor still saves through the old library's repository — keep it
                // alive until that editor closes.
                if (_openEditor != null) _openEditor.Closed += (_, __) => old.Dispose();
                else old.Dispose();
            }

            _config = config;
            ViewModel = vm;
            DataContext = ViewModel;

            if (copy != null)
            {
                ShowCopyStatus(vm, copy);
                // A copy just taken is current; otherwise look for a newer one in the background.
                if (copy.Status != CacheRefreshStatus.Updated) RefreshCopyInBackground(vm, config);
            }
        }

        private static void ShowCopyStatus(FamilyBrowserViewModel vm, CacheRefreshResult copy)
        {
            var date = copy.CopyOfUtc?.ToLocalTime().ToString("g") ?? "?";
            vm.LibraryStatus = copy.Error != null
                ? $"Offline copy from {date}"
                : $"Library from {date}";
            vm.LibraryStatusDetail = copy.Error == null
                ? "You have view-only access, so the library is read from a copy on this PC. It updates when the library changes."
                : "Showing the last copy on this PC, because the library could not be read:\n" + copy.Error;
        }

        // Reopens the browser on the newer copy, unless the user has since switched library.
        private void RefreshCopyInBackground(FamilyBrowserViewModel vm, AppConfig config)
        {
            var sourceDb = config.DatabasePath;
            var cacheDir = config.GetDatabaseCacheFolder();
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                var result = LibraryCache.Refresh(sourceDb, cacheDir);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!ReferenceEquals(ViewModel, vm) || !IsLoaded) return; // switched library or closed
                    if (result.Status == CacheRefreshStatus.Updated)
                    {
                        try { LoadWithConfig(config); }
                        catch (Exception ex) { vm.LibraryStatusDetail = ex.Message; }
                    }
                    else ShowCopyStatus(vm, result);
                }));
            });
        }

        // WPF's native three-state CheckBox cycles true→null→false on each click, which would
        // let the user click their way into a stray "indeterminate" state. Indeterminate here is
        // display-only (computed from the individual category checkboxes), so this forces a
        // deterministic two-way toggle instead: check-all if not already fully checked, else
        // uncheck-all. The CheckBox's IsChecked binding is OneWay, so this Click handler is the
        // only thing driving the change.
        private void AllCategoriesCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool turnOn = ViewModel.IsAllCategoriesSelected != true;
            ViewModel.IsAllCategoriesSelected = turnOn;
        }

        // Edit Info is non-modal (editor.Show(), not ShowDialog()) so the Family Browser stays
        // interactive while an editor is open — that's what lets the user pick another family and
        // hit Edit Info again. We keep at most one editor on screen: closing the current one asks
        // about its unsaved changes itself, and the new one opens only if it actually closed.
        private void OnEditInfoRequested(FamilyBrowserItemViewModel item)
        {
            if (_openEditor != null)
            {
                // Same family already being edited: just surface the existing window.
                if (_openEditorItem != null && _openEditorItem.Id == item.Id)
                {
                    _openEditor.Activate();
                    return;
                }

                if (!CloseEditor()) return; // the user chose to keep editing
            }

            OpenEditorFor(item);
        }

        // Asks the open editor to close — it asks about unsaved changes itself. True when it did.
        private bool CloseEditor()
        {
            if (_openEditor == null) return true;
            // The browser stays clickable while the editor's question is up, and a second Close()
            // on a window that is mid-close throws: that question is still pending, so "not yet".
            try { _openEditor.Close(); }
            catch (InvalidOperationException) { return false; }
            return _openEditor == null;
        }

        private void OpenEditorFor(FamilyBrowserItemViewModel item)
        {
            var fullPath = Path.Combine(_config.LibraryFolderPath, item.RelativePath);
            var vm = ViewModel;
            InstructionsEditorWindow editor;
            try { editor = new InstructionsEditorWindow(item, fullPath, vm.Repo); }
            catch (Exception ex) // e.g. the shared DB is busy with another user's scan
            {
                MessageBox.Show(this, $"Could not open the editor:\n\n{ex.Message}", "RVTuk",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            editor.Owner = this; // owned: always above the browser, never behind it

            _openEditor = editor;
            _openEditorItem = item;

            editor.Closed += (s, e) =>
            {
                if (ReferenceEquals(_openEditor, editor))
                {
                    _openEditor = null;
                    _openEditorItem = null;
                }
                // The library was switched while this editor was open: its family is not in
                // the current list, so there is nothing to refresh.
                if (!ReferenceEquals(ViewModel, vm)) return;

                // Same post-edit refresh the old blocking ShowDialog() path ran inline: pull any new
                // custom thumbnail into the list row, and if the edited family is still selected,
                // reload its detail pane so saved instructions show. (If the user has since selected
                // another family, re-selecting this one later reloads it fresh anyway.)
                try { item.UpdateThumbnail(vm.Repo.GetResolvedThumbnail(item.Id)); }
                catch { /* best-effort; the next Sync refreshes it */ }
                if (ReferenceEquals(vm.SelectedItem, item))
                {
                    vm.SelectedItem = null;
                    vm.SelectedItem = item;
                }
            };

            editor.Show();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Close the editor first: WPF closes owned windows without raising their Closing, so
            // its unsaved-changes question would otherwise never be asked.
            if (!CloseEditor()) e.Cancel = true;
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            ViewModel?.Dispose();
            base.OnClosed(e);
        }
    }
}
