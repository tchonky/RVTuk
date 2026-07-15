using System;

namespace RVTuk.Core.FamilyBrowser.Util
{
    public readonly record struct PixelRect(int X, int Y, int Width, int Height);

    // Maps a crop rectangle expressed in on-screen display coordinates back to integer
    // source-image pixel coordinates, clamped so the result always stays inside the image.
    public static class ImageCropGeometry
    {
        public static PixelRect ToPixelRect(
            int sourcePixelWidth, int sourcePixelHeight,
            double displayWidth, double displayHeight,
            double cropX, double cropY, double cropWidth, double cropHeight)
        {
            if (sourcePixelWidth <= 0 || sourcePixelHeight <= 0)
                return new PixelRect(0, 0, Math.Max(0, sourcePixelWidth), Math.Max(0, sourcePixelHeight));
            if (displayWidth <= 0 || displayHeight <= 0)
                return new PixelRect(0, 0, sourcePixelWidth, sourcePixelHeight);

            double sx = sourcePixelWidth / displayWidth;
            double sy = sourcePixelHeight / displayHeight;

            int x = (int)Math.Round(cropX * sx);
            int y = (int)Math.Round(cropY * sy);
            int w = (int)Math.Round(cropWidth * sx);
            int h = (int)Math.Round(cropHeight * sy);

            x = Clamp(x, 0, sourcePixelWidth - 1);
            y = Clamp(y, 0, sourcePixelHeight - 1);
            w = Clamp(w, 1, sourcePixelWidth - x);
            h = Clamp(h, 1, sourcePixelHeight - y);
            return new PixelRect(x, y, w, h);
        }

        private static int Clamp(int v, int lo, int hi)
        {
            if (hi < lo) hi = lo;
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
