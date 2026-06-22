using System.IO;
using System.Windows.Media.Imaging;

namespace ReviTchucky.UI.ViewModels
{
    public class GalleryItemViewModel
    {
        public long Id { get; }
        public string? Caption { get; set; }
        public BitmapSource? Image { get; }

        public GalleryItemViewModel(long id, string? caption, string absolutePath)
        {
            Id = id;
            Caption = caption;
            Image = LoadFile(absolutePath);
        }

        private static BitmapSource? LoadFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null; // missing file → placeholder (null) handled by UI
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new System.Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 320;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }
    }
}
