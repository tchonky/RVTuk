using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using RVTuk.Core.Shared.Config;
using RVTuk.Core.FamilyBrowser.Config;

using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.FamilyBrowser.ViewModels
{
    /// <summary>
    /// View model for the Family Library settings (library root, scan, ignored subfolders and
    /// ignored file patterns). Embedded directly in <see cref="FamilyBrowserViewModel.Settings"/>
    /// and rendered in the Family Browser's right panel behind the gear button — there is no
    /// separate ribbon Config window (removed; this was the only tab it ever grew).
    /// </summary>
    public class ConfigViewModel : ViewModelBase
    {
        private readonly AppConfig _config;
        private readonly Action<bool, bool> _scan;
        private readonly Action? _onLibraryFolderChanged;

        private string? _ignoredSubfoldersText;
        private string? _ignoredFilePatternsText;
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
            get => _ignoredSubfoldersText ?? string.Join(Environment.NewLine,
                       _config.IgnoredSubfolders ?? new System.Collections.Generic.List<string>());
            set
            {
                if (Equals(_ignoredSubfoldersText, value)) return;
                _ignoredSubfoldersText = value;
                var folders = (value ?? string.Empty)
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
                Persist(c => c.IgnoredSubfolders = folders);
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// One regex per line, matched against family file names (see
        /// <see cref="RVTuk.Core.FamilyBrowser.Util.IgnoredFileMatcher"/>). Mirrors
        /// <see cref="IgnoredSubfoldersText"/>: parsed and saved on every change.
        /// </summary>
        public string IgnoredFilePatternsText
        {
            get => _ignoredFilePatternsText ?? string.Join(Environment.NewLine,
                       _config.IgnoredFilePatterns ?? new System.Collections.Generic.List<string>());
            set
            {
                if (Equals(_ignoredFilePatternsText, value)) return;
                _ignoredFilePatternsText = value;
                var patterns = (value ?? string.Empty)
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
                Persist(c => c.IgnoredFilePatterns = patterns);
                OnPropertyChanged();
            }
        }

        // Writes one change through to config.json, re-reading it first: the browser stays open
        // for hours, and other tools (Area Calc, DWG Export, Topo Tools) save to the same file.
        private void Persist(Action<AppConfig> apply)
        {
            apply(_config);
            var saved = ConfigManager.LoadConfig();
            apply(saved);
            ConfigManager.SaveConfig(saved);
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

            var folder = dialog.SelectedPath;
            LibraryFolderPath = folder;
            Persist(c => c.LibraryFolderPath = folder);
            CommandManager.InvalidateRequerySuggested(); // re-enable the Scan button now a folder is set
            // The host reloads the browser against the new folder (creating its .DB folder).
            _onLibraryFolderChanged?.Invoke();
        }
    }
}
