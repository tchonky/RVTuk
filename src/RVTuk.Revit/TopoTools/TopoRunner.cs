using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// Collect, sample, route, apply. Discovery and application share one planning pass, so what
    /// the pane lists is exactly what a run will do — there is no second implementation to drift.
    /// </summary>
    public static class TopoRunner
    {
        private sealed class PlannedLine
        {
            public long LineId;
            public TopoLineStatus Status;
            public string ElevationText = "";
            public string LengthText = "";
            public readonly List<(ToposolidTarget Target, XyzPoint Point)> Points = new();
        }

        public static TopoScope Discover(Document doc, View? activeView, double spacingFeet)
        {
            string viewName = activeView?.Name ?? "";

            if (!TopoLineStyle.Exists(doc))
                return TopoScope.NotSetUp(viewName);

            if (activeView is not ViewPlan plan)
                return TopoScope.Unavailable(viewName,
                    "Topo lines are read from plan views only — open a floor or site plan.");

            var targets = ToposolidCollector.Collect(doc, plan);
            var planned = BuildPlan(doc, plan, spacingFeet, targets);

            var lines = planned
                .Select(line => new TopoLineInfo(
                    line.LineId, line.ElevationText, line.LengthText, line.Points.Count, line.Status))
                .ToList();

            return new TopoScope(true, plan.Name, lines, targets.Count, Describe(lines, targets.Count));
        }

        public static string Apply(Document doc, View? activeView, double spacingFeet)
        {
            if (!TopoLineStyle.Exists(doc))
                return "This project has no Topo_Line style yet.";

            if (activeView is not ViewPlan plan)
                return "Topo lines are read from plan views only — open a floor or site plan.";

            var targets = ToposolidCollector.Collect(doc, plan);
            if (targets.Count == 0)
                return "No toposolid is visible in this view, so there is nothing to shape.";

            var planned = BuildPlan(doc, plan, spacingFeet, targets);
            var lineIdsSeen = planned.Select(line => line.LineId).ToList();

            using (var tx = new Transaction(doc, "Topo Tools — apply points"))
            {
                tx.Start();
                try
                {
                    int added = 0, removed = 0, missing = 0;

                    // Every target, not only those receiving points: a line dragged onto the
                    // neighbouring toposolid must lose its points on the one it left, and that
                    // toposolid's ledger is the only record of them.
                    foreach (var target in targets)
                    {
                        var newPointsByLine = new Dictionary<long, IReadOnlyList<XyzPoint>>();
                        foreach (var line in planned.Where(line => line.Status == TopoLineStatus.Ready))
                        {
                            var points = line.Points
                                .Where(entry => entry.Target.Id == target.Id)
                                .Select(entry => entry.Point)
                                .ToList();
                            if (points.Count > 0) newPointsByLine[line.LineId] = points;
                        }

                        var outcome = TopoPointApplier.Apply(
                            target,
                            lineIdsSeen,
                            newPointsByLine,
                            lineId => doc.GetElement(new ElementId(lineId)) != null);

                        added += outcome.Added;
                        removed += outcome.Removed;
                        missing += outcome.Missing;
                    }

                    tx.Commit();
                    return Summarise(planned, added, removed, missing);
                }
                catch (Exception ex)
                {
                    tx.RollBack();
                    return "Topo Tools failed, and nothing was changed: " + ex.Message;
                }
            }
        }

        private static List<PlannedLine> BuildPlan(
            Document doc, ViewPlan view, double spacingFeet, IReadOnlyList<ToposolidTarget> targets)
        {
            // The whole shared→internal conversion: the shared elevation of the internal origin,
            // subtracted. Rotation and true north do not enter into Z.
            double sharedElevationOfInternalZero =
                doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).Elevation;

            var planned = new List<PlannedLine>();

            foreach (var candidate in TopoLineCollector.Collect(doc, view))
            {
                var line = new PlannedLine
                {
                    LineId = candidate.LineId,
                    LengthText = FormatLength(doc, candidate.LengthFeet),
                };

                if (candidate.ElevationFeet == null)
                {
                    line.Status = TopoLineStatus.NoElevation;
                    line.ElevationText = "";
                    planned.Add(line);
                    continue;
                }

                // forEditing: the pane's box is an editor, and its text comes back here to be
                // parsed — a display-rounded figure would quietly move the height on every edit.
                line.ElevationText = FormatLengthForEditing(doc, candidate.ElevationFeet.Value);
                double z = candidate.ElevationFeet.Value - sharedElevationOfInternalZero;

                foreach (var xy in TopoLineSampler.Sample(candidate.Polyline, spacingFeet))
                {
                    var target = ToposolidRouter.Route(targets, xy, z);
                    if (target == null) continue;
                    line.Points.Add((target, new XyzPoint(xy.X, xy.Y, z)));
                }

                line.Status = line.Points.Count == 0
                    ? TopoLineStatus.OutsideToposolid
                    : TopoLineStatus.Ready;

                planned.Add(line);
            }

            return planned;
        }

        private static string FormatLength(Document doc, double feet) =>
            UnitFormatUtils.Format(doc.GetUnits(), SpecTypeId.Length, feet, false);

        private static string FormatLengthForEditing(Document doc, double feet) =>
            UnitFormatUtils.Format(doc.GetUnits(), SpecTypeId.Length, feet, true);

        private static string Describe(IReadOnlyList<TopoLineInfo> lines, int toposolidCount)
        {
            if (toposolidCount == 0)
                return "No toposolid is visible in this view.";
            if (lines.Count == 0)
                return "No topo lines in this view — draw detail lines on the Topo_Line style.";

            int ready = lines.Count(line => line.Status == TopoLineStatus.Ready);
            int points = lines.Sum(line => line.PointCount);
            return $"{ready} of {lines.Count} line(s) ready, {points} point(s) over " +
                   $"{toposolidCount} toposolid(s).";
        }

        private static string Summarise(
            IReadOnlyList<PlannedLine> planned, int added, int removed, int missing)
        {
            var parts = new List<string> { $"{added} point(s) added, {removed} replaced" };

            int noElevation = planned.Count(line => line.Status == TopoLineStatus.NoElevation);
            if (noElevation > 0) parts.Add($"{noElevation} line(s) skipped for having no elevation");

            int outside = planned.Count(line => line.Status == TopoLineStatus.OutsideToposolid);
            if (outside > 0) parts.Add($"{outside} line(s) fell outside every toposolid");

            if (missing > 0)
                parts.Add($"{missing} earlier point(s) had been moved by hand and were left alone");

            return string.Join("; ", parts) + ".";
        }
    }
}
