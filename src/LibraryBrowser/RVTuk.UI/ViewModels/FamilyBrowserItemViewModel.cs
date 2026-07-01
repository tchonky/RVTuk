// RVTuk.UI/ViewModels/FamilyBrowserItemViewModel.cs
using System.IO;
using System.Windows.Media.Imaging;
using RVTuk.Core.Models;

namespace RVTuk.UI.ViewModels
{
    public class FamilyBrowserItemViewModel : ViewModelBase
    {
        private VersionStatus _versionStatus;

        public FamilyBrowserItem Model { get; }

        public long Id => Model.Id;
        public string FileName => Model.FileName;
        public string DisplayName => Path.GetFileNameWithoutExtension(Model.FileName);
        public string? Category => Model.Category;
        public string RelativePath => Model.RelativePath;
        public int RevitYear => Model.RevitYear;
        public string? Tags => Model.Tags;

        public bool IsFavorite
        {
            get => Model.IsFavorite;
            set
            {
                if (Model.IsFavorite == value) return;
                Model.IsFavorite = value;
                OnPropertyChanged();
            }
        }

        public VersionStatus VersionStatus
        {
            get => _versionStatus;
            set
            {
                SetProperty(ref _versionStatus, value);
                OnPropertyChanged(nameof(ShowUpToDate));
                OnPropertyChanged(nameof(ShowUpdateAvailable));
            }
        }

        public bool ShowUpToDate => _versionStatus == VersionStatus.UpToDate;
        public bool ShowUpdateAvailable => _versionStatus == VersionStatus.UpdateAvailable;

        private BitmapSource? _thumbnail;
        public BitmapSource? Thumbnail
        {
            get => _thumbnail;
            private set => SetProperty(ref _thumbnail, value);
        }

        public FamilyBrowserItemViewModel(FamilyBrowserItem model)
        {
            Model = model;
            _versionStatus = model.VersionStatus;
            _thumbnail = model.ThumbnailPng != null ? LoadBitmap(model.ThumbnailPng) : null;
        }

        /// <summary>
        /// Replaces the preview image (e.g. after a single-family rescan re-extracts it).
        /// Raises PropertyChanged so both the list row and the detail pane update live.
        /// </summary>
        public void UpdateThumbnail(byte[]? pngData)
        {
            Model.ThumbnailPng = pngData;
            Thumbnail = pngData != null ? LoadBitmap(pngData) : null;
        }

        private static BitmapSource? LoadBitmap(byte[] pngData)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new System.IO.MemoryStream(pngData);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelHeight = 128; // crisp enough for the ~118px square detail frame
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
    }
}
