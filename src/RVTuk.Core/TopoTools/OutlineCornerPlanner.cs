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

    /// <summary>A corner pass as carried out (<see cref="OutlineCornerPlanner.Settle"/>), in internal feet.</summary>
    /// <param name="Records">The element's corner record after the pass, sorted by X, then Y.</param>
    /// <param name="Set">Corners that hold a height the lines gave them after this run, raised now
    /// or earlier (records not flagged by hand, less the raises that ended off their height).</param>
    /// <param name="PutBack">Corners put back to their own height in this run.</param>
    /// <param name="Lost">Raises and put-backs that ended more than 0.1 mm off their height.</param>
    /// <param name="Changed">Corners this run actually moved, by more than 0.1 mm.</param>
    public sealed record CornerOutcome(IReadOnlyList<CornerRecord> Records, int Set, int PutBack, int Lost, int Changed);

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
        /// any outline corner, worked out without switching it on: each corner, at the element's
        /// flat height, through <see cref="Plan"/> with no records (a plain element keeps none: its
        /// shape was never edited, or Reset Shape threw it away). Only when this is true is the
        /// editor switched on, so a floor none of whose corners takes a height from the lines stays
        /// a plain floor.
        /// </summary>
        /// <param name="outlineCornersFeet">The outline's and holes' real corners in plan (each
        /// sketch curve's start), not their tessellation: an arc's inner points are no vertices.</param>
        /// <param name="flatZ">The element's top while it is plain, internal feet.</param>
        public static bool NeedsShapeEditing(
            IReadOnlyList<XyPoint> outlineCornersFeet, double flatZ, Func<XyzPoint, double?> targetZ)
        {
            var points = outlineCornersFeet.Select(point => new XyzPoint(point.X, point.Y, flatZ)).ToList();
            return Plan(points, targetZ, Array.Empty<CornerRecord>()).Raises.Count > 0;
        }

        /// <summary>
        /// <paramref name="candidates"/> without those at one of <paramref name="spots"/> in plan (X
        /// and Y each within 0.1 mm): vertices kept out of this run's pass only, with no record. A
        /// ledgered point that Revit would not delete matched in X, Y and Z, so nobody moved it: it
        /// must not be raised as a corner in the run whose summary calls it left alone, nor flagged
        /// by hand for good. Unledgered from now on, it is an ordinary candidate on the next run. No
        /// record is lost: a ledgered vertex was never a candidate, so it has none.
        /// </summary>
        public static IReadOnlyList<XyzPoint> ExceptAt(IReadOnlyList<XyzPoint> candidates, IReadOnlyList<XyzPoint> spots)
        {
            if (spots.Count == 0) return candidates;
            return candidates.Where(candidate => ShapeReplayPlanner.IndexOfXy(spots, candidate) < 0).ToList();
        }

        /// <summary>
        /// The line points the user moved by hand, as corner records flagged <c>hand</c>. A ledgered
        /// point with no vertex at its X, Y and Z, but a live vertex at its plan spot (X and Y each
        /// within 0.1 mm), was moved up or down by hand: the stale-line cleanup reports it and drops
        /// its ledger entry, which would make it an unledgered outline vertex the pass may raise. Its
        /// record (own and set height = where it now stands) keeps the pass off it from now on.
        /// One record per vertex. A vertex inside the outline gets a record too, which
        /// <see cref="Plan"/> drops, since only candidates keep theirs.
        /// </summary>
        /// <param name="unmatched">The ledgered points the cleanup could not take out.</param>
        /// <param name="vertices">The live vertices after the cleanup.</param>
        public static IReadOnlyList<CornerRecord> HandMovedRecords(
            IReadOnlyList<XyzPoint> unmatched, IReadOnlyList<XyzPoint> vertices)
        {
            var found = new HashSet<int>();
            foreach (var point in unmatched)
            {
                int index = ShapeReplayPlanner.IndexOfXy(vertices, point);
                if (index >= 0) found.Add(index);
            }

            return found
                .Select(i => new CornerRecord(vertices[i].X, vertices[i].Y, vertices[i].Z, vertices[i].Z, true))
                .OrderBy(r => r.X).ThenBy(r => r.Y)
                .ToList();
        }

        /// <summary>
        /// The plan as the applier carried it out, given the Z each raised and each put-back corner
        /// was read back at (keyed by its exact plan XY, as <see cref="CornerPlan.WithReached"/>):
        /// the records to store and what to report. A raise or put-back more than 0.1 mm off its
        /// height is lost; a put-back that is lost keeps its record (own height, set height = where
        /// it stands) so the next Update tries again rather than forgetting the own height. Throws
        /// <see cref="ArgumentException"/> when a raise or put-back has no reached height.
        /// </summary>
        public static CornerOutcome Settle(
            CornerPlan plan,
            IReadOnlyDictionary<(double X, double Y), double> raisedZByXy,
            IReadOnlyDictionary<(double X, double Y), double> putBackZByXy)
        {
            var records = plan.WithReached(raisedZByXy).NewRecords.ToList();

            int lostRaises = 0;
            int changed = 0;
            foreach (var raise in plan.Raises)
            {
                double z = raisedZByXy[(raise.At.X, raise.At.Y)];
                if (Math.Abs(z - raise.ToZ) > ShapeReplayPlanner.ToleranceFeet) lostRaises++;
                if (Math.Abs(z - raise.At.Z) > ShapeReplayPlanner.ToleranceFeet) changed++;
            }

            // Every record not flagged by hand holds a height the lines gave it this run.
            int set = records.Count(r => !r.ByHand) - lostRaises;

            int putBack = 0;
            int lostPutBacks = 0;
            foreach (var back in plan.PutBacks)
            {
                if (!putBackZByXy.TryGetValue((back.At.X, back.At.Y), out double z))
                {
                    throw new ArgumentException(
                        "No reached height for the corner put back at X = " +
                        back.At.X.ToString("R", CultureInfo.InvariantCulture) + ", Y = " +
                        back.At.Y.ToString("R", CultureInfo.InvariantCulture) + ".",
                        nameof(putBackZByXy));
                }

                if (Math.Abs(z - back.At.Z) > ShapeReplayPlanner.ToleranceFeet) changed++;
                if (Math.Abs(z - back.ToZ) <= ShapeReplayPlanner.ToleranceFeet)
                {
                    putBack++;
                    continue;
                }

                lostPutBacks++;
                records.Add(new CornerRecord(back.At.X, back.At.Y, back.ToZ, z, false));
            }

            return new CornerOutcome(
                records.OrderBy(r => r.X).ThenBy(r => r.Y).ToList(),
                set, putBack, lostRaises + lostPutBacks, changed);
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

                // Back at its own height but not at the one set: Reset Shape (or an undo outside the
                // session) put it back, which is not a hand edit. The record goes; the corner is
                // planned as a new one.
                if (record != null && !record.ByHand &&
                    Math.Abs(corner.Z - record.SetZ) > ShapeReplayPlanner.ToleranceFeet &&
                    Math.Abs(corner.Z - record.OwnZ) <= ShapeReplayPlanner.ToleranceFeet)
                {
                    record = null;
                }

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
        /// Cancel's corner step: what puts back the corners the session moved, from the record taken
        /// when it started (<paramref name="atStart"/>) and the record now (<paramref name="now"/>).
        /// Lines are not read and the rule is not run, so no corner the session left alone moves.
        ///
        /// <para>A corner whose record changed during the session — the start had none, the session
        /// dropped it (a put-back), or its set height differs by more than 0.1 mm — goes back to the
        /// start's set height (in <see cref="CornerPlan.Raises"/>, at the start record's plan spot,
        /// so <see cref="CornerPlan.WithReached"/> keys its record) or, when the start had no record,
        /// to its own height (in <see cref="CornerPlan.PutBacks"/>). One already within 0.1 mm of
        /// that height is not moved. A corner flagged by hand, now or at the start, is left alone.
        /// A corner whose record did not change is not touched, even off its set height (moved by
        /// hand since the last Update, which that Update flags).</para>
        ///
        /// <para>The records become the start's, except that a hand flag set during the session
        /// stays. A start record whose corner is gone (Edit Boundary) comes back with nothing moved;
        /// the next Update drops it. Records pair with each other and with
        /// <paramref name="vertices"/> within 0.1 mm in plan, as in <see cref="Plan"/>, whatever the
        /// order of any list. Run the result through <see cref="Settle"/>.</para>
        /// </summary>
        /// <param name="vertices">Every live vertex of the element.</param>
        public static CornerPlan Restore(
            IReadOnlyList<CornerRecord> atStart,
            IReadOnlyList<CornerRecord> now,
            IReadOnlyList<XyzPoint> vertices)
        {
            var records = new List<CornerRecord>();
            var wanted = new List<(CornerRecord Spot, bool ToOwn)>();

            var nowPoints = now.Select(r => new XyzPoint(r.X, r.Y, r.SetZ)).ToList();
            var startIndexOf = PairIndices(nowPoints, atStart);
            var startTaken = new bool[atStart.Count];

            for (int j = 0; j < now.Count; j++)
            {
                var current = now[j];
                var start = startIndexOf[j] >= 0 ? atStart[startIndexOf[j]] : null;
                if (startIndexOf[j] >= 0) startTaken[startIndexOf[j]] = true;

                if (current.ByHand)
                {
                    records.Add(start != null && start.ByHand ? start : current);
                    continue;
                }

                if (start != null)
                {
                    records.Add(start);
                    if (!start.ByHand && Math.Abs(current.SetZ - start.SetZ) > ShapeReplayPlanner.ToleranceFeet)
                        wanted.Add((start, false));
                    continue;
                }

                wanted.Add((current with { SetZ = current.OwnZ }, true));
            }

            // Start records the session dropped: put back by an Update, or their corner gone.
            for (int i = 0; i < atStart.Count; i++)
            {
                if (startTaken[i]) continue;
                records.Add(atStart[i]);
                if (!atStart[i].ByHand) wanted.Add((atStart[i], false));
            }

            var raises = new List<(XyzPoint At, double ToZ)>();
            var putBacks = new List<(XyzPoint At, double ToZ)>();
            var wantedOf = PairIndices(vertices, wanted.Select(w => w.Spot).ToList());
            for (int v = 0; v < vertices.Count; v++)
            {
                if (wantedOf[v] < 0) continue;
                var (spot, toOwn) = wanted[wantedOf[v]];
                var vertex = vertices[v];
                if (Math.Abs(vertex.Z - spot.SetZ) <= ShapeReplayPlanner.ToleranceFeet) continue;

                if (toOwn) putBacks.Add((vertex, spot.SetZ));
                else raises.Add((new XyzPoint(spot.X, spot.Y, vertex.Z), spot.SetZ));
            }

            return new CornerPlan(
                raises.OrderBy(m => m.At.X).ThenBy(m => m.At.Y).ToList(),
                putBacks.OrderBy(m => m.At.X).ThenBy(m => m.At.Y).ToList(),
                records.OrderBy(r => r.X).ThenBy(r => r.Y).ToList(),
                0);
        }

        /// <summary>
        /// The corner record a session starts from, as <see cref="Restore"/> must read it: the stored
        /// record checked against where the corners really stand, by <see cref="Plan"/>'s own stale
        /// rules, so Cancel never raises a corner to a height it did not have at the start. With
        /// shape editing off there is none (a plain element). A record pairs with a vertex within
        /// 0.1 mm in plan, nearest first; one with no vertex (Edit Boundary) is left out. A record
        /// flagged by hand is kept as it is. Any other record is kept while its corner stands within
        /// 0.1 mm of its set height; one whose corner stands at its own height instead was reset
        /// (Reset Shape) and is left out; one whose corner stands elsewhere was moved by hand and
        /// is kept flagged by hand, with its set height where the corner stands, so Cancel leaves
        /// it there. Sorted by X, then Y.
        /// </summary>
        /// <param name="vertices">Every live vertex of the element at the start.</param>
        public static IReadOnlyList<CornerRecord> EffectiveStart(
            IReadOnlyList<CornerRecord> stored, bool editorEnabled, IReadOnlyList<XyzPoint> vertices)
        {
            if (!editorEnabled) return Array.Empty<CornerRecord>();

            var records = new List<CornerRecord>();
            var recordOf = PairIndices(vertices, stored);
            for (int v = 0; v < vertices.Count; v++)
            {
                if (recordOf[v] < 0) continue;
                var record = stored[recordOf[v]];
                double z = vertices[v].Z;

                if (record.ByHand || Math.Abs(z - record.SetZ) <= ShapeReplayPlanner.ToleranceFeet)
                    records.Add(record);
                else if (Math.Abs(z - record.OwnZ) > ShapeReplayPlanner.ToleranceFeet)
                    records.Add(record with { SetZ = z, ByHand = true });
            }

            return records.OrderBy(r => r.X).ThenBy(r => r.Y).ToList();
        }

        /// <summary>
        /// Whether Cancel has to run <see cref="Restore"/>. Not when the record text now is the one
        /// stored at the start and that record was not stale then (<paramref name="cornerStart"/>,
        /// the <see cref="EffectiveStart"/>, encodes to the same text): nothing moved. A stale start
        /// record written again, bit for bit, by an Update that raised its corners once more must
        /// still be restored; Restore plans no move where nothing moved.
        /// </summary>
        public static bool CancelHasCornerWork(
            string rawNow, string cornerSnapshot, IReadOnlyList<CornerRecord> cornerStart) =>
            rawNow != cornerSnapshot || OutlineCornerRecordCodec.Encode(cornerStart) != cornerSnapshot;

        /// <summary>
        /// The vertices within 1 mm of an edge of any loop (outline or hole), the band
        /// <see cref="Candidates"/> uses: the only ones a corner record can belong to. For
        /// <see cref="EffectiveStart"/> and <see cref="Restore"/>, so a survey toposolid's thousands
        /// of inner points are not paired against the records.
        /// </summary>
        public static IReadOnlyList<XyzPoint> OnOutline(
            IReadOnlyList<XyzPoint> vertices, IReadOnlyList<IReadOnlyList<XyPoint>> loopsFeet) =>
            vertices
                .Where(vertex => PolygonContainment.IsNearEdge(
                    loopsFeet, new XyPoint(vertex.X, vertex.Y), ShapeReplayPlanner.EdgeBandFeet))
                .ToList();

        /// <summary>
        /// For each candidate, its record or null. Every candidate–record pair within 0.1 mm in plan
        /// is sorted by distance, ties broken by the candidate's X, Y, Z and then the record's
        /// fields, and taken greedily, so the result does not depend on either list's order.
        /// </summary>
        private static CornerRecord?[] PairRecords(
            IReadOnlyList<XyzPoint> candidates, IReadOnlyList<CornerRecord> records) =>
            PairIndices(candidates, records)
                .Select(index => index >= 0 ? records[index] : null)
                .ToArray();

        /// <summary><see cref="PairRecords"/> as indices into <paramref name="records"/>, -1 for none.</summary>
        private static int[] PairIndices(
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

            var recordOf = Enumerable.Repeat(-1, candidates.Count).ToArray();
            var taken = new bool[records.Count];
            foreach (var pair in ordered)
            {
                if (recordOf[pair.Candidate] >= 0 || taken[pair.Record]) continue;
                recordOf[pair.Candidate] = pair.Record;
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
