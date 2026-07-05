using System;
using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>
    /// Finds which walls transversally cross a reference line, in 2D (view-plane) coordinates,
    /// excluding near-parallel/collinear walls and endpoint-only (T-junction) touches.
    /// </summary>
    public static class WallCrossingFinder
    {
        private const double ParallelToleranceDegrees = 5.0;
        private const double BoundaryEpsilon = 1e-6;

        public static IReadOnlyList<int> FindCrossingIndices(
            XyPoint lineStart, XyPoint lineEnd, IReadOnlyList<WallCandidate> walls)
        {
            var d1X = lineEnd.X - lineStart.X;
            var d1Y = lineEnd.Y - lineStart.Y;
            var lineAngle = Math.Atan2(d1Y, d1X);

            var crossings = new List<(int Index, double T)>();

            for (int i = 0; i < walls.Count; i++)
            {
                var wall = walls[i];
                var d2X = wall.End.X - wall.Start.X;
                var d2Y = wall.End.Y - wall.Start.Y;

                var wallAngle = Math.Atan2(d2Y, d2X);
                if (AngleFromParallelDegrees(lineAngle, wallAngle) < ParallelToleranceDegrees)
                    continue;

                var denom = d1X * d2Y - d1Y * d2X;
                if (Math.Abs(denom) < 1e-12) continue;

                var dx = wall.Start.X - lineStart.X;
                var dy = wall.Start.Y - lineStart.Y;

                var t = (dx * d2Y - dy * d2X) / denom;
                var s = (dx * d1Y - dy * d1X) / denom;

                if (t <= BoundaryEpsilon || t >= 1 - BoundaryEpsilon) continue;
                if (s <= BoundaryEpsilon || s >= 1 - BoundaryEpsilon) continue;

                crossings.Add((i, t));
            }

            return crossings.OrderBy(c => c.T).Select(c => c.Index).ToList();
        }

        /// <summary>0 = the two directions are parallel/collinear, 90 = perpendicular.</summary>
        private static double AngleFromParallelDegrees(double angleA, double angleB)
        {
            var diffDegrees = Math.Abs(angleA - angleB) * 180.0 / Math.PI;
            diffDegrees %= 180.0;
            if (diffDegrees > 90.0) diffDegrees = 180.0 - diffDegrees;
            return diffDegrees;
        }
    }
}
