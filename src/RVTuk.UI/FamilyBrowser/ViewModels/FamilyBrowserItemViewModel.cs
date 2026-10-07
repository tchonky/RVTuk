// RVTuk.UI/ViewModels/FamilyBrowserItemViewModel.cs
using System.Windows.Media.Imaging;
using RVTuk.Core.FamilyBrowser.Models;
using RVTuk.Core.FamilyBrowser.Util;

using RVTuk.UI.Shared.ViewModels;

namespace RVTuk.UI.FamilyBrowser.ViewModels
{
    public class FamilyBrowserItemViewModel : ViewModelBase
    {
        private VersionStatus _versionStatus;

        public FamilyBrowserItem Model { get; }

        public long Id => Model.Id;
        public string FileName => Model.FileName;
        // Never System.IO.Path here: model-only rows carry in-project family names, which
        // may contain characters that are illegal in paths (") — net48 Path APIs throw on them.
        public string DisplayName => FamilyFileName.WithoutRfaExtension(Model.FileName);
        public string? Category => Model.Category;
        public string RelativePath => Model.RelativePath;
        public string? Tags => Model.Tags;

        // Value of the _Version shared parameter — from the library index for indexed rows,
        // from the loaded family (symbols, else a placed instance) for model-only rows.
        // Shown next to the row flags.
        public string? Version => Model.Version;
        public bool HasVersion => !string.IsNullOrWhiteSpace(Model.Version);

        // True when _Version is an instance parameter — in the library .rfa (deep scan) or in
        // the loaded copy (sync's instance fallback). Off-standard: the version renders red so
        // the user knows the family needs its parameter changed to a type parameter.
        public bool VersionIsInstance
        {
            get => Model.VersionIsInstance;
            set
            {
                if (Model.VersionIsInstance == value) return;
                Model.VersionIsInstance = value;
                OnPropertyChanged();
            }
        }

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
                OnPropertyChanged(nameof(IsModelOnly));
            }
        }

        public bool ShowUpToDate => _versionStatus == VersionStatus.UpToDate;
        public bool ShowUpdateAvailable => _versionStatus == VersionStatus.UpdateAvailable;

        // A synthetic row for a family loaded in the project but absent from the library.
        // It has no .rfa, no DB row (Id 0), no thumbnail — every library-backed action
        // (load, favourite, edit info, rescan, open in editor) must be disabled for it.
        public bool IsModelOnly => _versionStatus == VersionStatus.ModelOnly;

        // Decoded on first bind, not when the list loads: the row list is virtualised, so most
        // rows are never drawn. A bad PNG decodes to null once rather than on every bind.
        private BitmapSource? _thumbnail;
        private bool _thumbnailDecoded;
        public BitmapSource? Thumbnail
        {
            get
            {
                if (!_thumbnailDecoded)
                {
                    _thumbnailDecoded = true;
                    _thumbnail = Model.ThumbnailPng != null ? LoadBitmap(Model.ThumbnailPng) : null;
                }
                return _thumbnail;
            }
        }

        // The copy loaded in the project, as of the last Sync — kept so a Rescan can re-judge.
        private ProjectFamilyInfo? _inProject;

        public FamilyBrowserItemViewModel(FamilyBrowserItem model)
        {
            Model = model;
            _versionStatus = model.VersionStatus;
        }

        /// <summary>Judges this library row against the copy loaded in the project (Sync).</summary>
        public void CompareWithProject(ProjectFamilyInfo loaded)
        {
            _inProject = loaded;
            VersionStatus = FamilyVersionCheck.IsUpdateAvailable(Model.Version, loaded.Version)
                ? VersionStatus.UpdateAvailable : VersionStatus.UpToDate;
            // Red flag if *either* copy still carries _Version at instance level — the library
            // flag comes from the index (deep scan), the loaded copy's from Sync's fallback.
            if (loaded.VersionIsInstance) VersionIsInstance = true;
        }

        /// <summary>Takes the re-read DB row after a single-family Rescan.</summary>
        public void Refresh(FamilyBrowserItem fresh)
        {
            Model.Category = fresh.Category;
            Model.Version = fresh.Version;
            VersionIsInstance = fresh.VersionIsInstance;
            OnPropertyChanged(nameof(Category));
            OnPropertyChanged(nameof(Version));
            OnPropertyChanged(nameof(HasVersion));
            UpdateThumbnail(fresh.ThumbnailPng);
            if (_inProject != null) CompareWithProject(_inProject);
        }

        /// <summary>
        /// Replaces the preview image (e.g. after a single-family rescan re-extracts it).
        /// Raises PropertyChanged so both the list row and the detail pane update live.
        /// </summary>
        public void UpdateThumbnail(byte[]? pngData)
        {
            Model.ThumbnailPng = pngData;
            _thumbnailDecoded = false;
            OnPropertyChanged(nameof(Thumbnail));
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
