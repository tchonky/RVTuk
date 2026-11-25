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
            ScanCommand = new RelayCommand(() => _scan(ScanThumbnails, ScanParameters), () => IsConfigured && !IsReadOnlyLibrary);
            InstallShortcutsCommand = new RelayCommand(InstallShortcuts, () => IsConfigured);
            PublishShortcutsCommand = new RelayCommand(PublishShortcuts, () => IsConfigured && !IsReadOnlyLibrary);
        }

        public ICommand InstallShortcutsCommand { get; }
        public ICommand PublishShortcutsCommand { get; }

        /// <summary>Answers this panel's message boxes instead of showing them (tests).</summary>
        public Func<string, MessageBoxButton, MessageBoxResult>? AnswerMessage { get; set; }

        /// <summary><c>%APPDATA%\Autodesk\Revit</c>; a test points it at a temp folder.</summary>
        public string UserRevitRoot { get; set; } = OfficeShortcuts.UserRevitRoot();

        private MessageBoxResult Ask(string text, MessageBoxButton buttons, MessageBoxImage icon) =>
            AnswerMessage?.Invoke(text, buttons)
            ?? System.Windows.MessageBox.Show(text, "RVTuk — Keyboard shortcuts", buttons, icon);

        // The office keys win: the office file replaces each Revit year's user file, whose old
        // version is kept next to it.
        private void InstallShortcuts()
        {
            var office = OfficeShortcuts.OfficeFile(_config.LibraryFolderPath);
            var problem = OfficeShortcuts.Validate(office);
            if (problem != null)
            {
                Ask("There are no office keyboard shortcuts to install yet.\n\n" + problem +
                    "\n\nThe library maintainer publishes them from this panel.", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var years = OfficeShortcuts.UserYears(UserRevitRoot);
            if (years.Count == 0)
            {
                Ask("No Revit settings folder was found for your Windows user.", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var list = string.Join(", ", years.Select(y => y.Year));
            if (Ask($"Replace your keyboard shortcuts in Revit {list} with the office's?\n\n" +
                    "Your current shortcuts are kept as a backup file next to them. " +
                    "The new shortcuts work after you restart Revit.",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            try
            {
                var done = OfficeShortcuts.Install(office, UserRevitRoot, DateTime.Now);
                Ask($"Installed the office keyboard shortcuts for Revit {string.Join(", ", done)}.\n\nRestart Revit to use them.",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Ask("Could not install the keyboard shortcuts:\n\n" + ex.Message, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // The maintainer's own shortcuts (set in Revit's Keyboard Shortcuts dialog) become the office's.
        private void PublishShortcuts()
        {
            var newest = OfficeShortcuts.NewestUserFile(UserRevitRoot);
            if (newest == null)
            {
                Ask("You have no keyboard shortcuts of your own yet. Set them in Revit (View › User Interface › " +
                    "Keyboard Shortcuts), click OK, then publish them here.", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var office = OfficeShortcuts.OfficeFile(_config.LibraryFolderPath);
            if (Ask($"Publish your Revit {newest.Value.Year} keyboard shortcuts as the office's?\n\n{newest.Value.Path}\n\n" +
                    "Everyone gets them when they press Install office shortcuts.",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            try
            {
                OfficeShortcuts.Publish(newest.Value.Path, office);
                Ask("Published to:\n" + office, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Ask("Could not publish the keyboard shortcuts:\n\n" + ex.Message, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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

        /// <summary>True when this user has view-only access to the library: Scan is off.</summary>
        public bool IsReadOnlyLibrary { get; set; }

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
