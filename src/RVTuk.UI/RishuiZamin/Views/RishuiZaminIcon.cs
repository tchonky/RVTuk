using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RVTuk.UI.RishuiZamin.Views
{
    /// <summary>
    /// The Rishui Zamin ribbon button's icon, shared by RVTuk and KKarea: a simplified state
    /// emblem (blue shield, white menorah, olive branches; no lettering) on the dark tile the
    /// other RVTuk buttons use. At 16 px the branches are left out, as they would only blur.
    /// </summary>
    public static class RishuiZaminIcon
    {
        public static readonly Color ShieldBlue = Color.FromRgb(0x00, 0x72, 0xBC);
        private static readonly Color Tile = Color.FromRgb(0x25, 0x25, 0x26);

        public static BitmapSource Create(int size)
        {
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                ctx.DrawRectangle(new SolidColorBrush(Tile), null, new Rect(0, 0, size, size));
                DrawEmblem(ctx, size, branches: size > 16);
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        // Coordinates are fractions of the icon's size.
        private static void DrawEmblem(DrawingContext ctx, double s, bool branches)
        {
            Point P(double x, double y) => new Point(x * s, y * s);
            var white = Brushes.White;

            var shield = new StreamGeometry();
            using (var g = shield.Open())
            {
                g.BeginFigure(P(0.12, 0.05), true, true);
                g.LineTo(P(0.88, 0.05), true, false);
                g.LineTo(P(0.88, 0.60), true, false);
                g.QuadraticBezierTo(P(0.88, 0.85), P(0.50, 0.96), true, false);
                g.QuadraticBezierTo(P(0.12, 0.85), P(0.12, 0.60), true, false);
            }
            ctx.DrawGeometry(new SolidColorBrush(ShieldBlue), null, shield);

            // Menorah: three nested arms and the stem, on a stepped base.
            const double top = 0.20, cx = 0.50;
            var arm = new Pen(white, Math.Max(1.0, s * (branches ? 0.040 : 0.07)));
            var spread = branches ? 0.068 : 0.11;
            for (var k = 1; k <= 3; k++)
            {
                var d = spread * k;
                var arc = new StreamGeometry();
                using (var g = arc.Open())
                {
                    g.BeginFigure(P(cx - d, top), false, false);
                    g.ArcTo(P(cx + d, top), new Size(d * s, d * 1.15 * s), 0, false, SweepDirection.Counterclockwise, true, false);
                }
                ctx.DrawGeometry(null, arm, arc);
            }
            ctx.DrawLine(arm, P(cx, top), P(cx, 0.64));
            ctx.DrawRectangle(white, null, new Rect(P(0.40, 0.62), P(0.60, 0.68)));
            ctx.DrawRectangle(white, null, new Rect(P(0.33, 0.68), P(0.67, 0.76)));

            if (!branches) return;

            // Olive branches up both sides: a curved stem with a leaf on each side every so often.
            var stemPen = new Pen(white, Math.Max(1.0, s * 0.022));
            const double x0 = 0.10, y0 = 0.85, xc = 0.34, yc = 0.74, x1 = 0.30, y1 = 0.12;
            foreach (var side in new[] { -1, 1 })
            {
                double X(double x) => cx + side * x;
                var stem = new StreamGeometry();
                using (var g = stem.Open())
                {
                    g.BeginFigure(P(X(x0), y0), false, false);
                    g.QuadraticBezierTo(P(X(xc), yc), P(X(x1), y1), true, false);
                }
                ctx.DrawGeometry(null, stemPen, stem);

                for (var i = 0; i < 6; i++)
                {
                    var t = 0.15 + i * 0.15;
                    var bx = (1 - t) * (1 - t) * x0 + 2 * (1 - t) * t * xc + t * t * x1;
                    var by = (1 - t) * (1 - t) * y0 + 2 * (1 - t) * t * yc + t * t * y1;
                    foreach (var outward in new[] { true, false })
                    {
                        var lx = X(bx + (outward ? 0.032 : -0.030));
                        var ly = by - 0.02;
                        var leaf = new EllipseGeometry(P(lx, ly), s * 0.026, s * 0.012,
                            new RotateTransform(side * (outward ? -50 : 50), lx * s, ly * s));
                        ctx.DrawGeometry(white, null, leaf);
                    }
                }
            }
        }
    }
}
