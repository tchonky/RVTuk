using System;
using System.Collections.Generic;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// Clips a topo line to a target's outline (outer loop plus holes, even-odd) and splits the
    /// sampled points into interior and edge points. Each segment is cut at every crossing with
    /// a loop edge; sub-segments whose midpoint is inside (boundary included) are kept and joined
    /// into runs. Zero-length edges from curve tessellation are tolerated.
    /// </summary>
    public static class TopoLineClipper
    {
        private const double Epsilon = 1e-12;

        public static IReadOnlyList<IReadOnlyList<XyPoint>> Clip(
            IReadOnlyList<XyPoint> polyline, IReadOnlyList<IReadOnlyList<XyPoint>> loops)
        {
            var runs = new List<IReadOnlyList<XyPoint>>();
            if (polyline == null || polyline.Count < 2 || loops == null) return runs;

            List<XyPoint> current = null;

            void Close()
            {
                if (current != null && current.Count >= 2) runs.Add(current);
                current = null;
            }

            for (int i = 1; i < polyline.Count; i++)
            {
                var p = polyline[i - 1];
                var q = polyline[i];
                if (p.X == q.X && p.Y == q.Y) continue;

                var ts = Crossings(p, q, loops);
                for (int k = 1; k < ts.Count; k++)
                {
                    var from = Lerp(p, q, ts[k - 1]);
                    var to = Lerp(p, q, ts[k]);
                    if (from.X == to.X && from.Y == to.Y) continue;

                    var mid = new XyPoint((from.X + to.X) / 2, (from.Y + to.Y) / 2);
                    if (PolygonContainment.Contains(loops, mid))
                    {
                        if (current == null) current = new List<XyPoint> { from };
                        current.Add(to);
                    }
                    else
                    {
                        Close();
                    }
                }
            }
            Close();
            return runs;
        }

        public static TopoLinePoints Points(
            IReadOnlyList<XyPoint> polyline, IReadOnlyList<IReadOnlyList<XyPoint>> loops,
            double spacing, double edgeTolerance)
        {
            var interior = new List<XyPoint>();
            var edge = new List<XyPoint>();

            foreach (var run in Clip(polyline, loops))
            {
                foreach (var point in TopoLineSampler.Sample(run, spacing))
                {
                    if (PolygonContainment.IsNearEdge(loops, point, edgeTolerance)) edge.Add(point);
                    else interior.Add(point);
                }
            }
            return new TopoLinePoints(interior, edge);
        }

        /// <summary>Sorted parameters along p to q (always including 0 and 1) where it meets a loop edge.</summary>
        private static List<double> Crossings(XyPoint p, XyPoint q, IReadOnlyList<IReadOnlyList<XyPoint>> loops)
        {
            var ts = new List<double> { 0, 1 };
            double dx = q.X - p.X, dy = q.Y - p.Y;
            double dd = dx * dx + dy * dy;

            foreach (var loop in loops)
            {
                if (loop == null || loop.Count < 2) continue;
                for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                {
                    var a = loop[j];
                    var b = loop[i];
                    double ex = b.X - a.X, ey = b.Y - a.Y;
                    double denom = dx * ey - dy * ex;
                    double apx = a.X - p.X, apy = a.Y - p.Y;

                    double scale = Math.Sqrt(dd) * Math.Sqrt(ex * ex + ey * ey);
                    if (scale == 0 || Math.Abs(denom) <= Epsilon * scale)
                    {
                        // Parallel, or a zero-length edge: only a collinear edge contributes its endpoints.
                        double cross = apx * dy - apy * dx;
                        if (Math.Abs(cross) > 1e-9 * Math.Sqrt(dd)) continue;
                        AddIfInside(ts, ((a.X - p.X) * dx + (a.Y - p.Y) * dy) / dd);
                        AddIfInside(ts, ((b.X - p.X) * dx + (b.Y - p.Y) * dy) / dd);
                        continue;
                    }

                    double t = (apx * ey - apy * ex) / denom;
                    double u = (apx * dy - apy * dx) / denom;
                    if (u >= 0 && u <= 1) AddIfInside(ts, t);
                }
            }

            ts.Sort();
            return ts;
        }

        private static void AddIfInside(List<double> ts, double t)
        {
            if (t > 0 && t < 1) ts.Add(t);
        }

        private static XyPoint Lerp(XyPoint p, XyPoint q, double t)
        {
            if (t <= 0) return p;
            if (t >= 1) return q;
            return new XyPoint(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t);
        }
    }
}
