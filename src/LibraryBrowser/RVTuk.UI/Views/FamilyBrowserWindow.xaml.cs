using System;
using System.IO;
using System.Windows;
using System.Collections.Generic;
using RVTuk.Core.Config;
using RVTuk.Core.Database;
using RVTuk.UI.ViewModels;

namespace RVTuk.UI.Views
{
    public partial class FamilyBrowserWindow : Window
    {
        private AppConfig _config = null!;
        private readonly Func<IReadOnlyList<string>> _getProjectFamilies;
        private readonly Func<string, (bool Success, string? Error)> _loadFamily;
        private readonly Func<long, string, bool> _rescanFamily;
        private readonly Action<bool, bool> _scan;
        private readonly Action<string> _openInFamilyEditor;

        // The single Edit-Info editor currently on screen, if any. Edit Info is only ever triggered
        // from within this FamilyBrowserWindow, so a per-window field (not an app-wide singleton) is
        // enough to enforce "one editor at a time". _openEditorItem is the family that editor is for,
        // used both to detect a re-open of the same family and to name it in the discard prompt.
        private InstructionsEditorWindow? _openEditor;
        private FamilyBrowserItemViewModel? _openEditorItem;

        public FamilyBrowserViewModel ViewModel { get; private set; } = null!;

        public FamilyBrowserWindow(
            AppConfig config,
            Func<IReadOnlyList<string>> getProjectFamilies,
            Func<string, (bool Success, string? Error)> loadFamily,
            Func<long, string, bool> rescanFamily,
            Action<bool, bool> scan,
            Action<string> openInFamilyEditor)
        {
            InitializeComponent();
            _getProjectFamilies = getProjectFamilies;
            _loadFamily = loadFamily;
            _rescanFamily = rescanFamily;
            _scan = scan;
            _openInFamilyEditor = openInFamilyEditor;
            LoadWithConfig(config);
        }

        /// <summary>
        /// Re-loads the browser (and its embedded Settings) against the current saved config.
        /// Called after the library folder changes in the embedded Settings panel, so the
        /// browser doesn't keep showing a stale library.
        /// </summary>
        public void ReloadConfig() => LoadWithConfig(ConfigManager.LoadConfig());

        private void LoadWithConfig(AppConfig config)
        {
            _config = config;
            var setupDir = Path.Combine(config.LibraryFolderPath, ".Setup");
            Directory.CreateDirectory(setupDir);
            var repo = new BrowserRepository(config.DatabasePath);
            var vm = new FamilyBrowserViewModel(
                config, repo, _getProjectFamilies, _loadFamily, _rescanFamily,
                _scan, _openInFamilyEditor, onLibraryFolderChanged: ReloadConfig);
            vm.EditInfoRequested += OnEditInfoRequested;

            if (ViewModel != null)
            {
                ViewModel.EditInfoRequested -= OnEditInfoRequested;
                ViewModel.Dispose();
            }

            ViewModel = vm;
            DataContext = ViewModel;
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

        // Edit Info is now non-modal (editor.Show(), not ShowDialog()) so the Family Browser stays
        // interactive while an editor is open — that's what lets the user pick another family and
        // hit Edit Info again. We keep at most one editor on screen: if one is already open for a
        // DIFFERENT family, confirm discarding its unsaved changes, close it, and open the new one.
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

                var openName = _openEditorItem?.DisplayName ?? "the current family";
                var answer = MessageBox.Show(
                    this,
                    $"You have unsaved changes for '{openName}' open. " +
                    $"Discard them and edit '{item.DisplayName}' instead?",
                    "RVTuk — Discard changes?",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes)
                    return; // leave the open editor untouched

                // Discard: close the current editor without saving. Null the tracking first so its
                // Closed handler recognises this as a managed swap (see OpenEditorFor) and doesn't
                // clobber the field we're about to reassign to the new editor.
                var toClose = _openEditor;
                _openEditor = null;
                _openEditorItem = null;
                toClose.Close();
            }

            OpenEditorFor(item);
        }

        private void OpenEditorFor(FamilyBrowserItemViewModel item)
        {
            var fullPath = Path.Combine(_config.LibraryFolderPath, item.RelativePath);
            var editor = new InstructionsEditorWindow(
                item, ViewModel.InstructionsXaml, fullPath, ViewModel.Repo);
            editor.Owner = this;
            // The browser is Topmost; a non-modal, non-topmost child would render behind it and be
            // unusable, so the editor must be Topmost too to stay visible above the interactive browser.
            editor.Topmost = true;

            _openEditor = editor;
            _openEditorItem = item;

            editor.Closed += (s, e) =>
            {
                // Only clear tracking if this is still the active editor — a swap already cleared it
                // and pointed the field at the replacement, which we must not stomp.
                if (ReferenceEquals(_openEditor, editor))
                {
                    _openEditor = null;
                    _openEditorItem = null;
                }

                // Same post-edit refresh the old blocking ShowDialog() path ran inline: pull any new
                // custom thumbnail into the list row, and if the edited family is still selected,
                // reload its detail pane so saved instructions show. (If the user has since selected
                // another family, re-selecting this one later reloads it fresh anyway.)
                item.UpdateThumbnail(ViewModel.Repo.GetResolvedThumbnail(item.Id));
                if (ReferenceEquals(ViewModel.SelectedItem, item))
                {
                    var sel = ViewModel.SelectedItem;
                    ViewModel.SelectedItem = null;
                    ViewModel.SelectedItem = sel;
                }
            };

            editor.Show();
        }

        protected override void OnClosed(EventArgs e)
        {
            ViewModel?.Dispose();
            base.OnClosed(e);
        }
    }
}
