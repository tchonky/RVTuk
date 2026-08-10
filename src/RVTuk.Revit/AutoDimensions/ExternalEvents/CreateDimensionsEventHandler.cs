using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions.ExternalEvents
{
    /// <summary>
    /// The scope pane's Create Dimensions run, marshalled onto Revit's main thread: for every
    /// level with a reference view, for every selected view of that level, run the reference
    /// view's lines against that view. One transaction for the whole fan-out, including the
    /// selection write — a hard failure rolls back every dimension it created.
    /// </summary>
    public class CreateDimensionsEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);
        private int _categoryMask;
        private IReadOnlyList<long> _checkedViewIds = Array.Empty<long>();
        private long _dimensionTypeId;

        public string Summary { get; private set; } = string.Empty;

        public void Prepare(int categoryMask, IReadOnlyList<long> checkedViewIds, long dimensionTypeId)
        {
            _categoryMask = categoryMask;
            _checkedViewIds = checkedViewIds;
            _dimensionTypeId = dimensionTypeId;
            _done.Reset();
        }

        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    Summary = "No active document.";
                    return;
                }

                Summary = Run(doc);
                TaskDialog.Show("RVTuk – Auto Dimensions", Summary);
            }
            catch (Exception ex)
            {
                Summary = "Auto Dimensions failed: " + ex.Message;
                TaskDialog.Show("RVTuk – Auto Dimensions (error)", ex.ToString());
            }
            finally
            {
                _done.Set();
            }
        }

        private string Run(Document doc)
        {
            var categories = CategoryMask.FromMask(_categoryMask);
            var selectedViewIds = new HashSet<long>(_checkedViewIds);
            // Resolved once for the whole fan-out. Null when nothing was chosen or the chosen
            // type has since been deleted — the runner then falls back to each view's default.
            var dimensionType = _dimensionTypeId > 0
                ? doc.GetElement(new ElementId(_dimensionTypeId)) as DimensionType
                : null;

            // Re-discovered here rather than trusting the pane's snapshot: the model may have
            // changed since the tree was populated.
            var scopes = LevelScopeFinder.Find(doc);
            var levelsWithoutReferenceView = scopes.Count(s => !s.HasReferenceView);

            var report = new StringBuilder();
            var totalCreated = 0;
            var totalSkipped = 0;
            var totals = new DimensionRunTally();

            using (var tx = new Transaction(doc, "Auto Dimensions (scope)"))
            {
                tx.Start();

                try
                {
                    DimensionLineStyle.EnsureExists(doc);

                    foreach (var scope in scopes)
                    {
                        if (!scope.HasReferenceView) continue;

                        var selected = scope.Views.Where(v => selectedViewIds.Contains(v.ViewId)).ToList();
                        if (selected.Count == 0) continue;

                        if (doc.GetElement(new ElementId(scope.ReferenceViewId!.Value)) is not View referenceView)
                            continue;

                        var stringLines = DimensionRunner.CollectReferenceLines(doc, referenceView);
                        var refLines = DimensionRunner.CollectRefLines(doc, referenceView);

                        foreach (var viewInfo in selected)
                        {
                            if (doc.GetElement(new ElementId(viewInfo.ViewId)) is not View targetView) continue;

                            var tally = new DimensionRunTally();
                            DimensionRunner.RunPair(
                                doc, stringLines, refLines, targetView, categories,
                                dimensionType, tally);

                            totalCreated += tally.Created;
                            totalSkipped += tally.Skipped;
                            totals.ExcludedNotCut += tally.ExcludedNotCut;
                            totals.ExcludedNoReferences += tally.ExcludedNoReferences;
                            totals.CoincidentMerged += tally.CoincidentMerged;
                            totals.RefLinesUnattached += tally.RefLinesUnattached;
                            totals.RefLinesUnresolved += tally.RefLinesUnresolved;
                            report.AppendLine(
                                $"{scope.LevelName} — {viewInfo.ViewName}: " +
                                $"{tally.Created} created, {tally.Skipped} skipped");
                        }
                    }

                    ScopeSelectionStore.Write(
                        doc, _categoryMask, _checkedViewIds, _dimensionTypeId);
                }
                catch
                {
                    tx.RollBack();
                    throw;
                }

                tx.Commit();
            }

            if (report.Length == 0) report.AppendLine("No selected view had a reference view to work from.");
            report.AppendLine();
            report.Append($"Total: {totalCreated} dimension(s) created, {totalSkipped} line(s) skipped.");
            if (levelsWithoutReferenceView > 0)
                report.Append($" {levelsWithoutReferenceView} level(s) skipped — no reference view.");

            AppendExclusions(report, totals);
            return report.ToString();
        }

        /// <summary>
        /// Why elements a line visibly crosses may carry no mark. Without this the three causes
        /// are indistinguishable from a bug, and every one of them is silent by nature.
        /// </summary>
        private static void AppendExclusions(StringBuilder report, DimensionRunTally totals)
        {
            if (!totals.HasExclusions) return;

            report.AppendLine();
            report.AppendLine();
            report.AppendLine("Not marked:");
            if (totals.ExcludedNotCut > 0)
                report.AppendLine($"  • {totals.ExcludedNotCut} element(s) the view draws but does not cut (below the cut plane).");
            if (totals.ExcludedNoReferences > 0)
                report.AppendLine($"  • {totals.ExcludedNoReferences} crossing(s) with no usable reference — a curtain or stacked wall (which report no side faces), or a door/window whose jambs lie parallel to the line (a line crossing a wall cannot measure the openings in it).");
            if (totals.CoincidentMerged > 0)
                report.AppendLine($"  • {totals.CoincidentMerged} reference(s) merged for sharing a position along the line (joined walls).");
            if (totals.RefLinesUnattached > 0)
                report.AppendLine($"  • {totals.RefLinesUnattached} reference line(s) touching no dimension string — draw one from a wall end to the string it should mark.");
            if (totals.RefLinesUnresolved > 0)
                report.Append($"  • {totals.RefLinesUnresolved} reference line(s) with no wall end to mark — nothing within reach of the far end, the wall too far off parallel to the string, or its end face consumed by a join with another wall.");
        }

        public string GetName() => "RVTuk.AutoDimensionsCreateDimensionsEventHandler";
    }
}
