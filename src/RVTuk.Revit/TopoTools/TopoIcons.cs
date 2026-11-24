using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// The Topo Tools ribbon icons (Topo Tools, Floor ⇄ Topo and the Topo Edit panel), drawn like
    /// the toolkit's other icons: dark square, orange strokes, white accents. Cancel and Accept are
    /// the exception: a red ✗ and a green ✓ on a transparent background, like Revit's own, in a
    /// light-theme and a dark-theme version. Each takes the size in pixels: 32 for LargeImage, 16
    /// for Image. Shapes are kept bold enough to read at 16.
    /// </summary>
    internal static class TopoIcons
    {
        private static readonly WpfColor Background = WpfColor.FromRgb(0x25, 0x25, 0x26);
        private static readonly WpfColor Orange = WpfColor.FromRgb(0xFF, 0x8C, 0x00);

        // ✗ and ✓ per theme: #D9312B and #2F9E44 in the light one, #FF6666 and #62D987 in the dark.
        private static readonly WpfColor CancelRedLight = WpfColor.FromRgb(0xD9, 0x31, 0x2B);
        private static readonly WpfColor AcceptGreenLight = WpfColor.FromRgb(0x2F, 0x9E, 0x44);
        private static readonly WpfColor CancelRedDark = WpfColor.FromRgb(0xFF, 0x66, 0x66);
        private static readonly WpfColor AcceptGreenDark = WpfColor.FromRgb(0x62, 0xD9, 0x87);

        /// <summary>Topo Tools: three nested contour lines, with a point sitting on the middle one.</summary>
        internal static BitmapSource TopoTools(int size) => Draw(size, (ctx, s) =>
        {
            var pen = OrangePen(s);
            ctx.DrawEllipse(null, pen, new WpfPoint(s * 0.5, s * 0.55), s * 0.36, s * 0.24);
            ctx.DrawEllipse(null, pen, new WpfPoint(s * 0.5, s * 0.55), s * 0.23, s * 0.15);
            ctx.DrawEllipse(null, pen, new WpfPoint(s * 0.5, s * 0.55), s * 0.10, s * 0.07);

            ctx.DrawEllipse(Brushes.White, null, new WpfPoint(s * 0.5, s * 0.31), s * 0.07, s * 0.07);
        });

        /// <summary>Draw Lines: a white pencil whose orange tip has just drawn an orange line.</summary>
        internal static BitmapSource DrawLines(int size) => Draw(size, (ctx, s) =>
        {
            // The line runs from the tip to the right edge.
            ctx.DrawLine(OrangePen(s), new WpfPoint(s * 0.18, s * 0.84), new WpfPoint(s * 0.88, s * 0.84));

            // Pencil at 45°: body from its collar up to the top right, tip down to the line.
            ctx.DrawGeometry(Brushes.White, null, Polygon(s,
                (0.357, 0.791), (0.810, 0.338), (0.682, 0.210), (0.229, 0.663)));
            ctx.DrawGeometry(new SolidColorBrush(Orange), null, Polygon(s,
                (0.357, 0.791), (0.229, 0.663), (0.18, 0.84)));
        });

        /// <summary>Height Labels: an orange line with a white height tag over its middle.</summary>
        internal static BitmapSource HeightLabels(int size) => Draw(size, (ctx, s) =>
        {
            ctx.DrawLine(OrangePen(s), new WpfPoint(s * 0.10, s * 0.76), new WpfPoint(s * 0.90, s * 0.76));

            ctx.DrawRectangle(Brushes.White, null, new Rect(s * 0.24, s * 0.22, s * 0.52, s * 0.34));
            // The "number" in the tag: a dark bar, legible where real digits would not be.
            var digits = new Pen(new SolidColorBrush(Background), Math.Max(1, s * 0.08));
            digits.Freeze();
            ctx.DrawLine(digits, new WpfPoint(s * 0.34, s * 0.39), new WpfPoint(s * 0.66, s * 0.39));
        });

        /// <summary>Pick: a white mouse pointer clicking an orange line.</summary>
        internal static BitmapSource Pick(int size) => Draw(size, (ctx, s) =>
        {
            ctx.DrawLine(OrangePen(s), new WpfPoint(s * 0.10, s * 0.30), new WpfPoint(s * 0.90, s * 0.30));

            // The pointer: tip on the line, body down to the bottom right, with its tail.
            ctx.DrawGeometry(Brushes.White, null, Polygon(s,
                (0.36, 0.30), (0.36, 0.86), (0.49, 0.73), (0.59, 0.92), (0.68, 0.87), (0.58, 0.69), (0.76, 0.69)));
        });

        /// <summary>Update: a white clockwise refresh arrow around an orange contour.</summary>
        internal static BitmapSource Update(int size) => Draw(size, (ctx, s) =>
        {
            ctx.DrawEllipse(null, OrangePen(s), new WpfPoint(s * 0.5, s * 0.5), s * 0.16, s * 0.11);

            // Angles in degrees, measured clockwise on screen from the positive x axis.
            double cx = s * 0.5, cy = s * 0.5, r = s * 0.32;
            WpfPoint At(double degrees, double radius)
            {
                double a = degrees * Math.PI / 180;
                return new WpfPoint(cx + radius * Math.Cos(a), cy + radius * Math.Sin(a));
            }

            // A 290° arc with the gap at the top right; the arrow head sits on its end.
            const double Start = -40, End = 250;
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                g.BeginFigure(At(Start, r), false, false);
                g.ArcTo(At(End, r), new Size(r, r), 0, true, SweepDirection.Clockwise, true, false);
            }
            arc.Freeze();
            ctx.DrawGeometry(null, WhitePen(s), arc);

            // Head: base across the arc's end, tip ahead along the clockwise tangent.
            var end = At(End, r);
            double endAngle = End * Math.PI / 180;
            var tangent = new Vector(-Math.Sin(endAngle), Math.Cos(endAngle));
            var across = new Vector(Math.Cos(endAngle), Math.Sin(endAngle));
            ctx.DrawGeometry(Brushes.White, null, Polygon(
                end + tangent * (s * 0.16), end + across * (s * 0.11), end - across * (s * 0.11)));
        });

        /// <summary>Accept: a green check mark on a transparent background, in the dark theme's
        /// green when <paramref name="dark"/>.</summary>
        internal static BitmapSource Accept(int size, bool dark) => DrawTransparent(size, (ctx, s) =>
        {
            var pen = BoldPen(dark ? AcceptGreenDark : AcceptGreenLight, s);
            ctx.DrawGeometry(null, pen, Polyline(s, (0.18, 0.52), (0.40, 0.74), (0.82, 0.28)));
        });

        /// <summary>Cancel: a red X on a transparent background, in the dark theme's red when
        /// <paramref name="dark"/>.</summary>
        internal static BitmapSource Cancel(int size, bool dark) => DrawTransparent(size, (ctx, s) =>
        {
            var pen = BoldPen(dark ? CancelRedDark : CancelRedLight, s);
            ctx.DrawLine(pen, new WpfPoint(s * 0.24, s * 0.24), new WpfPoint(s * 0.76, s * 0.76));
            ctx.DrawLine(pen, new WpfPoint(s * 0.76, s * 0.24), new WpfPoint(s * 0.24, s * 0.76));
        });

        /// <summary>Floor ⇄ Topo: a flat white slab over orange terrain, a two-way arrow between.</summary>
        internal static BitmapSource FloorTopo(int size) => Draw(size, (ctx, s) =>
        {
            ctx.DrawRectangle(Brushes.White, null, new Rect(s * 0.12, s * 0.08, s * 0.76, s * 0.13));

            ctx.DrawGeometry(new SolidColorBrush(Orange), null, Polygon(s,
                (0.12, 0.92), (0.12, 0.84), (0.32, 0.74), (0.50, 0.82),
                (0.70, 0.70), (0.88, 0.78), (0.88, 0.92)));

            // Two-way arrow: a stem with a solid head at each end, clear of slab and terrain.
            var stem = new Pen(Brushes.White, Math.Max(1, s * 0.06));
            stem.Freeze();
            ctx.DrawLine(stem, new WpfPoint(s * 0.5, s * 0.36), new WpfPoint(s * 0.5, s * 0.54));
            ctx.DrawGeometry(Brushes.White, null, Polygon(s, (0.5, 0.27), (0.38, 0.39), (0.62, 0.39)));
            ctx.DrawGeometry(Brushes.White, null, Polygon(s, (0.5, 0.63), (0.38, 0.51), (0.62, 0.51)));
        });

        /// <summary>An icon on the toolkit's dark square.</summary>
        private static BitmapSource Draw(int size, Action<DrawingContext, double> drawShape) =>
            DrawTransparent(size, (ctx, s) =>
            {
                ctx.DrawRectangle(new SolidColorBrush(Background), null, new Rect(0, 0, s, s));
                drawShape(ctx, s);
            });

        /// <summary>An icon on a transparent background: only what <paramref name="drawShape"/>
        /// draws is opaque.</summary>
        private static BitmapSource DrawTransparent(int size, Action<DrawingContext, double> drawShape)
        {
            var dv = new DrawingVisual();
            using (var ctx = dv.RenderOpen())
                drawShape(ctx, size);
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        private static Pen OrangePen(double s)
        {
            var pen = new Pen(new SolidColorBrush(Orange), Math.Max(1, s * 0.06));
            pen.Freeze();
            return pen;
        }

        private static Pen WhitePen(double s)
        {
            var pen = new Pen(Brushes.White, Math.Max(1, s * 0.06));
            pen.Freeze();
            return pen;
        }

        /// <summary>A heavier, round-capped pen for the single-stroke marks (check, X).</summary>
        private static Pen BoldPen(WpfColor color, double s)
        {
            var pen = new Pen(new SolidColorBrush(color), Math.Max(1.5, s * 0.11))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            pen.Freeze();
            return pen;
        }

        /// <summary>A closed polygon through points given as fractions of the icon size.</summary>
        private static StreamGeometry Polygon(double s, params (double X, double Y)[] fractions) =>
            Figure(Scale(s, fractions), closed: true);

        private static StreamGeometry Polygon(params WpfPoint[] points) => Figure(points, closed: true);

        /// <summary>An open polyline through points given as fractions of the icon size.</summary>
        private static StreamGeometry Polyline(double s, params (double X, double Y)[] fractions) =>
            Figure(Scale(s, fractions), closed: false);

        private static WpfPoint[] Scale(double s, (double X, double Y)[] fractions)
        {
            var points = new WpfPoint[fractions.Length];
            for (int i = 0; i < points.Length; i++)
                points[i] = new WpfPoint(s * fractions[i].X, s * fractions[i].Y);
            return points;
        }

        private static StreamGeometry Figure(WpfPoint[] points, bool closed)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(points[0], closed, closed);
                for (int i = 1; i < points.Length; i++)
                    g.LineTo(points[i], true, true);
            }
            geometry.Freeze();
            return geometry;
        }
    }
}
