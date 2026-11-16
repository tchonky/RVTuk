using System;
using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.TopoTools
{
    /// <summary>A line's points with their internal Z: inside the outline, and on it.</summary>
    public sealed record LinePointsZ(IReadOnlyList<XyzPoint> Interior, IReadOnlyList<XyzPoint> Edge);

    /// <param name="Kept">Each line's points that go to the element, in their original order;
    /// every line given has an entry, empty when it lost every point.</param>
    /// <param name="DroppedForHigher">Points dropped because a higher point stood at their spot.
    /// A duplicate at the same height is dropped too, but not counted.</param>
    public sealed record TopoPointDedupe(IReadOnlyDictionary<long, LinePointsZ> Kept, int DroppedForHigher);

    /// <summary>
    /// One Update's points with every plan spot used once, decided here rather than left to Revit.
    /// Two contours crossing in plan, a line crossing itself, and chained lines meeting on the
    /// outline all put two points on one spot. Handing both to the element would let Revit merge
    /// them however it sees fit, and ledger two records for one vertex — the second then reads as
    /// "moved by hand" on every later run.
    ///
    /// The rule: the highest point at a spot is kept and every other point there is dropped from
    /// its line; at equal heights the first line in the given order keeps it (within a line,
    /// interior points come before edge points). Interior and edge points of all lines are
    /// weighed together. The rule is greedy from the highest point down, so it does not depend on
    /// the order the lines arrive in, except to break ties.
    /// </summary>
    public static class TopoPointDeduper
    {
        /// <summary>
        /// 1 mm, in feet: how close in plan two points are to count as one spot. Revit merges
        /// slab-shape points closer than about its short-curve tolerance (roughly 0.8 mm), so a
        /// tighter test (the ledger's 0.1 mm) would leave pairs between 0.1 and 0.8 mm for Revit to
        /// merge on its own, and the rule would not be ours. 1 mm sits just above that.
        /// </summary>
        public const double SameSpotFeet = 1.0 / 304.8;

        public static TopoPointDedupe Dedupe(IEnumerable<KeyValuePair<long, LinePointsZ>> pointsByLine)
        {
            var lines = pointsByLine.ToList();

            // Every point once, in tie-break order: line by line, interior before edge.
            var all = new List<Candidate>();
            for (int line = 0; line < lines.Count; line++)
            {
                var points = lines[line].Value;
                for (int i = 0; i < points.Interior.Count; i++)
                    all.Add(new Candidate(line, false, points.Interior[i], all.Count));
                for (int i = 0; i < points.Edge.Count; i++)
                    all.Add(new Candidate(line, true, points.Edge[i], all.Count));
            }

            // Highest first; OrderBy is stable, and Order makes the tie-break explicit anyway.
            var kept = new HashSet<int>();
            var grid = new Dictionary<(long, long), List<XyzPoint>>();
            int droppedForHigher = 0;
            foreach (var candidate in all.OrderByDescending(c => c.Point.Z).ThenBy(c => c.Order))
            {
                var keeper = KeptNear(grid, candidate.Point);
                if (keeper != null)
                {
                    if (keeper.Value.Z - candidate.Point.Z > ShapeReplayPlanner.ToleranceFeet) droppedForHigher++;
                    continue;
                }

                kept.Add(candidate.Order);
                var cell = Cell(candidate.Point);
                if (!grid.TryGetValue(cell, out var bucket)) grid[cell] = bucket = new List<XyzPoint>();
                bucket.Add(candidate.Point);
            }

            var result = new Dictionary<long, LinePointsZ>();
            var interior = lines.Select(_ => new List<XyzPoint>()).ToList();
            var edge = lines.Select(_ => new List<XyzPoint>()).ToList();
            foreach (var candidate in all)
            {
                if (!kept.Contains(candidate.Order)) continue;
                (candidate.IsEdge ? edge : interior)[candidate.Line].Add(candidate.Point);
            }
            for (int line = 0; line < lines.Count; line++)
                result[lines[line].Key] = new LinePointsZ(interior[line], edge[line]);

            return new TopoPointDedupe(result, droppedForHigher);
        }

        private readonly record struct Candidate(int Line, bool IsEdge, XyzPoint Point, int Order);

        /// <summary>A grid of <see cref="SameSpotFeet"/> cells: a point within that distance is in
        /// its own cell or one of the eight around it.</summary>
        private static (long, long) Cell(XyzPoint point) =>
            ((long)Math.Floor(point.X / SameSpotFeet), (long)Math.Floor(point.Y / SameSpotFeet));

        private static XyzPoint? KeptNear(Dictionary<(long, long), List<XyzPoint>> grid, XyzPoint point)
        {
            var (cx, cy) = Cell(point);
            for (long dx = -1; dx <= 1; dx++)
            {
                for (long dy = -1; dy <= 1; dy++)
                {
                    if (!grid.TryGetValue((cx + dx, cy + dy), out var bucket)) continue;
                    foreach (var other in bucket)
                    {
                        double x = other.X - point.X;
                        double y = other.Y - point.Y;
                        if (x * x + y * y <= SameSpotFeet * SameSpotFeet) return other;
                    }
                }
            }
            return null;
        }
    }
}
