using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.AutoDimensions;

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

        public bool HasExclusions =>
            ExcludedNotCut > 0 || ExcludedNoReferences > 0 || CoincidentMerged > 0;
    }

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
        /// Reference lines owned by a view. Document-wide collector filtered by OwnerViewId, not a
        /// view-scoped collector: a view-scoped collector only returns elements currently visible,
        /// and lines hidden by the view template must still produce dimensions.
        /// </summary>
        public static IReadOnlyList<DetailLine> CollectReferenceLines(Document doc, View referenceView)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(CurveElement))
                .Cast<CurveElement>()
                .OfType<DetailLine>()
                .Where(l => l.OwnerViewId == referenceView.Id && DimensionLineStyle.IsDimensionsLine(l))
                .ToList();
        }

        /// <summary>
        /// Runs every reference line against one target view. The lines may be owned by a
        /// different view of the same level — that is exactly the fan-out the scope pane performs.
        /// </summary>
        public static void RunPair(
            Document doc,
            IReadOnlyList<DetailLine> referenceLines,
            View targetView,
            DimensionCategories categories,
            double openingReach,
            DimensionRunTally tally)
        {
            var candidates = DimensionCandidateCollector.Collect(doc, targetView, categories);
            tally.ExcludedNotCut += candidates.ExcludedNotCut;

            foreach (var line in referenceLines)
            {
                try
                {
                    RunLine(doc, line, targetView, candidates, openingReach, tally);
                }
                catch
                {
                    // One unreadable line never aborts the run; the transaction still guarantees
                    // all-or-nothing for a hard Revit-level failure.
                    tally.Skipped++;
                }
            }
        }

        private static void RunLine(
            Document doc,
            DetailLine line,
            View targetView,
            DimensionCandidateSet candidates,
            double openingReach,
            DimensionRunTally tally)
        {
            // Always first, and independent of whether this line still has crossings: a line whose
            // walls were deleted or moved away must still lose its stale dimension.
            var tracked = AutoDimensionTracker.TryGetTrackedDimension(line, targetView.Id);
            if (tracked != null) doc.Delete(tracked.Id);

            if (line.GeometryCurve is not Line geometryLine)
            {
                tally.Skipped++;
                return;
            }

            var lineStart = ToXyPoint(geometryLine.GetEndPoint(0));
            var lineEnd = ToXyPoint(geometryLine.GetEndPoint(1));

            // Walls by being crossed, openings by lying alongside — interleaved in one order
            // along the line, so the dimension string reads correctly.
            var crossingIndices = CandidateMatcher.FindMatchIndices(
                lineStart, lineEnd, candidates.Segments, candidates.MatchModes, openingReach);

            if (crossingIndices.Count == 0)
            {
                tally.Skipped++;
                return;
            }

            var referenceArray = new ReferenceArray();
            var anyResolved = false;
            foreach (var index in crossingIndices)
            {
                if (DimensionCandidateCollector.TryAppendReferences(
                        candidates, index, lineStart, lineEnd, referenceArray))
                    anyResolved = true;
                else
                    tally.ExcludedNoReferences++;
            }

            if (!anyResolved)
            {
                tally.Skipped++;
                return;
            }

            var dimensionLine = ToTargetViewPlane(geometryLine, targetView);
            var dimension = doc.Create.NewDimension(targetView, dimensionLine, referenceArray);
            dimension = RemoveCoincidentReferences(doc, targetView, dimensionLine, dimension, tally);
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
            Document doc, View view, Line dimensionLine, Dimension dimension, DimensionRunTally tally)
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
            return doc.Create.NewDimension(view, dimensionLine, filtered);
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
