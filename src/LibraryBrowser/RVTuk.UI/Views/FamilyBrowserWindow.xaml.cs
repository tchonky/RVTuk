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
        private readonly Action _deepScan;
        private readonly Func<long, string, bool> _rescanFamily;

        public FamilyBrowserViewModel ViewModel { get; private set; } = null!;

        public FamilyBrowserWindow(
            AppConfig config,
            Func<IReadOnlyList<string>> getProjectFamilies,
            Func<string, (bool Success, string? Error)> loadFamily,
            Action deepScan,
            Func<long, string, bool> rescanFamily)
        {
            InitializeComponent();
            _getProjectFamilies = getProjectFamilies;
            _loadFamily = loadFamily;
            _deepScan = deepScan;
            _rescanFamily = rescanFamily;
            LoadWithConfig(config);
        }

        private void LoadWithConfig(AppConfig config)
        {
            _config = config;
            var setupDir = Path.Combine(config.LibraryFolderPath, ".Setup");
            Directory.CreateDirectory(setupDir);
            var repo = new BrowserRepository(config.DatabasePath);
            var vm = new FamilyBrowserViewModel(config, repo, _getProjectFamilies, _loadFamily, _deepScan, _rescanFamily);
            vm.EditInfoRequested += OnEditInfoRequested;

            if (ViewModel != null)
            {
                ViewModel.EditInfoRequested -= OnEditInfoRequested;
                ViewModel.Dispose();
            }

            ViewModel = vm;
            DataContext = ViewModel;
        }

        private void BrowseLibraryFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select the root folder containing your Revit families",
                SelectedPath = ViewModel.LibraryFolderPath
            };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            var error = LibraryFolderValidator.Validate(dialog.SelectedPath);
            if (error != null)
            {
                MessageBox.Show(error, "RVTuk - Family Browser",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var config = ConfigManager.LoadConfig();
            config.LibraryFolderPath = dialog.SelectedPath;
            Directory.CreateDirectory(Path.Combine(dialog.SelectedPath, ".Setup"));
            ConfigManager.SaveConfig(config);
            LoadWithConfig(config);
            ViewModel.IsShowingSettings = true;
        }

        private void OnEditInfoRequested(FamilyBrowserItemViewModel item)
        {
            var fullPath = Path.Combine(_config.LibraryFolderPath, item.RelativePath);
            var editor = new InstructionsEditorWindow(
                item, ViewModel.InstructionsXaml, fullPath, ViewModel.Repo);
            editor.Owner = this;
            editor.ShowDialog();
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
