using System;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ReviTchucky.Core.Database;
using ReviTchucky.Core.Extraction;
using System.Collections.ObjectModel;

namespace ReviTchucky.UI.ViewModels
{
    public class InstructionsEditorViewModel : ViewModelBase
    {
        private readonly BrowserRepository _repo;
        private readonly long _familyId;
        private readonly string _rfaFullPath;

        private string? _instructionsXaml;
        private byte[]? _customThumbPng;
        private bool _oleSynced;
        private BitmapSource? _thumbnailSource;
        private string _thumbStatus = string.Empty;

        public string FamilyDisplayName { get; }

        public string? InstructionsXaml
        {
            get => _instructionsXaml;
            set => SetProperty(ref _instructionsXaml, value);
        }

        public BitmapSource? ThumbnailSource
        {
            get => _thumbnailSource;
            private set => SetProperty(ref _thumbnailSource, value);
        }

        public string ThumbStatus
        {
            get => _thumbStatus;
            private set => SetProperty(ref _thumbStatus, value);
        }

        public bool OleSynced
        {
            get => _oleSynced;
            private set { SetProperty(ref _oleSynced, value); OnPropertyChanged(nameof(CanUpdateOle)); }
        }

        public bool CanUpdateOle => _customThumbPng != null && !_oleSynced;

        public ObservableCollection<GalleryItemViewModel> GalleryItems { get; } = new();
        public ICommand AddImageCommand { get; }
        public ICommand DeleteImageCommand { get; }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ReplaceThumbnailCommand { get; }
        public ICommand ResetThumbnailCommand { get; }
        public ICommand UpdateOleCommand { get; }

        public event Action? CloseRequested;

        public InstructionsEditorViewModel(
            long familyId,
            string familyFileName,
            string rfaFullPath,
            string? currentXaml,
            BrowserRepository repo)
        {
            _repo = repo;
            _familyId = familyId;
            _rfaFullPath = rfaFullPath;
            FamilyDisplayName = Path.GetFileNameWithoutExtension(familyFileName);
            _instructionsXaml = currentXaml;

            SaveCommand             = new RelayCommand(() => { }); // actual save goes through ExecuteSave
            CancelCommand           = new RelayCommand(() => CloseRequested?.Invoke());
            ReplaceThumbnailCommand = new RelayCommand(ReplaceThumbnail);
            ResetThumbnailCommand   = new RelayCommand(ResetThumbnail);
            UpdateOleCommand        = new RelayCommand(UpdateOle, () => CanUpdateOle);
            AddImageCommand    = new RelayCommand(AddImage);
            DeleteImageCommand = new RelayCommand<long>(DeleteImage);
            ReloadGallery();

            LoadThumbnailState();
        }

        private void LoadThumbnailState()
        {
            var (customPng, oleSynced) = _repo.GetCustomThumbnail(_familyId);
            if (customPng != null)
            {
                _customThumbPng = customPng;
                _oleSynced = oleSynced;
                ThumbnailSource = ToBitmapSource(customPng, 80);
                ThumbStatus = oleSynced ? "● Custom thumbnail" : "● Custom thumbnail · .rfa out of sync";
            }
            else
            {
                var olePng = _repo.GetOleThumbnail(_familyId);
                ThumbnailSource = olePng != null ? ToBitmapSource(olePng, 80) : null;
                ThumbStatus = "● System thumbnail";
                _oleSynced = true;
            }
        }

        public void SetThumbnailFromBytes(byte[] pngData)
        {
            _customThumbPng = pngData;
            _oleSynced = false;
            ThumbnailSource = ToBitmapSource(pngData, 80);
            ThumbStatus = "● Custom thumbnail · .rfa out of sync";
            OnPropertyChanged(nameof(CanUpdateOle));
        }

        private void ReplaceThumbnail()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp",
                Title  = "Replace Thumbnail"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var png = ConvertToPng(File.ReadAllBytes(dlg.FileName));
                SetThumbnailFromBytes(png);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not load image: {ex.Message}");
            }
        }

        private void ResetThumbnail()
        {
            _customThumbPng = null;
            _oleSynced = true;
            var olePng = _repo.GetOleThumbnail(_familyId);
            ThumbnailSource = olePng != null ? ToBitmapSource(olePng, 80) : null;
            ThumbStatus = "● System thumbnail";
            OnPropertyChanged(nameof(CanUpdateOle));
        }

        private void UpdateOle()
        {
            if (_customThumbPng == null) return;
            bool ok = ThumbnailWriter.WriteThumbnailToRfa(_rfaFullPath, _customThumbPng);
            if (ok)
            {
                _oleSynced = true;
                ThumbStatus = "● Custom thumbnail";
                _repo.SetOleSynced(_familyId, true);
                OnPropertyChanged(nameof(CanUpdateOle));
            }
            else
            {
                System.Windows.MessageBox.Show(
                    "Could not write to the .rfa file. The file may be read-only or locked.",
                    "ReviTchucky");
            }
        }

        public void ExecuteSave(string? xamlFromEditor)
        {
            _repo.SaveInstructionsXaml(_familyId, xamlFromEditor);

            if (_customThumbPng != null)
            {
                bool oleOk = ThumbnailWriter.WriteThumbnailToRfa(_rfaFullPath, _customThumbPng);
                _repo.SaveCustomThumbnail(_familyId, _customThumbPng, oleOk);
            }
            else
            {
                // Reset was clicked — delete custom thumbnail from DB
                _repo.DeleteCustomThumbnail(_familyId);
            }

            CloseRequested?.Invoke();
        }

        private void ReloadGallery()
        {
            GalleryItems.Clear();
            foreach (var im in _repo.GetImages(_familyId))
                GalleryItems.Add(new GalleryItemViewModel(im.Id, im.Caption, _repo.GetGalleryPath(_familyId, im.FileName)));
        }

        private void AddImage()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp",
                Title  = "Add gallery image",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                foreach (var file in dlg.FileNames)
                {
                    var png = ConvertToPng(File.ReadAllBytes(file));
                    _repo.AddImage(_familyId, png, Path.GetFileNameWithoutExtension(file));
                }
                ReloadGallery();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not add image: {ex.Message}");
            }
        }

        private void DeleteImage(long imageId)
        {
            _repo.DeleteImage(imageId);
            ReloadGallery();
        }

        public void SaveCaption(long imageId, string? caption) => _repo.UpdateCaption(imageId, caption);

        private static byte[] ConvertToPng(byte[] rawBytes)
        {
            using var ms = new MemoryStream(rawBytes);
            using var bmp = System.Drawing.Image.FromStream(ms);
            using var pngMs = new MemoryStream();
            bmp.Save(pngMs, System.Drawing.Imaging.ImageFormat.Png);
            return pngMs.ToArray();
        }

        private static BitmapSource? ToBitmapSource(byte[] pngData, int decodeWidth)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource     = new MemoryStream(pngData);
                bmp.CacheOption      = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = decodeWidth;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
    }
}
