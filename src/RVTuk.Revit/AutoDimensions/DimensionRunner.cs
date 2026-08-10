using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.AutoDimensions;
using RVTuk.Core.Shared.Geometry;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>Running totals for one Auto Dimensions run, across however many views it spans.</summary>
    public sealed class DimensionRunTally
    {
        public int Created;
        public int Skipped;

        /// <summary>Elements the view draws in projection but does not cut.</summary>
        public int ExcludedNotCut;

        /// <summary>
        /// Crossings whose two faces could not be resolved — curtain walls and stacked walls
        /// report no side faces, so they cross the line and mark nothing.
        /// </summary>
        public int ExcludedNoReferences;

        /// <summary>
        /// References collapsed for sitting at the same station along the line (joined walls,
        /// a door flush with a wall face). Expected in small numbers; a large count means real
        /// marks are being merged away.
        /// </summary>
        public int CoincidentMerged;

        /// <summary>Ref lines that met no string, or whose both ends sat on one.</summary>
        public int RefLinesUnattached;

        /// <summary>
        /// Ref lines that met a string but found no reference: no wall end within tolerance, a
        /// wall too far off parallel to the string, or no planar end face — which is what a wall
        /// joined into another at that end leaves behind.
        /// </summary>
        public int RefLinesUnresolved;

        public bool HasExclusions =>
            ExcludedNotCut > 0 || ExcludedNoReferences > 0 || CoincidentMerged > 0
            || RefLinesUnattached > 0 || RefLinesUnresolved > 0;
    }

    /// <summary>One reference line, with the ring its line style puts it in.</summary>
    public sealed record RingLine(DetailLine Line, DimensionRing Ring);

    /// <summary>
    /// The per-reference-line pipeline, shared by the single-view ribbon command and the scope
    /// pane's fan-out so the two entry points can't drift: delete the stale dimension, find the
    /// crossings, resolve references, create, de-duplicate coincident references, track.
    ///
    /// Must be called inside an open transaction.
    /// </summary>
    public static class DimensionRunner
    {
        /// <summary>
        /// Reference lines owned by a view, each tagged with its ring. Document-wide collector
        /// filtered by OwnerViewId, not a view-scoped collector: a view-scoped collector only
        /// returns elements currently visible, and lines hidden by the view template must still
        /// produce dimensions.
        /// </summary>
        public static IReadOnlyList<RingLine> CollectReferenceLines(Document doc, View referenceView)
        {
            var result = new List<RingLine>();

            foreach (var line in new FilteredElementCollector(doc)
                         .OfClass(typeof(CurveElement))
                         .Cast<CurveElement>()
                         .OfType<DetailLine>()
                         .Where(l => l.OwnerViewId == referenceView.Id))
            {
                if (DimensionLineStyle.TryGetRing(line, out var ring))
                    result.Add(new RingLine(line, ring));
            }

            return result;
        }

        /// <summary>
        /// Feet (~3 mm). How near a ref line must come to a string to count as touching it —
        /// Revit's snaps make it exact in practice, and this covers a line drawn by eye.
        /// </summary>
        public const double RefLineTouchTolerance = 0.01;

        /// <summary>
        /// The reference view's _DP-Dim Ref lines. Same collection rule as the strings: owned by
        /// the view, found document-wide so template-hidden lines still count.
        /// </summary>
        public static IReadOnlyList<DetailLine> CollectRefLines(Document doc, View referenceView)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .OfType<DetailLine>()
                .Where(l => l.OwnerViewId == referenceView.Id && DimensionLineStyle.IsRefLine(l))
                .ToList();
        }

        /// <summary>
        /// Runs every dimension string against one target view. The lines may be owned by a
        /// different view of the same level — that is exactly the fan-out the scope pane performs.
        ///
        /// <paramref name="stringLines"/> are the strings that receive dimensions;
        /// <paramref name="refLines"/> are the pointers that mark a wall end on one, and are never
        /// dimensioned themselves. Deliberately not both called "reference lines".
        /// </summary>
        public static void RunPair(
            Document doc,
            IReadOnlyList<RingLine> stringLines,
            IReadOnlyList<DetailLine> refLines,
            View targetView,
            DimensionCategories categories,
            DimensionType? dimensionType,
            DimensionRunTally tally)
        {
            var candidates = DimensionCandidateCollector.Collect(doc, targetView, categories);
            tally.ExcludedNotCut += candidates.ExcludedNotCut;

            // Matching happens for all the level's lines at once, not line by line: an opening
            // belongs to the line that owns it, which can only be known by comparing them.
            var geometry = stringLines.Select(l => l.Line.GeometryCurve as Line).ToList();

            var straightIndices = new List<int>();
            var lines = new List<ReferenceLine>();
            for (int i = 0; i < geometry.Count; i++)
            {
                if (geometry[i] == null) continue;

                straightIndices.Add(i);
                lines.Add(new ReferenceLine(
                    ToXyPoint(geometry[i]!.GetEndPoint(0)),
                    ToXyPoint(geometry[i]!.GetEndPoint(1)),
                    stringLines[i].Ring));
            }

            var matches = CandidateMatcher.FindMatchesForLines(
                lines, candidates.Segments, candidates.MatchModes, candidates.Occluders);

            var byLine = new IReadOnlyList<CandidateMatchResult>?[stringLines.Count];
            for (int k = 0; k < straightIndices.Count; k++) byLine[straightIndices[k]] = matches[k];

            var refByLine = ResolveRefLines(refLines, lines, straightIndices, candidates,
                stringLines.Count, tally);

            // Every line goes through the loop, including any whose geometry isn't a Line: the
            // stale-dimension delete must stay unconditional.
            for (int i = 0; i < stringLines.Count; i++)
            {
                try
                {
                    RunLine(doc, stringLines[i].Line, geometry[i], targetView, candidates,
                        byLine[i] ?? Array.Empty<CandidateMatchResult>(),
                        refByLine[i], dimensionType, tally);
                }
                catch
                {
                    // One unreadable line never aborts the run; the transaction still guarantees
                    // all-or-nothing for a hard Revit-level failure.
                    tally.Skipped++;
                }
            }
        }

        /// <summary>
        /// Each ref line's marks, bucketed by the string they join. A ref line joins EVERY string
        /// it meets, so one line can contribute a mark to several — how far it is drawn is the
        /// control.
        /// </summary>
        private static List<(double T, Reference Reference)>?[] ResolveRefLines(
            IReadOnlyList<DetailLine> refLines,
            IReadOnlyList<ReferenceLine> lines,
            IReadOnlyList<int> straightIndices,
            DimensionCandidateSet candidates,
            int stringLineCount,
            DimensionRunTally tally)
        {
            var byLine = new List<(double T, Reference Reference)>?[stringLineCount];

            foreach (var refLine in refLines)
            {
                if (refLine.GeometryCurve is not Line refGeometry)
                {
                    tally.RefLinesUnattached++;
                    continue;
                }

                var match = RefLineMatcher.Match(
                    ToXyPoint(refGeometry.GetEndPoint(0)),
                    ToXyPoint(refGeometry.GetEndPoint(1)),
                    lines,
                    RefLineTouchTolerance);

                if (match == null)
                {
                    tally.RefLinesUnattached++;
                    continue;
                }

                foreach (var hit in match.Hits)
                {
                    var reference = WallEndResolver.TryResolve(
                        candidates,
                        match.TargetEnd,
                        lines[hit.LineIndex].Start,
                        lines[hit.LineIndex].End);

                    if (reference == null)
                    {
                        tally.RefLinesUnresolved++;
                        continue;
                    }

                    var target = straightIndices[hit.LineIndex];
                    (byLine[target] ??= new List<(double, Reference)>()).Add((hit.T, reference));
                }
            }

            return byLine;
        }

        private static void RunLine(
            Document doc,
            DetailLine line,
            Line? geometryLine,
            View targetView,
            DimensionCandidateSet candidates,
            IReadOnlyList<CandidateMatchResult> matches,
            IReadOnlyList<(double T, Reference Reference)>? refMarks,
            DimensionType? dimensionType,
            DimensionRunTally tally)
        {
            // Always first, and independent of whether this line still has matches: a line whose
            // walls were deleted or moved away must still lose its stale dimension.
            var tracked = AutoDimensionTracker.TryGetTrackedDimension(line, targetView.Id);
            if (tracked != null) doc.Delete(tracked.Id);

            if (geometryLine == null)
            {
                tally.Skipped++;
                return;
            }

            if (matches.Count == 0 && (refMarks == null || refMarks.Count == 0))
            {
                tally.Skipped++;
                return;
            }

            var lineStart = ToXyPoint(geometryLine.GetEndPoint(0));
            var lineEnd = ToXyPoint(geometryLine.GetEndPoint(1));

            // Candidate marks and ref line marks are ordered together by position along the line,
            // so a wall end lands between the two walls it sits between rather than at the end.
            var entries = new List<(double T, List<Reference> References)>();

            foreach (var match in matches)
            {
                var resolved = DimensionCandidateCollector.TryResolveReferences(
                    candidates, match.Index, lineStart, lineEnd);

                if (resolved == null) tally.ExcludedNoReferences++;
                else entries.Add((match.T, resolved));
            }

            if (refMarks != null)
            {
                foreach (var mark in refMarks)
                    entries.Add((mark.T, new List<Reference> { mark.Reference }));
            }

            var referenceArray = new ReferenceArray();
            foreach (var entry in entries.OrderBy(e => e.T))
            {
                foreach (var reference in entry.References) referenceArray.Append(reference);
            }

            // Revit needs two. A lone ref line mark is one, which the old wall-only path could
            // never produce.
            if (referenceArray.Size < 2)
            {
                tally.Skipped++;
                return;
            }

            var dimensionLine = ToTargetViewPlane(geometryLine, targetView);
            var dimension = CreateDimension(
                doc, targetView, dimensionLine, referenceArray, dimensionType);
            dimension = RemoveCoincidentReferences(
                doc, targetView, dimensionLine, dimension, dimensionType, tally);
            if (dimension == null)
            {
                tally.Skipped++;
                return;
            }

            AutoDimensionTracker.SetTrackedDimension(line, targetView.Id, dimension.Id);
            tally.Created++;
        }

        /// <summary>
        /// A dimension's line has to lie in the plane of the view it's created in. The reference
        /// line sits on its own view's sketch plane, so fanning out to another view of the same
        /// level needs the Z swapped for the target's. A no-op when target == reference view.
        /// </summary>
        private static Line ToTargetViewPlane(Line line, View targetView)
        {
            try
            {
                var plane = targetView.SketchPlane?.GetPlane();
                if (plane == null) return line;

                var z = plane.Origin.Z;
                var start = line.GetEndPoint(0);
                var end = line.GetEndPoint(1);
                if (Math.Abs(start.Z - z) < 1e-9 && Math.Abs(end.Z - z) < 1e-9) return line;

                return Line.CreateBound(new XYZ(start.X, start.Y, z), new XYZ(end.X, end.Y, z));
            }
            catch
            {
                return line;
            }
        }

        /// <summary>
        /// Joined walls (or a door flush with a wall face) can put two references at the same
        /// station along the line, producing zero-length segments. Detects them on the freshly
        /// created dimension and, if any exist, recreates it keeping only the first reference at
        /// each station. Returns null (after deleting the dimension) when fewer than two survive.
        /// </summary>
        private static Dimension? RemoveCoincidentReferences(
            Document doc,
            View view,
            Line dimensionLine,
            Dimension dimension,
            DimensionType? dimensionType,
            DimensionRunTally tally)
        {
            var segmentValues = GetSegmentValues(dimension);
            var keep = CoincidentReferenceFilter.KeepIndices(
                segmentValues, doc.Application.ShortCurveTolerance);
            if (keep.Count == segmentValues.Count + 1) return dimension;

            tally.CoincidentMerged += segmentValues.Count + 1 - keep.Count;

            var references = dimension.References;
            var filtered = new ReferenceArray();
            foreach (var index in keep)
            {
                filtered.Append(references.get_Item(index));
            }

            doc.Delete(dimension.Id);
            if (filtered.Size < 2) return null;
            return CreateDimension(doc, view, dimensionLine, filtered, dimensionType);
        }

        /// <summary>
        /// One place both creation paths go through, so the recreated (de-duplicated) dimension
        /// cannot quietly fall back to the view's default type while the original carried the
        /// chosen one. Null means no type was chosen, or the chosen one no longer exists — the
        /// view's default is then the only sensible answer.
        /// </summary>
        private static Dimension CreateDimension(
            Document doc,
            View view,
            Line dimensionLine,
            ReferenceArray references,
            DimensionType? dimensionType)
        {
            return dimensionType == null
                ? doc.Create.NewDimension(view, dimensionLine, references)
                : doc.Create.NewDimension(view, dimensionLine, references, dimensionType);
        }

        /// <summary>
        /// Segment values in order along the line; a single-segment dimension has an empty
        /// Segments collection and exposes its length via Value instead.
        /// </summary>
        private static IReadOnlyList<double> GetSegmentValues(Dimension dimension)
        {
            if (dimension.NumberOfSegments == 0)
            {
                return new[] { dimension.Value ?? 0.0 };
            }

            return dimension.Segments
                .Cast<DimensionSegment>()
                .Select(s => s.Value ?? 0.0)
                .ToList();
        }

        private static XyPoint ToXyPoint(XYZ point) => new(point.X, point.Y);
    }
}
