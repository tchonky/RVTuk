using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using RVTuk.Core.Util;

namespace RVTuk.UI.Views
{
    public partial class ImageCropWindow : Window
    {
        private readonly BitmapSource _source;
        private readonly double _dispW;
        private readonly double _dispH;
        private double _cropX, _cropY, _cropW, _cropH;

        public BitmapSource? Result { get; private set; }

        public ImageCropWindow(BitmapSource source)
        {
            InitializeComponent();
            _source = source;

            // Fit the preview into a working box, preserving aspect ratio.
            const double max = 520;
            double aspect = (double)source.PixelHeight / source.PixelWidth;
            _dispW = Math.Min(max, source.PixelWidth);
            _dispH = _dispW * aspect;
            if (_dispH > max) { _dispH = max; _dispW = _dispH / aspect; }

            Preview.Source = source;
            Preview.Width  = _dispW;
            Preview.Height = _dispH;
            CropCanvas.Width  = _dispW;
            CropCanvas.Height = _dispH;

            _cropX = _dispW * 0.1; _cropY = _dispH * 0.1;
            _cropW = _dispW * 0.8; _cropH = _dispH * 0.8;
            Loaded += (s, e) => LayoutCrop();
        }

        private void CropNW_DragDelta(object s, DragDeltaEventArgs e)
        { _cropX += e.HorizontalChange; _cropY += e.VerticalChange; _cropW -= e.HorizontalChange; _cropH -= e.VerticalChange; Clamp(); LayoutCrop(); }
        private void CropNE_DragDelta(object s, DragDeltaEventArgs e)
        { _cropY += e.VerticalChange; _cropW += e.HorizontalChange; _cropH -= e.VerticalChange; Clamp(); LayoutCrop(); }
        private void CropSW_DragDelta(object s, DragDeltaEventArgs e)
        { _cropX += e.HorizontalChange; _cropW -= e.HorizontalChange; _cropH += e.VerticalChange; Clamp(); LayoutCrop(); }
        private void CropSE_DragDelta(object s, DragDeltaEventArgs e)
        { _cropW += e.HorizontalChange; _cropH += e.VerticalChange; Clamp(); LayoutCrop(); }
        private void CropBody_DragDelta(object s, DragDeltaEventArgs e)
        { _cropX += e.HorizontalChange; _cropY += e.VerticalChange; Clamp(); LayoutCrop(); }

        private void Clamp()
        {
            _cropW = Math.Max(20, Math.Min(_cropW, _dispW));
            _cropH = Math.Max(20, Math.Min(_cropH, _dispH));
            _cropX = Math.Max(0, Math.Min(_cropX, _dispW - _cropW));
            _cropY = Math.Max(0, Math.Min(_cropY, _dispH - _cropH));
        }

        private void LayoutCrop()
        {
            Place(CropRect, _cropX, _cropY, _cropW, _cropH);
            Place(CropBody, _cropX, _cropY, _cropW, _cropH);
            Place(MaskTop,    0,               0,               _dispW,                     _cropY);
            Place(MaskBottom, 0,               _cropY + _cropH, _dispW,                     _dispH - (_cropY + _cropH));
            Place(MaskLeft,   0,               _cropY,          _cropX,                     _cropH);
            Place(MaskRight,  _cropX + _cropW, _cropY,          _dispW - (_cropX + _cropW), _cropH);
            Place(CropNW, _cropX - 6,          _cropY - 6);
            Place(CropNE, _cropX + _cropW - 6, _cropY - 6);
            Place(CropSW, _cropX - 6,          _cropY + _cropH - 6);
            Place(CropSE, _cropX + _cropW - 6, _cropY + _cropH - 6);
        }

        private static void Place(FrameworkElement el, double left, double top,
                                  double w = double.NaN, double h = double.NaN)
        {
            Canvas.SetLeft(el, left);
            Canvas.SetTop(el, top);
            if (!double.IsNaN(w)) el.Width  = Math.Max(0, w);
            if (!double.IsNaN(h)) el.Height = Math.Max(0, h);
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            var r = ImageCropGeometry.ToPixelRect(
                _source.PixelWidth, _source.PixelHeight, _dispW, _dispH,
                _cropX, _cropY, _cropW, _cropH);
            try
            {
                Result = new CroppedBitmap(_source, new Int32Rect(r.X, r.Y, r.Width, r.Height));
                DialogResult = true;
            }
            catch { DialogResult = false; }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
