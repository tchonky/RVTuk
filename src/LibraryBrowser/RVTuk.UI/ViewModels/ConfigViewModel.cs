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
    /// settings (library root, scan, ignored subfolders) that previously lived in an
    /// inline panel inside the Family Browser. Built as a hub so other tools' settings can be
    /// added as further tabs later.
    /// </summary>
    public class ConfigViewModel : ViewModelBase
    {
        private readonly AppConfig _config;
        private readonly Action<bool, bool> _scan;
        private readonly Action? _onLibraryFolderChanged;

        private string? _ignoredSubfoldersText;
        private bool _scanThumbnails;
        private bool _scanParameters;

        /// <param name="scan">
        /// Runs a scan. Args are (includeThumbnails, includeParameters); both false means a
        /// filenames-only sync (add new families, prune deleted ones, no extraction).
        /// </param>
        /// <param name="onLibraryFolderChanged">
        /// Invoked after the library folder is changed + saved, so the host can refresh an open
        /// Family Browser. Optional — kept as a delegate so this UI project takes no Revit/window
        /// dependency.
        /// </param>
        public ConfigViewModel(
            AppConfig config,
            Action<bool, bool> scan,
            Action? onLibraryFolderChanged = null)
        {
            _config = config;
            _scan = scan;
            _onLibraryFolderChanged = onLibraryFolderChanged;

            BrowseLibraryCommand = new RelayCommand(BrowseLibraryFolder);
            ScanCommand = new RelayCommand(() => _scan(ScanThumbnails, ScanParameters), () => IsConfigured);
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

        public bool ScanThumbnails
        {
            get => _scanThumbnails;
            set => SetProperty(ref _scanThumbnails, value);
        }

        public bool ScanParameters
        {
            get => _scanParameters;
            set => SetProperty(ref _scanParameters, value);
        }

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
        public ICommand ScanCommand { get; }

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
            CommandManager.InvalidateRequerySuggested(); // re-enable the Scan button now a folder is set
            _onLibraryFolderChanged?.Invoke();
        }
    }
}
