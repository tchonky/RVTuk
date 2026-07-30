using System;
using System.Collections.Generic;
using System.Linq;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>
    /// Finds which walls transversally cross a reference line, in 2D (view-plane) coordinates,
    /// excluding near-parallel/collinear walls and endpoint-only (T-junction) touches.
    /// </summary>
    public static class WallCrossingFinder
    {
        public const double ParallelToleranceDegrees = 5.0;
        private const double BoundaryEpsilon = 1e-6;

        public static IReadOnlyList<int> FindCrossingIndices(
            XyPoint lineStart, XyPoint lineEnd, IReadOnlyList<WallCandidate> walls)
        {
            var crossings = new List<(int Index, double T)>();

            for (int i = 0; i < walls.Count; i++)
            {
                if (TryGetCrossingParameter(lineStart, lineEnd, walls[i], out var t))
                    crossings.Add((i, t));
            }

            return crossings.OrderBy(c => c.T).Select(c => c.Index).ToList();
        }

        /// <summary>
        /// Where along the line (0 at its start, 1 at its end) the wall transversally crosses,
        /// or false when it doesn't. Shared with <see cref="CandidateMatcher"/>, which
        /// interleaves crossings with openings matched a different way.
        /// </summary>
        public static bool TryGetCrossingParameter(
            XyPoint lineStart, XyPoint lineEnd, WallCandidate wall, out double t)
        {
            t = 0;

            var d1X = lineEnd.X - lineStart.X;
            var d1Y = lineEnd.Y - lineStart.Y;
            var d2X = wall.End.X - wall.Start.X;
            var d2Y = wall.End.Y - wall.Start.Y;

            var lineAngle = Math.Atan2(d1Y, d1X);
            var wallAngle = Math.Atan2(d2Y, d2X);
            if (Angle2D.FromParallelDegrees(lineAngle, wallAngle) < ParallelToleranceDegrees)
                return false;

            var denom = d1X * d2Y - d1Y * d2X;
            if (Math.Abs(denom) < 1e-12) return false;

            var dx = wall.Start.X - lineStart.X;
            var dy = wall.Start.Y - lineStart.Y;

            var candidateT = (dx * d2Y - dy * d2X) / denom;
            var s = (dx * d1Y - dy * d1X) / denom;

            if (candidateT <= BoundaryEpsilon || candidateT >= 1 - BoundaryEpsilon) return false;
            if (s <= BoundaryEpsilon || s >= 1 - BoundaryEpsilon) return false;

            t = candidateT;
            return true;
        }
    }
}
