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

        public FamilyBrowserViewModel ViewModel { get; private set; } = null!;

        public FamilyBrowserWindow(
            AppConfig config,
            Func<IReadOnlyList<string>> getProjectFamilies,
            Func<string, (bool Success, string? Error)> loadFamily,
            Func<long, string, bool> rescanFamily)
        {
            InitializeComponent();
            _getProjectFamilies = getProjectFamilies;
            _loadFamily = loadFamily;
            _rescanFamily = rescanFamily;
            LoadWithConfig(config);
        }

        /// <summary>
        /// Re-loads the browser against the current saved config. Called by the Config window
        /// after the library folder changes, so an open browser doesn't show a stale library.
        /// </summary>
        public void ReloadConfig() => LoadWithConfig(ConfigManager.LoadConfig());

        private void LoadWithConfig(AppConfig config)
        {
            _config = config;
            var setupDir = Path.Combine(config.LibraryFolderPath, ".Setup");
            Directory.CreateDirectory(setupDir);
            var repo = new BrowserRepository(config.DatabasePath);
            var vm = new FamilyBrowserViewModel(config, repo, _getProjectFamilies, _loadFamily, _rescanFamily);
            vm.EditInfoRequested += OnEditInfoRequested;

            if (ViewModel != null)
            {
                ViewModel.EditInfoRequested -= OnEditInfoRequested;
                ViewModel.Dispose();
            }

            ViewModel = vm;
            DataContext = ViewModel;
        }

        private void OnEditInfoRequested(FamilyBrowserItemViewModel item)
        {
            var fullPath = Path.Combine(_config.LibraryFolderPath, item.RelativePath);
            var editor = new InstructionsEditorWindow(
                item, ViewModel.InstructionsXaml, fullPath, ViewModel.Repo);
            editor.Owner = this;
            editor.ShowDialog();
            item.UpdateThumbnail(ViewModel.Repo.GetResolvedThumbnail(item.Id));
            var sel = ViewModel.SelectedItem;
            ViewModel.SelectedItem = null;
            ViewModel.SelectedItem = sel;
        }

        protected override void OnClosed(EventArgs e)
        {
            ViewModel?.Dispose();
            base.OnClosed(e);
        }
    }
}
