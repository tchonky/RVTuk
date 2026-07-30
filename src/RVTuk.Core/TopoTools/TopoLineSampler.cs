using System;
using System.Collections.Generic;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// Turns a tessellated polyline into the points a topo line contributes to the toposolid.
    ///
    /// Every vertex is a candidate, and any segment longer than the spacing is divided evenly, so
    /// no gap exceeds the spacing and arcs keep their shape. A candidate is dropped when it lands
    /// within a tenth of the spacing of the previous kept point <b>or of the first</b> — the second
    /// test is what stops a closed loop, whose tessellation repeats the start point at the end,
    /// from stacking two coincident points. Vertices are not exempt: a tight arc tessellates into
    /// vertices millimetres apart, and those are slivers, not detail.
    ///
    /// Even division already keeps interior candidates more than half a spacing apart, so the drop
    /// test only ever fires on genuine near-duplicates.
    /// </summary>
    public static class TopoLineSampler
    {
        public static IReadOnlyList<XyPoint> Sample(IReadOnlyList<XyPoint> polyline, double spacing)
        {
            if (spacing <= 0)
                throw new ArgumentOutOfRangeException(nameof(spacing), "Spacing must be positive.");

            var kept = new List<XyPoint>();
            if (polyline == null || polyline.Count == 0) return kept;

            double minSeparation = spacing / 10.0;

            void Offer(XyPoint candidate)
            {
                if (kept.Count == 0) { kept.Add(candidate); return; }
                if (Distance(kept[kept.Count - 1], candidate) < minSeparation) return;
                if (Distance(kept[0], candidate) < minSeparation) return;
                kept.Add(candidate);
            }

            Offer(polyline[0]);

            for (int i = 1; i < polyline.Count; i++)
            {
                var from = polyline[i - 1];
                var to = polyline[i];
                double length = Distance(from, to);

                if (length > spacing)
                {
                    int divisions = (int)Math.Ceiling(length / spacing);
                    for (int step = 1; step < divisions; step++)
                    {
                        double t = (double)step / divisions;
                        Offer(new XyPoint(
                            from.X + (to.X - from.X) * t,
                            from.Y + (to.Y - from.Y) * t));
                    }
                }

                Offer(to);
            }

            return kept;
        }

        private static double Distance(XyPoint a, XyPoint b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
