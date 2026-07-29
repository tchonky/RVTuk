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
    /// them at all.
    ///
    /// Running alongside has no intersection to key on, so an opening qualifies on three counts:
    /// its host wall is near-parallel to the line, its centre falls within the line's span, and
    /// nothing parallel stands between its host wall and the line. That last is what a distance
    /// setting used to approximate: a dimension string reaches the whole run it belongs to and
    /// stops at the first wall behind it, which is a question of what is in the way, not of how
    /// many millimetres away it sits.
    ///
    /// Both kinds come back interleaved in one order along the line, which is what makes a
    /// dimension string read correctly: cross wall, jamb, jamb, cross wall.
    /// </summary>
    public static class CandidateMatcher
    {
        private const double BoundaryEpsilon = 1e-6;

        /// <summary>
        /// How much nearer the line a wall must be than an opening to stand in its way. Chiefly
        /// there so an opening's own host wall is not read as blocking it: a door sits on its
        /// host's location curve, so the two differ by rounding alone.
        /// </summary>
        private const double BlockingEpsilon = 1e-6;

        /// <summary>Matches for a single line. A thin wrapper — one line owns everything.</summary>
        public static IReadOnlyList<int> FindMatchIndices(
            XyPoint lineStart,
            XyPoint lineEnd,
            IReadOnlyList<WallCandidate> segments,
            IReadOnlyList<CandidateMatch> matchModes,
            IReadOnlyList<WallCandidate> occluders)
        {
            var lines = new[] { new ReferenceLine(lineStart, lineEnd) };
            return FindMatchIndicesForLines(lines, segments, matchModes, occluders)[0];
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
        /// wall, centre within the line's span, nothing in the way), not merely the nearest line:
        /// a door the nearest line cannot see must still fall to one that can.
        ///
        /// <paramref name="occluders"/> is every wall that could stand in the way, which is not
        /// the same list as the walls being dimensioned — a wall still blocks when the user has
        /// unticked Walls.
        /// </summary>
        public static IReadOnlyList<IReadOnlyList<int>> FindMatchIndicesForLines(
            IReadOnlyList<ReferenceLine> lines,
            IReadOnlyList<WallCandidate> segments,
            IReadOnlyList<CandidateMatch> matchModes,
            IReadOnlyList<WallCandidate> occluders)
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
                            out var t, out var offset)) continue;
                    if (IsBlocked(lines[l].Start, lines[l].End, occluders, t, offset)) continue;

                    // Strict: a tie keeps the earlier line, so a door exactly between two
                    // strings lands the same way on every re-run rather than shuffling.
                    var distance = Math.Abs(offset);
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
        /// line and the centre falls inside the line's span. Also hands back the SIGNED
        /// perpendicular offset: its magnitude decides ownership between lines, and its sign says
        /// which side of the line the opening is on, which is what makes "in the way" answerable.
        /// </summary>
        private static bool TryGetAlongside(
            XyPoint lineStart,
            XyPoint lineEnd,
            WallCandidate segment,
            out double t,
            out double offset)
        {
            t = 0;
            offset = 0;

            var lineX = lineEnd.X - lineStart.X;
            var lineY = lineEnd.Y - lineStart.Y;
            var lineLengthSquared = lineX * lineX + lineY * lineY;
            if (lineLengthSquared < 1e-24) return false;

            var segmentX = segment.End.X - segment.Start.X;
            var segmentY = segment.End.Y - segment.Start.Y;
            if (Math.Abs(segmentX) < 1e-12 && Math.Abs(segmentY) < 1e-12) return false;

            if (!IsParallelToLine(lineX, lineY, segmentX, segmentY)) return false;

            var centerX = (segment.Start.X + segment.End.X) / 2.0;
            var centerY = (segment.Start.Y + segment.End.Y) / 2.0;

            var candidateT = ProjectOnLine(
                lineStart, lineX, lineY, lineLengthSquared, centerX, centerY);
            if (candidateT <= BoundaryEpsilon || candidateT >= 1 - BoundaryEpsilon) return false;

            t = candidateT;
            offset = SignedOffset(
                lineStart, lineX, lineY, Math.Sqrt(lineLengthSquared), centerX, centerY);
            return true;
        }

        /// <summary>
        /// Whether a parallel wall stands between the opening and the line: on the same side of
        /// it, strictly nearer, and spanning the opening's station along the line.
        ///
        /// The opening's own host wall excludes itself here with no special case — a door's
        /// location sits on its host's location curve, so "strictly nearer" fails for it. A wall
        /// the line crosses is never parallel, so it never blocks. A wall that stops short of the
        /// opening's station does not block either, which is what lets a line see an opening
        /// through a gap in the wall run in front of it.
        /// </summary>
        private static bool IsBlocked(
            XyPoint lineStart,
            XyPoint lineEnd,
            IReadOnlyList<WallCandidate> occluders,
            double openingT,
            double openingOffset)
        {
            var lineX = lineEnd.X - lineStart.X;
            var lineY = lineEnd.Y - lineStart.Y;
            var lineLengthSquared = lineX * lineX + lineY * lineY;
            if (lineLengthSquared < 1e-24) return false;
            var lineLength = Math.Sqrt(lineLengthSquared);

            foreach (var wall in occluders)
            {
                var wallX = wall.End.X - wall.Start.X;
                var wallY = wall.End.Y - wall.Start.Y;
                if (Math.Abs(wallX) < 1e-12 && Math.Abs(wallY) < 1e-12) continue;
                if (!IsParallelToLine(lineX, lineY, wallX, wallY)) continue;

                // Parallel, so every point of it shares one offset; the midpoint speaks for it.
                var wallOffset = SignedOffset(
                    lineStart, lineX, lineY, lineLength,
                    (wall.Start.X + wall.End.X) / 2.0,
                    (wall.Start.Y + wall.End.Y) / 2.0);

                if (wallOffset * openingOffset <= 0) continue; // other side of the line
                if (Math.Abs(wallOffset) >= Math.Abs(openingOffset) - BlockingEpsilon) continue;

                var t0 = ProjectOnLine(
                    lineStart, lineX, lineY, lineLengthSquared, wall.Start.X, wall.Start.Y);
                var t1 = ProjectOnLine(
                    lineStart, lineX, lineY, lineLengthSquared, wall.End.X, wall.End.Y);
                if (t0 > t1) (t0, t1) = (t1, t0);

                if (openingT > t0 + BoundaryEpsilon && openingT < t1 - BoundaryEpsilon) return true;
            }

            return false;
        }

        private static bool IsParallelToLine(
            double lineX, double lineY, double segmentX, double segmentY)
        {
            var lineAngle = Math.Atan2(lineY, lineX);
            var segmentAngle = Math.Atan2(segmentY, segmentX);
            return Angle2D.FromParallelDegrees(lineAngle, segmentAngle)
                < WallCrossingFinder.ParallelToleranceDegrees;
        }

        /// <summary>How far along the line a point projects — 0 at its start, 1 at its end.</summary>
        private static double ProjectOnLine(
            XyPoint lineStart,
            double lineX,
            double lineY,
            double lineLengthSquared,
            double x,
            double y)
        {
            var dx = x - lineStart.X;
            var dy = y - lineStart.Y;
            return (dx * lineX + dy * lineY) / lineLengthSquared;
        }

        /// <summary>
        /// Perpendicular distance from the line, signed: the two sides get opposite signs, which
        /// is all the caller needs (which sign means which side is arbitrary and never asked).
        /// </summary>
        private static double SignedOffset(
            XyPoint lineStart,
            double lineX,
            double lineY,
            double lineLength,
            double x,
            double y)
        {
            var dx = x - lineStart.X;
            var dy = y - lineStart.Y;
            return (dx * lineY - dy * lineX) / lineLength;
        }
    }
}
