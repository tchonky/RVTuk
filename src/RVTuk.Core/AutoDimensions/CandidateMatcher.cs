using System;
using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.AutoDimensions
{
    /// <summary>How a candidate earns its place on a reference line.</summary>
    public enum CandidateMatch
    {
        /// <summary>The line passes through it — a wall, measured across its thickness.</summary>
        Crossing,

        /// <summary>
        /// The line runs alongside it — an opening in a wall parallel to the line, measured
        /// across its width.
        /// </summary>
        Alongside,
    }

    /// <summary>
    /// Which candidates a reference line dimensions, in order along it.
    ///
    /// The two kinds are matched by opposite tests, because their references face opposite ways
    /// (see <see cref="ReferenceAlignment"/>). A wall is measured across its thickness, so the
    /// line must cross it. An opening is measured across its width, so the line must run ALONG
    /// its host wall — a line crossing that wall lies parallel to the jambs and cannot measure
    /// them at all. Matching openings by crossing, as the original design did, selected them
    /// only in the one arrangement where they are unusable.
    ///
    /// Running alongside has no intersection to key on, so an opening qualifies on three counts:
    /// its host wall is near-parallel to the line, its centre falls within the line's span, and
    /// it sits within reach of the line. That reach is the user's to set — it is really the
    /// question "how far from a wall may its dimension line sit", which is drafting convention,
    /// not geometry.
    ///
    /// Both kinds come back interleaved in one order along the line, which is what makes a
    /// dimension string read correctly: cross wall, jamb, jamb, cross wall.
    /// </summary>
    public static class CandidateMatcher
    {
        private const double BoundaryEpsilon = 1e-6;

        public static IReadOnlyList<int> FindMatchIndices(
            XyPoint lineStart,
            XyPoint lineEnd,
            IReadOnlyList<WallCandidate> segments,
            IReadOnlyList<CandidateMatch> matchModes,
            double alongsideReach)
        {
            var matches = new List<(int Index, double T)>();

            for (int i = 0; i < segments.Count; i++)
            {
                var mode = i < matchModes.Count ? matchModes[i] : CandidateMatch.Crossing;

                if (mode == CandidateMatch.Crossing)
                {
                    if (WallCrossingFinder.TryGetCrossingParameter(lineStart, lineEnd, segments[i], out var crossingT))
                        matches.Add((i, crossingT));
                }
                else if (TryGetAlongsideParameter(lineStart, lineEnd, segments[i], alongsideReach, out var alongsideT))
                {
                    matches.Add((i, alongsideT));
                }
            }

            return matches.OrderBy(m => m.T).Select(m => m.Index).ToList();
        }

        /// <summary>
        /// Where the opening's centre projects along the line, if its wall runs parallel to the
        /// line, the centre falls inside the line's span, and it lies within reach.
        /// </summary>
        private static bool TryGetAlongsideParameter(
            XyPoint lineStart, XyPoint lineEnd, WallCandidate segment, double reach, out double t)
        {
            t = 0;

            var lineX = lineEnd.X - lineStart.X;
            var lineY = lineEnd.Y - lineStart.Y;
            var lineLengthSquared = lineX * lineX + lineY * lineY;
            if (lineLengthSquared < 1e-24) return false;

            var segmentX = segment.End.X - segment.Start.X;
            var segmentY = segment.End.Y - segment.Start.Y;
            if (Math.Abs(segmentX) < 1e-12 && Math.Abs(segmentY) < 1e-12) return false;

            var lineAngle = Math.Atan2(lineY, lineX);
            var segmentAngle = Math.Atan2(segmentY, segmentX);
            if (Angle2D.FromParallelDegrees(lineAngle, segmentAngle) >= WallCrossingFinder.ParallelToleranceDegrees)
                return false;

            var centerX = (segment.Start.X + segment.End.X) / 2.0;
            var centerY = (segment.Start.Y + segment.End.Y) / 2.0;

            var dx = centerX - lineStart.X;
            var dy = centerY - lineStart.Y;

            var candidateT = (dx * lineX + dy * lineY) / lineLengthSquared;
            if (candidateT <= BoundaryEpsilon || candidateT >= 1 - BoundaryEpsilon) return false;

            // Perpendicular distance, either side of the line.
            var lineLength = Math.Sqrt(lineLengthSquared);
            var distance = Math.Abs(dx * lineY - dy * lineX) / lineLength;
            if (distance > reach) return false;

            t = candidateT;
            return true;
        }
    }
}
