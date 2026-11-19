using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Core.TopoTools
{
    /// <summary>
    /// What one corner pass does to an element's outline, in internal feet. Every list is sorted by
    /// plan X, then Y, so the plan (and the stored record text) depends only on the corners, never
    /// on the order Revit lists the vertices in.
    ///
    /// <para>A corner left where it is (already within 0.1 mm of its target) is recorded with
    /// <c>SetZ</c> = its current Z. A raised corner's record carries <c>SetZ</c> = the height asked
    /// for. The applier MUST replace it with the Z read back after the raise
    /// (<see cref="WithReached"/>): a record whose <c>SetZ</c> is not where the corner actually sits
    /// reads as "moved by hand" on the next run.</para>
    /// </summary>
    /// <param name="Raises">Corners to move to the height the lines give them.</param>
    /// <param name="PutBacks">Corners no line influences any more, back to their own height.</param>
    /// <param name="NewRecords">The element's corner record after the pass, replacing the old one.</param>
    /// <param name="NewlyByHand">Corners found moved by hand in this pass (flagged now, not before).</param>
    public sealed record CornerPlan(
        IReadOnlyList<(XyzPoint At, double ToZ)> Raises,
        IReadOnlyList<(XyzPoint At, double ToZ)> PutBacks,
        IReadOnlyList<CornerRecord> NewRecords,
        int NewlyByHand)
    {
        /// <summary>
        /// This plan with <c>SetZ</c> of each raised corner's record replaced by the height the
        /// corner reached, keyed by the corner's exact plan XY (a raise's <c>At.X</c>, <c>At.Y</c>, which
        /// its record carries unchanged). Every raise needs an entry — a raise that failed must report
        /// the Z the corner actually has — and a missing one throws <see cref="ArgumentException"/>
        /// naming its XY. Entries for corners that were not raised are ignored.
        /// </summary>
        public CornerPlan WithReached(IReadOnlyDictionary<(double X, double Y), double> reachedZByXy)
        {
            var raised = new HashSet<(double X, double Y)>();
            foreach (var raise in Raises)
            {
                var xy = (raise.At.X, raise.At.Y);
                if (!reachedZByXy.ContainsKey(xy))
                {
                    throw new ArgumentException(
                        "No reached height for the raised corner at X = " +
                        raise.At.X.ToString("R", CultureInfo.InvariantCulture) + ", Y = " +
                        raise.At.Y.ToString("R", CultureInfo.InvariantCulture) + ".",
                        nameof(reachedZByXy));
                }
                raised.Add(xy);
            }

            var records = NewRecords
                .Select(r => !r.ByHand && raised.Contains((r.X, r.Y))
                    ? r with { SetZ = reachedZByXy[(r.X, r.Y)] }
                    : r)
                .ToList();
            return this with { NewRecords = records };
        }
    }

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
        /// Whether a line's edge point is left to the corner pass rather than drawn: an outline
        /// vertex the pass may change stands within 1 mm of it in plan (Revit merges points closer
        /// than about 0.8 mm). Not drawn, the point is not among the placed points either, so that
        /// vertex stays a candidate, and the rule's 1 mm on-line snap gives it the line's height —
        /// recorded, so it goes back when the line goes.
        /// </summary>
        /// <param name="cornerVertices">The vertices the pass may change, before any point is
        /// placed: <see cref="Candidates"/> with no placed points.</param>
        public static bool IsLeftToCornerPass(XyzPoint edgePoint, IReadOnlyList<XyzPoint> cornerVertices) =>
            IsNearInPlan(edgePoint, cornerVertices);

        /// <summary>
        /// For an element whose shape editing is off (a plain floor), whether the pass would raise
        /// any outline point, worked out without switching it on: each point of the loops, at the
        /// element's flat height, through <see cref="Plan"/> with no records (a plain element keeps
        /// none: its shape was never edited, or Reset Shape threw it away). Only when this is true
        /// is the editor switched on, so a floor none of whose corners takes a height from the lines
        /// stays a plain floor.
        /// </summary>
        /// <param name="loopsFeet">The element's outline and holes, tessellated, in plan.</param>
        /// <param name="flatZ">The element's top while it is plain, internal feet.</param>
        public static bool NeedsShapeEditing(
            IReadOnlyList<IReadOnlyList<XyPoint>> loopsFeet, double flatZ, Func<XyzPoint, double?> targetZ)
        {
            var points = new List<XyzPoint>();
            foreach (var loop in loopsFeet)
            {
                foreach (var point in loop) points.Add(new XyzPoint(point.X, point.Y, flatZ));
            }
            return Plan(points, targetZ, Array.Empty<CornerRecord>()).Raises.Count > 0;
        }

        /// <summary>
        /// Decides the pass for each candidate. A record pairs with a candidate within 0.1 mm in
        /// plan: a record stores its corner's live XY exactly, so anything farther is another corner.
        /// Pairs are chosen nearest first, whatever the order of either list.
        ///
        /// <para>A record whose corner is not among <paramref name="candidates"/> is dropped (Edit
        /// Boundary, Reset Shape). So the applier must pass every outline vertex that is not ledgered
        /// and not within 1 mm of a placed point — exactly <see cref="Candidates"/> — and never filter
        /// them further, or it erases the own heights of the corners it left out.</para>
        /// </summary>
        /// <param name="targetZ">The internal Z a candidate should take, or null when no line
        /// influences it; a non-finite value counts as null. Not asked for corners moved by hand.</param>
        public static CornerPlan Plan(
            IReadOnlyList<XyzPoint> candidates,
            Func<XyzPoint, double?> targetZ,
            IReadOnlyList<CornerRecord> records)
        {
            var raises = new List<(XyzPoint At, double ToZ)>();
            var putBacks = new List<(XyzPoint At, double ToZ)>();
            var newRecords = new List<CornerRecord>();
            var recordOf = PairRecords(candidates, records);
            int newlyByHand = 0;

            for (int i = 0; i < candidates.Count; i++)
            {
                var corner = candidates[i];
                var record = recordOf[i];

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
                if (target.HasValue && !double.IsNaN(target.Value) && !double.IsInfinity(target.Value))
                {
                    // A corner already within 0.1 mm of its target stays put and keeps the height
                    // it reached (SetZ = where it is), so a re-run writes the same record. A raised
                    // one gets the target here, which WithReached then replaces.
                    bool raise = Math.Abs(target.Value - corner.Z) > ShapeReplayPlanner.ToleranceFeet;
                    if (raise) raises.Add((corner, target.Value));
                    newRecords.Add(new CornerRecord(
                        corner.X, corner.Y, record?.OwnZ ?? corner.Z, raise ? target.Value : corner.Z, false));
                    continue;
                }

                if (record != null && Math.Abs(record.OwnZ - corner.Z) > ShapeReplayPlanner.ToleranceFeet)
                    putBacks.Add((corner, record.OwnZ));
            }

            return new CornerPlan(
                raises.OrderBy(m => m.At.X).ThenBy(m => m.At.Y).ToList(),
                putBacks.OrderBy(m => m.At.X).ThenBy(m => m.At.Y).ToList(),
                newRecords.OrderBy(r => r.X).ThenBy(r => r.Y).ToList(),
                newlyByHand);
        }

        /// <summary>
        /// For each candidate, its record or null. Every candidate–record pair within 0.1 mm in plan
        /// is sorted by distance, ties broken by the candidate's X, Y, Z and then the record's
        /// fields, and taken greedily, so the result does not depend on either list's order.
        /// </summary>
        private static CornerRecord?[] PairRecords(
            IReadOnlyList<XyzPoint> candidates, IReadOnlyList<CornerRecord> records)
        {
            var pairs = new List<(int Candidate, int Record, double Distance)>();
            for (int c = 0; c < candidates.Count; c++)
            {
                for (int r = 0; r < records.Count; r++)
                {
                    double distance = PlanDistance(candidates[c].X, candidates[c].Y, records[r].X, records[r].Y);
                    if (distance <= ShapeReplayPlanner.ToleranceFeet) pairs.Add((c, r, distance));
                }
            }

            var ordered = pairs
                .OrderBy(p => p.Distance)
                .ThenBy(p => candidates[p.Candidate].X)
                .ThenBy(p => candidates[p.Candidate].Y)
                .ThenBy(p => candidates[p.Candidate].Z)
                .ThenBy(p => records[p.Record].X)
                .ThenBy(p => records[p.Record].Y)
                .ThenBy(p => records[p.Record].OwnZ)
                .ThenBy(p => records[p.Record].SetZ)
                .ThenBy(p => records[p.Record].ByHand);

            var recordOf = new CornerRecord?[candidates.Count];
            var taken = new bool[records.Count];
            foreach (var pair in ordered)
            {
                if (recordOf[pair.Candidate] != null || taken[pair.Record]) continue;
                recordOf[pair.Candidate] = records[pair.Record];
                taken[pair.Record] = true;
            }
            return recordOf;
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
