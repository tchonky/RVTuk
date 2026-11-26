using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RVTuk.Revit.WallFinishes
{
    /// <summary>
    /// Offsets a loop of the outline 10 cm into the open space, so the lines meet cleanly at the
    /// corners without corner code of our own. The offset is not one-to-one with the edges — a
    /// short edge at a concave corner can vanish — so each offset curve finds its edge by
    /// geometry (parallel, 10 cm away), never by its place in the loop.
    /// </summary>
    public static class FinishOffset
    {
        public const double Distance = 10 / 30.48;
        private const double Tolerance = 1 / 304.8;

        public static List<FinishEdge> Apply(FinishLoop loop, out bool failed)
        {
            failed = false;
            CurveLoop? offset;
            try
            {
                var source = CurveLoop.Create(loop.Edges.Select(e => e.Edge).ToList());
                offset = CurveLoop.CreateViaOffset(source, Distance, XYZ.BasisZ);
                // The face lies under the outline; the side we want is the one off it.
                if (loop.Face.Project(Mid(offset.First())) != null)
                    offset = CurveLoop.CreateViaOffset(source, -Distance, XYZ.BasisZ);
            }
            catch (Exception)
            {
                failed = true;
                return new List<FinishEdge>();
            }

            var curves = offset.ToList();
            if (curves.Count == 0 || loop.Face.Project(Mid(curves[0])) != null || CrossesItself(curves))
            {
                failed = true; // a gap narrower than twice the offset
                return new List<FinishEdge>();
            }

            return curves.Select(c => Match(c, loop.Edges)).ToList();
        }

        private static FinishEdge Match(Curve curve, List<FinishEdge> edges)
        {
            var mid = Mid(curve);
            var tangent = Tangent(curve, 0.5);
            foreach (var edge in edges)
            {
                var projection = edge.Edge.Project(mid);
                if (projection == null || Math.Abs(projection.Distance - Distance) > Tolerance) continue;
                var along = Tangent(edge.Edge, edge.Edge.ComputeNormalizedParameter(projection.Parameter));
                if (Math.Abs(tangent.DotProduct(along)) < 0.999) continue;
                return new FinishEdge { Edge = curve, MaterialId = edge.MaterialId, Skip = edge.Skip };
            }
            return new FinishEdge { Edge = curve, Skip = true }; // no edge to take a finish from
        }

        private static bool CrossesItself(List<Curve> curves)
        {
            int n = curves.Count;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue; // neighbours round the loop's seam
                    if (Overlap(curves[i], curves[j])) return true;
                }
            }
            return false;
        }

        // The one-argument Intersect is gone from the 2027 API; its replacement is not in 2024's.
        private static bool Overlap(Curve a, Curve b) =>
#if REVIT2027
            a.Intersect(b, CurveIntersectResultOption.Simple).Result == SetComparisonResult.Overlap;
#else
            a.Intersect(b) == SetComparisonResult.Overlap;
#endif

        private static XYZ Mid(Curve curve) => curve.Evaluate(0.5, true);

        private static XYZ Tangent(Curve curve, double normalized) =>
            curve.ComputeDerivatives(normalized, true).BasisX.Normalize();
    }
}
