using System;
using System.Collections.Generic;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// Even-odd containment across every loop of a footprint, so a second loop reads as a hole
    /// without anyone having to say which loop is the outer one.
    ///
    /// A point on a boundary counts as inside: the ray-crossing rule alone answers edge cases
    /// arbitrarily, and a sampled point landing exactly on a toposolid's edge belongs to it.
    /// The loops arrive tessellated curve by curve, so they contain repeated points and
    /// zero-length segments; both are handled rather than assumed away.
    /// </summary>
    public static class PolygonContainment
    {
        public static bool Contains(
            IReadOnlyList<IReadOnlyList<XyPoint>> loops, XyPoint point, double edgeTolerance = 1e-9)
        {
            if (loops == null || loops.Count == 0) return false;

            foreach (var loop in loops)
            {
                if (loop == null || loop.Count < 2) continue;
                for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                {
                    if (DistanceToSegment(point, loop[j], loop[i]) <= edgeTolerance) return true;
                }
            }

            bool inside = false;
            foreach (var loop in loops)
            {
                if (loop == null || loop.Count < 3) continue;
                for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                {
                    var a = loop[i];
                    var b = loop[j];
                    if ((a.Y > point.Y) != (b.Y > point.Y) &&
                        point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                    {
                        inside = !inside;
                    }
                }
            }
            return inside;
        }

        private static double DistanceToSegment(XyPoint point, XyPoint a, XyPoint b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double lengthSquared = dx * dx + dy * dy;

            if (lengthSquared <= 0) return Distance(point, a);

            double t = ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared;
            t = Math.Max(0, Math.Min(1, t));
            return Distance(point, new XyPoint(a.X + t * dx, a.Y + t * dy));
        }

        private static double Distance(XyPoint a, XyPoint b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
