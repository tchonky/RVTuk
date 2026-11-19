using System;
using System.Collections.Generic;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// What one corner pass does to an element's outline, in internal feet.
    /// </summary>
    /// <param name="Raises">Corners to move to the height the lines give them.</param>
    /// <param name="PutBacks">Corners no line influences any more, back to their own height.</param>
    /// <param name="NewRecords">The element's corner record after the pass, replacing the old one.</param>
    /// <param name="NewlyByHand">Corners found moved by hand in this pass (flagged now, not before).</param>
    public sealed record CornerPlan(
        IReadOnlyList<(XyzPoint At, double ToZ)> Raises,
        IReadOnlyList<(XyzPoint At, double ToZ)> PutBacks,
        IReadOnlyList<CornerRecord> NewRecords,
        int NewlyByHand);

    /// <summary>
    /// The outline corner pass of a Topo Tools Update, without Revit: which vertices of the edited
    /// toposolid or floor it may touch, and, given the height each should take (rule N, or none),
    /// which to raise, which to put back to their own height, and what the element remembers.
    ///
    /// Heights never come from the corners' current Z; the current Z is read only to see whether a
    /// corner already sits at its target and whether the user moved it by hand. So running the pass
    /// again on its own result changes nothing.
    /// </summary>
    public static class OutlineCornerPlanner
    {
        /// <summary>
        /// The vertices the pass may change: within 1 mm of an edge of any loop (outline or hole),
        /// not a point the ledger records for any line (X, Y and Z each within 0.1 mm, as the
        /// applier matches), and not within 1 mm in plan of a point this run placed.
        /// </summary>
        /// <param name="loopsFeet">The element's outline and holes, tessellated, in plan.</param>
        /// <param name="ledgered">Every point the element's ledger records, for any line or view.</param>
        /// <param name="placedPoints">The points this run actually placed as new vertices (interior
        /// points and drawn edge points), not every point of the lines. An edge point that was not
        /// placed because it fell within 1 mm of an outline vertex is left out on purpose, so that
        /// vertex stays a candidate and takes the line's height through the rule.</param>
        public static IReadOnlyList<XyzPoint> Candidates(
            IReadOnlyList<XyzPoint> vertices,
            IReadOnlyList<IReadOnlyList<XyPoint>> loopsFeet,
            IReadOnlyList<XyzPoint> ledgered,
            IReadOnlyList<XyzPoint> placedPoints)
        {
            var candidates = new List<XyzPoint>();
            foreach (var vertex in vertices)
            {
                if (!PolygonContainment.IsNearEdge(
                        loopsFeet, new XyPoint(vertex.X, vertex.Y), ShapeReplayPlanner.EdgeBandFeet))
                    continue;
                if (IsLedgered(vertex, ledgered)) continue;
                if (IsNearInPlan(vertex, placedPoints)) continue;
                candidates.Add(vertex);
            }
            return candidates;
        }

        /// <summary>
        /// Decides the pass for each candidate. Records are matched to candidates by plan XY within
        /// 1 mm; a record without a candidate (Edit Boundary, Reset Shape) is dropped.
        /// </summary>
        /// <param name="targetZ">The internal Z a candidate should take, or null when no line
        /// influences it. Not asked for corners moved by hand.</param>
        public static CornerPlan Plan(
            IReadOnlyList<XyzPoint> candidates,
            Func<XyzPoint, double?> targetZ,
            IReadOnlyList<CornerRecord> records)
        {
            var raises = new List<(XyzPoint At, double ToZ)>();
            var putBacks = new List<(XyzPoint At, double ToZ)>();
            var newRecords = new List<CornerRecord>();
            var used = new bool[records.Count];
            int newlyByHand = 0;

            foreach (var corner in candidates)
            {
                var record = TakeRecord(corner, records, used);

                if (record != null && record.ByHand)
                {
                    newRecords.Add(record);
                    continue;
                }

                if (record != null && Math.Abs(corner.Z - record.SetZ) > ShapeReplayPlanner.ToleranceFeet)
                {
                    newRecords.Add(record with { ByHand = true });
                    newlyByHand++;
                    continue;
                }

                var target = targetZ(corner);
                if (target.HasValue)
                {
                    if (Math.Abs(target.Value - corner.Z) > ShapeReplayPlanner.ToleranceFeet)
                        raises.Add((corner, target.Value));
                    newRecords.Add(new CornerRecord(
                        corner.X, corner.Y, record?.OwnZ ?? corner.Z, target.Value, false));
                    continue;
                }

                if (record != null && Math.Abs(record.OwnZ - corner.Z) > ShapeReplayPlanner.ToleranceFeet)
                    putBacks.Add((corner, record.OwnZ));
            }

            return new CornerPlan(raises, putBacks, newRecords, newlyByHand);
        }

        /// <summary>The nearest unused record within 1 mm of the corner in plan, marked used.</summary>
        private static CornerRecord? TakeRecord(XyzPoint corner, IReadOnlyList<CornerRecord> records, bool[] used)
        {
            int best = -1;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < records.Count; i++)
            {
                if (used[i]) continue;
                double distance = PlanDistance(corner.X, corner.Y, records[i].X, records[i].Y);
                if (distance <= ShapeReplayPlanner.EdgeBandFeet && distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            if (best < 0) return null;
            used[best] = true;
            return records[best];
        }

        private static bool IsLedgered(XyzPoint vertex, IReadOnlyList<XyzPoint> ledgered)
        {
            foreach (var point in ledgered)
            {
                if (Math.Abs(point.X - vertex.X) <= ShapeReplayPlanner.ToleranceFeet &&
                    Math.Abs(point.Y - vertex.Y) <= ShapeReplayPlanner.ToleranceFeet &&
                    Math.Abs(point.Z - vertex.Z) <= ShapeReplayPlanner.ToleranceFeet)
                    return true;
            }
            return false;
        }

        private static bool IsNearInPlan(XyzPoint vertex, IReadOnlyList<XyzPoint> points)
        {
            foreach (var point in points)
            {
                if (PlanDistance(vertex.X, vertex.Y, point.X, point.Y) <= ShapeReplayPlanner.EdgeBandFeet)
                    return true;
            }
            return false;
        }

        private static double PlanDistance(double ax, double ay, double bx, double by)
        {
            double dx = ax - bx;
            double dy = ay - by;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
