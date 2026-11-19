using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>What one run did to its target's points.</summary>
    /// <param name="Added">Points placed at their height.</param>
    /// <param name="Removed">Earlier points of re-applied or deleted lines, taken out.</param>
    /// <param name="Missing">Earlier points no longer found where recorded: moved by hand.</param>
    /// <param name="Lost">Points that could not be placed, or not at their height — outline corners
    /// the corner pass could not bring within 0.1 mm of their height included.</param>
    /// <param name="CornersSet">Outline corners that hold a height the lines gave them after this
    /// run, raised now or by an earlier Update (<see cref="CornerOutcome.Set"/>).</param>
    /// <param name="CornersPutBack">Outline corners no line influences any more, put back to their own height in this run.</param>
    /// <param name="CornersByHand">Outline corners found moved by hand in this run, flagged and left alone.</param>
    public sealed record ApplyOutcome(
        int Added, int Removed, int Missing, int Lost,
        int CornersSet = 0, int CornersPutBack = 0, int CornersByHand = 0);

    /// <summary>
    /// Writes one toposolid's or floor's points, and the only place that copes with a point having
    /// no identity: <c>SlabShapeVertex</c> is a position, not an element, so the ledger stores
    /// coordinates and a re-run matches them back within a tenth of a millimetre.
    ///
    /// The positions recorded are the ones Revit reports after placing, not the ones asked for, so
    /// snapping or rounding inside Revit cannot drift the ledger away from the model. A recorded
    /// point with no vertex within tolerance was moved or deleted by hand since the last run: it is
    /// left alone and counted, never guessed at.
    ///
    /// Interior points go through <c>AddPoints</c>, which takes an absolute Z. Points on the
    /// outline cannot (one such point fails the whole batch), so each is drawn with the deprecated
    /// <c>DrawPoint</c> on the current surface and then raised with <c>ModifySubElement</c> — whose
    /// offset is measured from the element's unshaped top, not from the vertex (probe, Revit 2024).
    ///
    /// After the line points comes the outline corner pass (spec 2026-11-19): every outline vertex
    /// the tool did not place takes the height the lines give it (<see cref="OutlineCornerHeights"/>),
    /// or goes back to its own height when no line influences it any more, remembered in
    /// <see cref="OutlineCornerStore"/>. <see cref="OutlineCornerPlanner"/> decides; this class only
    /// raises.
    /// </summary>
    public static class TopoPointApplier
    {
        /// <summary>0.1 mm, in Revit's internal feet.</summary>
        public const double MatchToleranceFeet = 0.1 / 304.8;

        /// <summary>
        /// Caller must already be inside a transaction. <paramref name="newPointsByLine"/> must
        /// put at most one point on any plan spot (<see cref="TopoPointDeduper"/>): every point
        /// given is placed, except an edge point within 1 mm of an outline vertex the corner pass
        /// may change, which is left to that pass.
        /// </summary>
        /// <param name="corners">The corner pass's inputs; null runs no corner pass at all (Cancel,
        /// which collects no lines).</param>
        public static ApplyOutcome Apply(
            SlabTarget target,
            IReadOnlyCollection<long> lineIdsInView,
            IReadOnlyDictionary<long, LinePointsZ> newPointsByLine,
            Func<long, bool> lineStillExists,
            CornerPass? corners)
        {
            var element = target.Element;
            var doc = element.Document;

            var ledger = new Dictionary<long, IReadOnlyList<XyzPoint>>();
            foreach (var entry in TopoLedgerStore.Read(element)) ledger[entry.Key] = entry.Value;

            var editor = SlabShape.EditorOf(element);
            if (editor == null) return new ApplyOutcome(0, 0, 0, newPointsByLine.Values.Sum(Count));

            // Every line of the view, plus every line since deleted. Not only the lines producing
            // points: a line moved off the element, or whose height became unreadable, must lose
            // last run's points too. Ledger lines of other views are left alone.
            var inView = new HashSet<long>(lineIdsInView);
            var staleLineIds = ledger.Keys
                .Where(lineId => inView.Contains(lineId) || !lineStillExists(lineId))
                .ToList();
            bool lineWork = staleLineIds.Count > 0 || newPointsByLine.Values.Sum(Count) > 0;

            // A plain element (shape editing off) keeps no corner record that still means anything:
            // its shape was never edited, or Reset Shape threw it away. Its old records are dropped.
            bool wasEnabled = editor.IsEnabled;
            var records = wasEnabled ? OutlineCornerStore.Read(element) : Array.Empty<CornerRecord>();

            // Nothing to add and nothing to take out: leave the element as it is unless the corner
            // pass has work, so a floor none of whose corners takes a height from the lines is not
            // switched to shape editing. For a plain element that is worked out from its outline's
            // corners at its flat top before anything is switched on (ruling R2); with no flat top
            // to read, a rule counts as work.
            var start = CornerPassGate.Start(lineWork, corners, wasEnabled, records.Count, () =>
            {
                double? flatZ = FlatTop(element);
                return flatZ == null ||
                    OutlineCornerPlanner.NeedsShapeEditing(target.Corners, flatZ.Value, corners!.TargetZ);
            });
            if (start == CornerPassStart.Skip) return new ApplyOutcome(0, 0, 0, 0);
            if (start == CornerPassStart.DropRecords)
            {
                WriteCorners(element, Array.Empty<CornerRecord>());
                return new ApplyOutcome(0, 0, 0, 0);
            }

            if (!wasEnabled)
            {
                editor.Enable();
                doc.Regenerate();
            }

            int removed = 0;
            int missing = 0;
            var undeletable = new List<XyzPoint>();
            var keptOut = new List<XyzPoint>();

            if (staleLineIds.Count > 0)
            {
                var live = new List<SlabShapeVertex>();
                foreach (SlabShapeVertex vertex in editor.SlabShapeVertices) live.Add(vertex);

                foreach (var lineId in staleLineIds)
                {
                    foreach (var recorded in ledger[lineId])
                    {
                        var match = FindVertex(live, recorded);
                        if (match == null)
                        {
                            // Moved by hand (or deleted): counted once, kept out of this run's
                            // corner pass only, no record (see OutlineCornerPlanner.ExceptAt).
                            missing++;
                            keptOut.Add(recorded);
                            continue;
                        }

                        live.Remove(match);
                        if (match.IsValidObject && editor.DeletePoint(match))
                        {
                            removed++;
                        }
                        else
                        {
                            // Found where recorded, so nobody moved it: kept out of this run's
                            // corner pass only, no record (see OutlineCornerPlanner.ExceptAt).
                            missing++;
                            undeletable.Add(recorded);
                            keptOut.Add(recorded);
                        }
                    }
                    ledger.Remove(lineId);
                }
                doc.Regenerate();
            }

            // The ledger records only points this run created. A vertex that already stood at a
            // point's plan position before the run (a point added by hand, a point of a line in
            // another view) is raised or reused but never ledgered: otherwise a later run would
            // delete a vertex this tool does not own, or report it "moved by hand" on every run
            // because Revit merged our point into it. An outline vertex the corner pass may change
            // is not even raised here: that pass raises and records it (below).
            var preexisting = new List<XyzPoint>();
            foreach (SlabShapeVertex vertex in editor.SlabShapeVertices)
                preexisting.Add(SlabShape.ToPoint(vertex.Position));

            // Those outline vertices, as they stand before any point is placed. Needed only to test
            // edge points against. A point Revit would not delete is left out, so its line's edge
            // point reuses it; one moved by hand stays in, so the edge point there is not drawn
            // and the vertex stays where the user put it in this run.
            IReadOnlyList<XyzPoint> cornerVertices = Array.Empty<XyzPoint>();
            if (corners != null && newPointsByLine.Values.Any(points => points.Edge.Count > 0))
            {
                cornerVertices = OutlineCornerPlanner.ExceptAt(
                    OutlineCornerPlanner.Candidates(
                        preexisting, target.Loops, LedgeredPoints(ledger), Array.Empty<XyzPoint>()),
                    undeletable);
            }

            int added = 0;
            int lost = 0;
            var recordedByLine = new Dictionary<long, List<XyzPoint>>();

            // Interior points first, one batch per line.
            foreach (var entry in newPointsByLine)
            {
                var recorded = new List<XyzPoint>();
                recordedByLine[entry.Key] = recorded;
                if (entry.Value.Interior.Count == 0) continue;

                var result = AddInterior(editor, entry.Value.Interior, preexisting, recorded);
                added += result.Created;
                lost += result.Lost;
            }

            // Then the edge points, each on the surface as it now stands. The reference that
            // ModifySubElement measures from is learnt on the first raise and reused.
            double? reference = null;
            foreach (var entry in newPointsByLine)
            {
                foreach (var point in entry.Value.Edge)
                {
                    // An outline vertex within 1 mm: not drawn (Revit would merge the two) and not
                    // a placed point, so the corner pass gives that vertex the line's height through
                    // the rule's 1 mm snap and remembers it (ruling R1).
                    if (OutlineCornerPlanner.IsLeftToCornerPass(point, cornerVertices)) continue;

                    int index = ShapeReplayPlanner.IndexOfXy(preexisting, point);
                    XyzPoint? existing = index >= 0 ? preexisting[index] : null;

                    var placed = PlaceEdgePoint(doc, element, editor, point, existing, ref reference);
                    if (placed == null)
                    {
                        lost++;
                        continue;
                    }

                    // A new point is recorded even when off its height: it is there, it belongs
                    // to this line, and the next run must be able to take it back out. A vertex
                    // that was already there is not (see above).
                    if (existing == null) recordedByLine[entry.Key].Add(placed.Value);
                    if (Math.Abs(placed.Value.Z - point.Z) <= MatchToleranceFeet) added++;
                    else lost++;
                }
            }

            foreach (var entry in recordedByLine)
            {
                if (entry.Value.Count > 0) ledger[entry.Key] = entry.Value;
            }

            int cornersSet = 0;
            int cornersPutBack = 0;
            int cornersByHand = 0;
            if (corners != null)
            {
                // The points this run placed as new vertices are exactly the ones just recorded.
                var placedPoints = recordedByLine.Values.SelectMany(points => points).ToList();
                var (outcome, newlyByHand) = RunCornerPass(
                    doc, target, editor, corners, ledger, placedPoints, keptOut, records, ref reference);
                cornersSet = outcome.Set;
                cornersPutBack = outcome.PutBack;
                cornersByHand = newlyByHand;
                lost += outcome.Lost;

                // Switched on for the pass alone, which then changed no corner (the dry run is only
                // a guess at the flat top): a plain floor again, keeping no record and holding no
                // corner at a line height.
                if (CornerPassGate.ResetToPlain(wasEnabled, lineWork, element is Floor, outcome.Changed))
                {
                    editor.ResetSlabShape();
                    WriteCorners(element, Array.Empty<CornerRecord>());
                    cornersSet = 0;
                }
            }

            if (lineWork) TopoLedgerStore.Write(element, ledger);
            return new ApplyOutcome(added, removed, missing, lost, cornersSet, cornersPutBack, cornersByHand);
        }

        /// <summary>
        /// The corner pass, after every line point is placed: read the live vertices, let the
        /// planner decide, and carry the plan out (<see cref="CarryOut"/>) in the same transaction. Nothing is retried in this run.
        /// </summary>
        private static (CornerOutcome Outcome, int NewlyByHand) RunCornerPass(
            Document doc, SlabTarget target, SlabShapeEditor editor, CornerPass corners,
            IReadOnlyDictionary<long, IReadOnlyList<XyzPoint>> ledger, IReadOnlyList<XyzPoint> placedPoints,
            IReadOnlyList<XyzPoint> keptOut, IReadOnlyList<CornerRecord> records, ref double? reference)
        {
            var element = target.Element;
            doc.Regenerate();

            var vertices = new List<XyzPoint>();
            foreach (SlabShapeVertex vertex in editor.SlabShapeVertices)
                vertices.Add(SlabShape.ToPoint(vertex.Position));

            // Every candidate goes to the plan, filtered no further (a record whose corner is not
            // among them is dropped) — except the stale lines' points the cleanup did not take out
            // (Revit would not delete them, or they were moved by hand), which have no record to lose.
            var candidates = OutlineCornerPlanner.ExceptAt(
                OutlineCornerPlanner.Candidates(vertices, target.Loops, LedgeredPoints(ledger), placedPoints),
                keptOut);
            var plan = OutlineCornerPlanner.Plan(candidates, corners.TargetZ, records);
            return (CarryOut(doc, element, editor, plan, ref reference), plan.NewlyByHand);
        }

        /// <summary>
        /// Carries out a corner plan (an Update's pass, or Cancel's restore): raises and puts back
        /// one vertex at a time (<see cref="RaiseVertexTo"/>, which does not regenerate first, so a
        /// caller that changed the shape since its last regenerate must regenerate before), lets
        /// the planner settle what was reached, and writes the record when its text changes. Each
        /// corner reports the Z it actually reached — after a failed raise, where it still stands —
        /// so its record is not read as "moved by hand" next time. Caller must be inside a
        /// transaction.
        /// </summary>
        internal static CornerOutcome CarryOut(
            Document doc, Element element, SlabShapeEditor editor, CornerPlan plan, ref double? reference)
        {
            var raised = new Dictionary<(double X, double Y), double>();
            foreach (var raise in plan.Raises)
                raised[(raise.At.X, raise.At.Y)] = RaiseVertexTo(doc, element, editor, raise.At, raise.ToZ, ref reference)?.Z ?? raise.At.Z;

            var putBack = new Dictionary<(double X, double Y), double>();
            foreach (var back in plan.PutBacks)
                putBack[(back.At.X, back.At.Y)] = RaiseVertexTo(doc, element, editor, back.At, back.ToZ, ref reference)?.Z ?? back.At.Z;

            var outcome = OutlineCornerPlanner.Settle(plan, raised, putBack);
            WriteCorners(element, outcome.Records);
            return outcome;
        }

        /// <summary>Writes the corner record only when its text changes, so a pass with nothing to
        /// record leaves an element that never had one untouched.</summary>
        private static void WriteCorners(Element element, IReadOnlyList<CornerRecord> records)
        {
            if (OutlineCornerRecordCodec.Encode(records) == OutlineCornerStore.Raw(element)) return;
            OutlineCornerStore.Write(element, records);
        }

        private static List<XyzPoint> LedgeredPoints(IReadOnlyDictionary<long, IReadOnlyList<XyzPoint>> ledger) =>
            ledger.Values.SelectMany(points => points).ToList();

        /// <summary>
        /// Where a plain element's outline vertices sit while its shape editing is off: the
        /// <see cref="ReferenceGuess"/>, else the top of its bounding box; null when neither can be read.
        /// </summary>
        private static double? FlatTop(Element element) =>
            ReferenceGuess(element) ?? element.get_BoundingBox(null)?.Max.Z;

        private static int Count(LinePointsZ points) => points.Interior.Count + points.Edge.Count;

        /// <summary>
        /// One <c>AddPoints</c> batch; if Revit refuses it, each point is tried on its own so one
        /// bad point does not cost the rest (as <see cref="SlabShape"/> does). Appends what was
        /// created to <paramref name="recorded"/>, except a vertex at a plan position that was a
        /// vertex already (in <paramref name="preexisting"/>; see Apply), and returns how many
        /// points Revit placed and how many it could not.
        /// </summary>
        private static (int Created, int Lost) AddInterior(
            SlabShapeEditor editor, IReadOnlyList<XyzPoint> points, IReadOnlyList<XyzPoint> preexisting,
            List<XyzPoint> recorded)
        {
            var asked = points.Select(point => new XYZ(point.X, point.Y, point.Z)).ToList();

            try
            {
                int created = Record(editor.AddPoints(asked), preexisting, recorded);
                return (created, Math.Max(0, asked.Count - created));
            }
            catch (Exception)
            {
                // Retry one by one below.
            }

            int total = 0;
            int lost = 0;
            foreach (var point in asked)
            {
                try
                {
                    int created = Record(editor.AddPoints(new List<XYZ> { point }), preexisting, recorded);
                    if (created == 0) lost++;
                    total += created;
                }
                catch (Exception)
                {
                    lost++;
                }
            }
            return (total, lost);
        }

        /// <returns>How many vertices Revit returned, ledgered or not.</returns>
        private static int Record(
            IList<SlabShapeVertex>? created, IReadOnlyList<XyzPoint> preexisting, List<XyzPoint> recorded)
        {
            if (created == null) return 0;
            foreach (var vertex in created)
            {
                var position = SlabShape.ToPoint(vertex.Position);
                if (ShapeReplayPlanner.IndexOfXy(preexisting, position) < 0) recorded.Add(position);
            }
            return created.Count;
        }

        /// <summary>
        /// Draws a point on the outline and raises it to <paramref name="target"/>'s Z (see
        /// <see cref="RaiseVertexTo"/>). When <paramref name="existing"/> is given, a vertex already
        /// stands there (a point of a line in another view, say): nothing is drawn and that vertex
        /// is raised instead.
        /// </summary>
        /// <returns>Where the point ended up, or null when no point could be drawn or found.</returns>
        private static XyzPoint? PlaceEdgePoint(
            Document doc, Element element, SlabShapeEditor editor, XyzPoint target, XyzPoint? existing,
            ref double? reference)
        {
            // Earlier raises have changed the surface; faces and vertices are read after a regenerate.
            doc.Regenerate();

            SlabShapeVertex? drawn;
            if (existing != null)
            {
                drawn = SlabShape.VertexAt(editor, existing.Value);
            }
            else
            {
                try
                {
                    var place = new XYZ(target.X, target.Y, SurfaceZ(element, target.X, target.Y));
                    // The 2026 and 2027 APIs no longer have DrawPoint; AddPoint is tried there, as
                    // SlabShape.DrawEdgePoints does. Whether it takes an edge point is unchecked in
                    // those years — a refused point counts as lost.
#if REVIT2026 || REVIT2027
                    drawn = editor.AddPoint(place);
#else
#pragma warning disable CS0618
                    drawn = editor.DrawPoint(place);
#pragma warning restore CS0618
#endif
                }
                catch (Exception)
                {
                    return null;
                }
            }
            if (drawn == null) return null;

            return RaiseVertex(doc, element, editor, drawn, target.Z, ref reference);
        }

        /// <summary>
        /// Raises (or lowers) the vertex at <paramref name="at"/> (plan XY within 0.1 mm) to
        /// <paramref name="targetZ"/> and reads where it ended up. <c>ModifySubElement</c>'s offset
        /// is measured from the element's unshaped top, which is guessed (level + height offset)
        /// until the first raise reveals it as resulting Z − offset used, kept in
        /// <paramref name="reference"/> for the raises after it; a raise that misses by more than
        /// 0.1 mm is corrected once with the revealed value. Regenerates after each raise. The
        /// vertices are read as they stand, so a caller that changed the shape since its last
        /// regenerate must regenerate first. Caller must be inside a transaction.
        /// </summary>
        /// <returns>Where the vertex ended up — after a failed raise, where it still stands — or
        /// null when no vertex is found at <paramref name="at"/>.</returns>
        private static XyzPoint? RaiseVertexTo(
            Document doc, Element element, SlabShapeEditor editor, XyzPoint at, double targetZ,
            ref double? reference)
        {
            var vertex = SlabShape.VertexAt(editor, at);
            return vertex == null ? null : RaiseVertex(doc, element, editor, vertex, targetZ, ref reference);
        }

        private static XyzPoint? RaiseVertex(
            Document doc, Element element, SlabShapeEditor editor, SlabShapeVertex vertex, double targetZ,
            ref double? reference)
        {
            var at = SlabShape.ToPoint(vertex.Position);
            double offset = targetZ - (reference ?? ReferenceGuess(element) ?? at.Z);

            var result = Raise(doc, editor, vertex, at, offset);
            if (result == null)
            {
                // The raise failed: the point may still be there, at the surface.
                var still = SlabShape.VertexAt(editor, at);
                return still == null ? null : SlabShape.ToPoint(still.Position);
            }

            reference = result.Value.Z - offset;
            if (Math.Abs(result.Value.Z - targetZ) > MatchToleranceFeet)
            {
                offset = targetZ - reference.Value;
                var corrected = Raise(doc, editor, null, at, offset);
                if (corrected != null) result = corrected;
            }

            return result;
        }

        /// <summary>Raises the vertex at <paramref name="at"/> (re-found if stale) and reads it back.</summary>
        private static XyzPoint? Raise(
            Document doc, SlabShapeEditor editor, SlabShapeVertex? vertex, XyzPoint at, double offset)
        {
            if (vertex == null || !vertex.IsValidObject) vertex = SlabShape.VertexAt(editor, at);
            if (vertex == null) return null;

            try
            {
                editor.ModifySubElement(vertex, offset);
            }
            catch (Exception)
            {
                doc.Regenerate();
                return null;
            }

            doc.Regenerate();
            var after = SlabShape.VertexAt(editor, at);
            return after == null ? null : SlabShape.ToPoint(after.Position);
        }

        /// <summary>
        /// The element's unshaped top as Revit stores it: its level plus its height offset. Only a
        /// first guess for <c>ModifySubElement</c>'s reference — the first raise measures the real one.
        /// </summary>
        private static double? ReferenceGuess(Element element)
        {
            if (element.Document.GetElement(element.LevelId) is not Level level) return null;

            var offset = element is Floor
                ? element.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)
                : element.get_Parameter(BuiltInParameter.TOPOSOLID_HEIGHTABOVELEVEL_PARAM);

            return level.ProjectElevation + (offset != null && offset.HasValue ? offset.AsDouble() : 0);
        }

        /// <summary>
        /// The top surface's Z at a plan point: a vertical line through the top faces, or — for a
        /// point on or just outside the outline that the line misses — the nearest point on them,
        /// as the probe did. With no readable top face, the element's highest point.
        /// </summary>
        private static double SurfaceZ(Element element, double x, double y)
        {
            var box = element.get_BoundingBox(null);
            double top = box?.Max.Z ?? 0;
            double bottom = box?.Min.Z ?? top - 1;

            var faces = new List<Face>();
            try
            {
                if (element is HostObject host)
                {
                    foreach (var reference in HostObjectUtils.GetTopFaces(host))
                    {
                        if (element.GetGeometryObjectFromReference(reference) is Face face) faces.Add(face);
                    }
                }
            }
            catch (Exception)
            {
                return top;
            }
            if (faces.Count == 0) return top;

            var vertical = Line.CreateBound(new XYZ(x, y, top + 1), new XYZ(x, y, bottom - 1));
            double? hit = null;
            foreach (var face in faces)
            {
                try
                {
                    if (face.Intersect(vertical, out IntersectionResultArray results) != SetComparisonResult.Overlap ||
                        results == null)
                        continue;
                    foreach (IntersectionResult result in results)
                    {
                        double z = result.XYZPoint.Z;
                        if (hit == null || z > hit) hit = z;
                    }
                }
                catch (Exception)
                {
                    // Try the next face.
                }
            }
            if (hit != null) return hit.Value;

            var above = new XYZ(x, y, top + 1);
            double best = double.MaxValue;
            double nearestZ = top;
            foreach (var face in faces)
            {
                var projected = face.Project(above);
                if (projected != null && projected.Distance < best)
                {
                    best = projected.Distance;
                    nearestZ = projected.XYZPoint.Z;
                }
            }
            return nearestZ;
        }

        private static SlabShapeVertex? FindVertex(List<SlabShapeVertex> live, XyzPoint recorded)
        {
            foreach (var vertex in live)
            {
                var position = vertex.Position;
                if (Math.Abs(position.X - recorded.X) <= MatchToleranceFeet &&
                    Math.Abs(position.Y - recorded.Y) <= MatchToleranceFeet &&
                    Math.Abs(position.Z - recorded.Z) <= MatchToleranceFeet)
                {
                    return vertex;
                }
            }
            return null;
        }
    }
}
