using System;
using System.Collections.Generic;

namespace RVTuk.Core.TopoTools
{
    /// <summary>A vertex of the new element to raise or lower, by its index in that element's
    /// vertex list, relative to where it sits now.</summary>
    public readonly record struct VertexOffset(int TargetIndex, double Offset);

    /// <summary>A split line drawn by the user, by the positions of its two ends.</summary>
    public readonly record struct CreaseEnds(XyzPoint Start, XyzPoint End);

    /// <summary>A split line to draw, by the indices of its two end vertices.</summary>
    public readonly record struct VertexPair(int StartIndex, int EndIndex);

    public sealed record ShapeReplayPlan(
        IReadOnlyList<VertexOffset> Offsets, IReadOnlyList<XyzPoint> Additions);

    public sealed record CreaseReplayPlan(IReadOnlyList<VertexPair> Pairs, int Dropped);

    /// <summary>
    /// How to give a freshly created, flat floor or toposolid the top surface of the element it
    /// replaces. Both have the same outline, so every corner of the source already exists on the
    /// target: those are raised or lowered in place (<c>ModifySubElement</c>). Everything else
    /// (interior points, points on an edge) is added at its absolute position (<c>AddPoints</c>).
    ///
    /// Vertices are matched by XY alone, within the same 0.1 mm the Topo Tools point ledger
    /// matches with, so a round trip leaves the ledger still pointing at real vertices.
    /// </summary>
    public static class ShapeReplayPlanner
    {
        /// <summary>0.1 mm, in Revit's internal feet.</summary>
        public const double ToleranceFeet = 0.1 / 304.8;

        public static ShapeReplayPlan Plan(IReadOnlyList<XyzPoint> source, IReadOnlyList<XyzPoint> target)
        {
            var offsets = new List<VertexOffset>();
            var additions = new List<XyzPoint>();
            var matchedTargets = new HashSet<int>();

            foreach (var point in source)
            {
                int index = IndexOfXy(target, point);
                if (index >= 0)
                {
                    // A second source vertex on the same corner has nowhere to go.
                    if (!matchedTargets.Add(index)) continue;

                    double offset = point.Z - target[index].Z;
                    if (Math.Abs(offset) > ToleranceFeet) offsets.Add(new VertexOffset(index, offset));
                    continue;
                }

                // AddPoints rejects the whole batch if two points share an XY, so the first wins
                // and the rest are dropped here rather than losing every point there.
                if (IndexOfXy(additions, point) >= 0) continue;
                additions.Add(point);
            }

            return new ShapeReplayPlan(offsets, additions);
        }

        /// <summary>
        /// Maps each user-drawn split line onto the vertices that exist once the points are in.
        /// A line whose end has no vertex, or whose two ends land on one vertex, cannot be drawn:
        /// it is counted rather than guessed at.
        /// </summary>
        public static CreaseReplayPlan PlanCreases(
            IReadOnlyList<XyzPoint> live, IReadOnlyList<CreaseEnds> creases)
        {
            var pairs = new List<VertexPair>();
            int dropped = 0;

            foreach (var crease in creases)
            {
                int start = IndexOfXy(live, crease.Start);
                int end = IndexOfXy(live, crease.End);
                if (start < 0 || end < 0 || start == end)
                {
                    dropped++;
                    continue;
                }
                pairs.Add(new VertexPair(start, end));
            }

            return new CreaseReplayPlan(pairs, dropped);
        }

        /// <summary>The first point within tolerance of <paramref name="point"/> in plan, else -1.</summary>
        public static int IndexOfXy(IReadOnlyList<XyzPoint> points, XyzPoint point)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (Math.Abs(points[i].X - point.X) <= ToleranceFeet &&
                    Math.Abs(points[i].Y - point.Y) <= ToleranceFeet)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
