using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using RVTuk.Core.Config;

namespace RVTuk.UI.ViewModels
{
    /// <summary>
    /// View model for the ribbon-launched Config hub. Today it hosts the Family Library
    /// settings (library root, deep scan, ignored subfolders) that previously lived in an
    /// inline panel inside the Family Browser. Built as a hub so other tools' settings can be
    /// added as further tabs later.
    /// </summary>
    public class ConfigViewModel : ViewModelBase
    {
        private readonly AppConfig _config;
        private readonly Action _scanNewAndChanged;
        private readonly Action _rescanAll;
        private readonly Action? _onLibraryFolderChanged;

        private string? _ignoredSubfoldersText;

        /// <param name="scanNewAndChanged">Incremental deep scan (new/modified families only).</param>
        /// <param name="rescanAll">Forced re-extraction of every family (non-destructive).</param>
        /// <param name="onLibraryFolderChanged">
        /// Invoked after the library folder is changed + saved, so the host can refresh an open
        /// Family Browser. Optional — kept as a delegate so this UI project takes no Revit/window
        /// dependency.
        /// </param>
        public ConfigViewModel(
            AppConfig config,
            Action scanNewAndChanged,
            Action rescanAll,
            Action? onLibraryFolderChanged = null)
        {
            _config = config;
            _scanNewAndChanged = scanNewAndChanged;
            _rescanAll = rescanAll;
            _onLibraryFolderChanged = onLibraryFolderChanged;

            BrowseLibraryCommand = new RelayCommand(BrowseLibraryFolder);
            ScanNewCommand       = new RelayCommand(() => _scanNewAndChanged(), () => IsConfigured);
            RescanAllCommand     = new RelayCommand(ConfirmAndRescanAll, () => IsConfigured);
        }

        public string LibraryFolderPath
        {
            get => _config.LibraryFolderPath;
            private set
            {
                if (_config.LibraryFolderPath == value) return;
                _config.LibraryFolderPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DerivedDatabasePath));
            }
        }

        public string DerivedDatabasePath =>
            string.IsNullOrWhiteSpace(_config.LibraryFolderPath)
                ? string.Empty
                : _config.DatabasePath;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_config.LibraryFolderPath);

        public string IgnoredSubfoldersText
        {
            get => _ignoredSubfoldersText ?? string.Join(Environment.NewLine, _config.IgnoredSubfolders);
            set
            {
                if (Equals(_ignoredSubfoldersText, value)) return;
                _ignoredSubfoldersText = value;
                _config.IgnoredSubfolders = (value ?? string.Empty)
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
                ConfigManager.SaveConfig(_config);
                OnPropertyChanged();
            }
        }

        public ICommand BrowseLibraryCommand { get; }
        public ICommand ScanNewCommand { get; }
        public ICommand RescanAllCommand { get; }

        private void BrowseLibraryFolder()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the root folder containing your Revit families",
                SelectedPath = Directory.Exists(LibraryFolderPath) ? LibraryFolderPath : string.Empty
            };
            if (dialog.ShowDialog() != DialogResult.OK) return;

            var error = LibraryFolderValidator.Validate(dialog.SelectedPath);
            if (error != null)
            {
                System.Windows.MessageBox.Show(error, "RVTuk – Config",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LibraryFolderPath = dialog.SelectedPath;
            Directory.CreateDirectory(Path.Combine(dialog.SelectedPath, ".Setup"));
            ConfigManager.SaveConfig(_config);
            CommandManager.InvalidateRequerySuggested(); // re-enable scan buttons now a folder is set
            _onLibraryFolderChanged?.Invoke();
        }

        private void ConfirmAndRescanAll()
        {
            var result = System.Windows.MessageBox.Show(
                "Re-scan all families?\n\n" +
                "This re-reads every family in the library to refresh parameters and thumbnails, " +
                "and removes families whose files no longer exist. It can take a long time for a " +
                "large library.\n\n" +
                "Your instructions, pictures, custom thumbnails, tags, and favourites are kept.\n\n" +
                "Continue?",
                "RVTuk – Re-scan All Families",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
                _rescanAll();
        }
    }
}
