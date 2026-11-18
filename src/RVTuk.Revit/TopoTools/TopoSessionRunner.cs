using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// One Update: collect the view's topo lines, clip each to the toposolid or floor being edited,
    /// read its height, apply. Only that element is written to. A line that does not cross it is
    /// ignored — not applied and not reported — though, being in the view, its earlier points on
    /// this element (if it has since moved off it) are still cleaned by the applier. Where lines
    /// put points on one plan spot, the highest is kept (<see cref="TopoPointDeduper"/>).
    /// </summary>
    public static class TopoSessionRunner
    {
        /// <summary>Caller must already be inside a transaction.</summary>
        /// <param name="heightTextByLine">Each line's raw height text; a line without one counts as unreadable "".</param>
        public static TopoSessionReport Apply(
            Document doc,
            ViewPlan view,
            SlabTarget target,
            double spacingFeet,
            IReadOnlyDictionary<long, string> heightTextByLine)
        {
            var report = new TopoSessionReport();

            // The whole shared→internal conversion: the shared elevation of the internal origin,
            // subtracted. Rotation and true north do not enter into Z.
            double sharedElevationOfInternalZero =
                doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Elevation;

            var lines = TopoLineCollector.Collect(doc, view);
            var lineIdsInView = lines.Select(line => line.LineId).ToList();
            var newPointsByLine = new Dictionary<long, LinePointsZ>();

            foreach (var line in lines)
            {
                var points = TopoLineClipper.Points(
                    line.Polyline, target.Loops, spacingFeet, ShapeReplayPlanner.EdgeBandFeet);
                if (points.IsEmpty) continue;

                var text = heightTextByLine.TryGetValue(line.LineId, out var raw) ? raw ?? "" : "";
                if (!SurveyHeightText.TryParse(text, out double sharedFeet))
                {
                    report.UnreadableHeights.Add(text);
                    continue;
                }

                double z = sharedFeet - sharedElevationOfInternalZero;
                newPointsByLine[line.LineId] = new LinePointsZ(
                    points.Interior.Select(xy => new XyzPoint(xy.X, xy.Y, z)).ToList(),
                    points.Edge.Select(xy => new XyzPoint(xy.X, xy.Y, z)).ToList());
            }

            // One point per plan spot, the highest, before anything reaches the element: where
            // lines cross (or chain on the outline) the rule is ours, not whatever Revit merges.
            var deduped = TopoPointDeduper.Dedupe(newPointsByLine);

            var outcome = TopoPointApplier.Apply(
                target,
                lineIdsInView,
                deduped.Kept,
                lineId => doc.GetElement(new ElementId(lineId)) != null);

            report.LinesApplied = newPointsByLine.Count;
            report.PointsAdded = outcome.Added;
            report.PointsReplaced = outcome.Removed;
            report.PointsMovedByHand = outcome.Missing;
            report.PointsLost = outcome.Lost;
            report.PointsDroppedAtCrossings = deduped.DroppedForHigher;
            return report;
        }
    }
}
