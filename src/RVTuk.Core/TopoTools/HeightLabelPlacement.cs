using System;
using System.Collections.Generic;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Core.TopoTools
{
    /// <summary>Where a height text sits on a line, and the angle it is drawn at.</summary>
    public readonly record struct LabelSpot(XyPoint Point, double AngleRadians);

    /// <summary>
    /// Places a height text at the middle of a polyline, measured by length.
    /// </summary>
    public static class HeightLabelPlacement
    {
        /// <summary>
        /// The point at half the polyline's total length, with the direction of the segment it lies
        /// on, normalised to (−π/2, π/2] so the text never reads upside down. Zero-length segments are
        /// skipped. A polyline with fewer than two points, or zero total length, returns its first
        /// point (or (0,0) when empty) with angle 0.
        /// </summary>
        public static LabelSpot Midpoint(IReadOnlyList<XyPoint> polyline)
        {
            if (polyline.Count < 2)
                return new LabelSpot(polyline.Count == 0 ? new XyPoint(0, 0) : polyline[0], 0);

            double total = 0;
            for (int i = 1; i < polyline.Count; i++)
                total += Length(polyline[i - 1], polyline[i]);

            if (total <= 0)
                return new LabelSpot(polyline[0], 0);

            double half = total / 2;
            double walked = 0;
            for (int i = 1; i < polyline.Count; i++)
            {
                XyPoint a = polyline[i - 1];
                XyPoint b = polyline[i];
                double segment = Length(a, b);
                if (segment <= 0) continue;

                if (walked + segment >= half)
                {
                    double t = (half - walked) / segment;
                    var point = new XyPoint(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
                    return new LabelSpot(point, Normalise(Math.Atan2(b.Y - a.Y, b.X - a.X)));
                }
                walked += segment;
            }

            // Unreachable: the walk adds the same lengths in the same order as `total`, so the last non-zero segment always reaches `half`.
            return new LabelSpot(polyline[polyline.Count - 1], 0);
        }

        private static double Length(XyPoint a, XyPoint b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Folds an angle from (−π, π] into (−π/2, π/2] by reversing the direction when needed.</summary>
        private static double Normalise(double angle)
        {
            if (angle > Math.PI / 2) return angle - Math.PI;
            if (angle <= -Math.PI / 2) return angle + Math.PI;
            return angle;
        }
    }
}
