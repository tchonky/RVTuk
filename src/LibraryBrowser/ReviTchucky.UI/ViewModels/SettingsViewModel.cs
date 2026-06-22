using System;
using System.IO;
using System.Windows.Forms;
using System.Windows.Input;
using ReviTchucky.Core.Config;

namespace ReviTchucky.UI.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private string _libraryFolderPath = string.Empty;
        private string _validationMessage = string.Empty;

        public string LibraryFolderPath
        {
            get => _libraryFolderPath;
            set
            {
                SetProperty(ref _libraryFolderPath, value);
                OnPropertyChanged(nameof(DerivedDatabasePath));
            }
        }

        public string DerivedDatabasePath =>
            string.IsNullOrWhiteSpace(_libraryFolderPath)
                ? string.Empty
                : Path.Combine(_libraryFolderPath, ".Setup", "ReviTchucky.db");

        public string ValidationMessage
        {
            get => _validationMessage;
            set => SetProperty(ref _validationMessage, value);
        }

        public ICommand BrowseLibraryCommand { get; }
        public ICommand SaveCommand { get; }

        public event Action? RequestClose;
        public bool Saved { get; private set; }

        public SettingsViewModel()
        {
            var config = ConfigManager.LoadConfig();
            LibraryFolderPath = config.LibraryFolderPath;

            BrowseLibraryCommand = new RelayCommand(BrowseLibraryFolder);
            SaveCommand = new RelayCommand(Save);
        }

        private void BrowseLibraryFolder()
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Select Family Library Folder",
                SelectedPath = Directory.Exists(LibraryFolderPath) ? LibraryFolderPath : string.Empty
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                LibraryFolderPath = dlg.SelectedPath;
        }

        private void Save()
        {
            ValidationMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(LibraryFolderPath))
            {
                ValidationMessage = "Library folder path is required.";
                return;
            }
            if (!Directory.Exists(LibraryFolderPath))
            {
                ValidationMessage = "Library folder does not exist.";
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.Combine(LibraryFolderPath, ".Setup"));
            }
            catch (Exception ex)
            {
                ValidationMessage = $"Cannot create .Setup folder: {ex.Message}";
                return;
            }

            ConfigManager.SaveConfig(new AppConfig { LibraryFolderPath = LibraryFolderPath });

            Saved = true;
            RequestClose?.Invoke();
        }
    }
}
