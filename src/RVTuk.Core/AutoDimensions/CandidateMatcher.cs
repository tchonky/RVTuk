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

    /// <summary>One reference line, as plain 2D endpoints.</summary>
    public record ReferenceLine(XyPoint Start, XyPoint End);

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

        /// <summary>Matches for a single line. A thin wrapper — one line owns everything.</summary>
        public static IReadOnlyList<int> FindMatchIndices(
            XyPoint lineStart,
            XyPoint lineEnd,
            IReadOnlyList<WallCandidate> segments,
            IReadOnlyList<CandidateMatch> matchModes,
            double alongsideReach)
        {
            var lines = new[] { new ReferenceLine(lineStart, lineEnd) };
            return FindMatchIndicesForLines(lines, segments, matchModes, alongsideReach)[0];
        }

        /// <summary>
        /// Matches for every reference line at once. Walls go to each line that crosses them; an
        /// opening goes to the single nearest line that qualifies, so a facade with three stacked
        /// dimension strings dimensions each door once, from the innermost.
        ///
        /// Only openings are owned. A wall crossed by three strings is measured by all three —
        /// that is what a chained string is.
        ///
        /// Ownership is decided among the lines an opening actually qualifies for (parallel host
        /// wall, centre within the line's span, within reach), not merely the nearest line:
        /// a door the nearest line cannot reach must still fall to one that can.
        /// </summary>
        public static IReadOnlyList<IReadOnlyList<int>> FindMatchIndicesForLines(
            IReadOnlyList<ReferenceLine> lines,
            IReadOnlyList<WallCandidate> segments,
            IReadOnlyList<CandidateMatch> matchModes,
            double alongsideReach)
        {
            var perLine = new List<List<(int Index, double T)>>();
            for (int i = 0; i < lines.Count; i++) perLine.Add(new List<(int, double)>());

            for (int c = 0; c < segments.Count; c++)
            {
                var mode = c < matchModes.Count ? matchModes[c] : CandidateMatch.Crossing;

                if (mode == CandidateMatch.Crossing)
                {
                    for (int l = 0; l < lines.Count; l++)
                    {
                        if (WallCrossingFinder.TryGetCrossingParameter(
                                lines[l].Start, lines[l].End, segments[c], out var t))
                            perLine[l].Add((c, t));
                    }
                    continue;
                }

                var owner = -1;
                var ownerT = 0.0;
                var ownerDistance = double.MaxValue;
                for (int l = 0; l < lines.Count; l++)
                {
                    if (!TryGetAlongside(lines[l].Start, lines[l].End, segments[c],
                            alongsideReach, out var t, out var distance)) continue;

                    // Strict: a tie keeps the earlier line, so a door exactly between two
                    // strings lands the same way on every re-run rather than shuffling.
                    if (distance >= ownerDistance) continue;

                    owner = l;
                    ownerT = t;
                    ownerDistance = distance;
                }

                if (owner >= 0) perLine[owner].Add((c, ownerT));
            }

            return perLine
                .Select(m => (IReadOnlyList<int>)m.OrderBy(x => x.T).Select(x => x.Index).ToList())
                .ToList();
        }

        /// <summary>
        /// Where the opening's centre projects along the line, if its wall runs parallel to the
        /// line, the centre falls inside the line's span, and it lies within reach. Also hands
        /// back the perpendicular distance, which is what decides ownership between lines.
        /// </summary>
        private static bool TryGetAlongside(
            XyPoint lineStart,
            XyPoint lineEnd,
            WallCandidate segment,
            double reach,
            out double t,
            out double distance)
        {
            t = 0;
            distance = 0;

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
            var perpendicular = Math.Abs(dx * lineY - dy * lineX) / lineLength;
            if (perpendicular > reach) return false;

            t = candidateT;
            distance = perpendicular;
            return true;
        }
    }
}
