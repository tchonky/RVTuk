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
    /// <param name="Lost">Points that could not be placed, or not at their height.</param>
    public sealed record ApplyOutcome(int Added, int Removed, int Missing, int Lost);

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
    /// </summary>
    public static class TopoPointApplier
    {
        /// <summary>0.1 mm, in Revit's internal feet.</summary>
        public const double MatchToleranceFeet = 0.1 / 304.8;

        /// <summary>
        /// Caller must already be inside a transaction. <paramref name="newPointsByLine"/> must
        /// put at most one point on any plan spot (<see cref="TopoPointDeduper"/>): every point
        /// given is placed.
        /// </summary>
        public static ApplyOutcome Apply(
            SlabTarget target,
            IReadOnlyCollection<long> lineIdsInView,
            IReadOnlyDictionary<long, LinePointsZ> newPointsByLine,
            Func<long, bool> lineStillExists)
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

            // Nothing to add and nothing to take out: leave the element as it is, so a floor with
            // no lines over it is not switched to shape editing.
            if (staleLineIds.Count == 0 && newPointsByLine.Values.Sum(Count) == 0)
                return new ApplyOutcome(0, 0, 0, 0);

            if (!editor.IsEnabled)
            {
                editor.Enable();
                doc.Regenerate();
            }

            int removed = 0;
            int missing = 0;

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
                            missing++;
                            continue;
                        }

                        live.Remove(match);
                        if (match.IsValidObject && editor.DeletePoint(match)) removed++;
                        else missing++;
                    }
                    ledger.Remove(lineId);
                }
                doc.Regenerate();
            }

            // The ledger records only points this run created. A vertex that already stood at a
            // point's plan position before the run (an outline corner, a point added by hand, a
            // point of a line in another view) is raised or reused but never ledgered: otherwise a
            // later run would delete a vertex this tool does not own, or report it "moved by hand"
            // on every run because Revit merged our point into it.
            var preexisting = new List<XyzPoint>();
            foreach (SlabShapeVertex vertex in editor.SlabShapeVertices)
                preexisting.Add(SlabShape.ToPoint(vertex.Position));

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

            TopoLedgerStore.Write(element, ledger);
            return new ApplyOutcome(added, removed, missing, lost);
        }

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
        /// Draws a point on the outline and raises it to <paramref name="target"/>'s Z.
        /// <c>ModifySubElement</c>'s offset is measured from the element's unshaped top, which is
        /// guessed (level + height offset) until the first raise reveals it as resulting Z − offset
        /// used; a raise that misses by more than 0.1 mm is corrected once with the revealed value.
        /// When <paramref name="existing"/> is given, a vertex already stands there (an outline
        /// corner, say): nothing is drawn and that vertex is raised instead.
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

            var at = SlabShape.ToPoint(drawn.Position);
            double offset = target.Z - (reference ?? ReferenceGuess(element) ?? at.Z);

            var result = Raise(doc, editor, drawn, at, offset);
            if (result == null)
            {
                // The raise failed: the point may still be there, at the surface.
                var still = SlabShape.VertexAt(editor, at);
                return still == null ? null : SlabShape.ToPoint(still.Position);
            }

            reference = result.Value.Z - offset;
            if (Math.Abs(result.Value.Z - target.Z) > MatchToleranceFeet)
            {
                offset = target.Z - reference.Value;
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
