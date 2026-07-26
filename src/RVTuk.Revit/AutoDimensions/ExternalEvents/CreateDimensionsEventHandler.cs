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

        public string Summary { get; private set; } = string.Empty;

        public void Prepare(int categoryMask, IReadOnlyList<long> checkedViewIds)
        {
            _categoryMask = categoryMask;
            _checkedViewIds = checkedViewIds;
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

            // Re-discovered here rather than trusting the pane's snapshot: the model may have
            // changed since the tree was populated.
            var scopes = LevelScopeFinder.Find(doc);
            var levelsWithoutReferenceView = scopes.Count(s => !s.HasReferenceView);

            var report = new StringBuilder();
            var totalCreated = 0;
            var totalSkipped = 0;

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

                        var referenceLines = DimensionRunner.CollectReferenceLines(doc, referenceView);

                        foreach (var viewInfo in selected)
                        {
                            if (doc.GetElement(new ElementId(viewInfo.ViewId)) is not View targetView) continue;

                            var tally = new DimensionRunTally();
                            DimensionRunner.RunPair(doc, referenceLines, targetView, categories, tally);

                            totalCreated += tally.Created;
                            totalSkipped += tally.Skipped;
                            report.AppendLine(
                                $"{scope.LevelName} — {viewInfo.ViewName}: " +
                                $"{tally.Created} created, {tally.Skipped} skipped");
                        }
                    }

                    ScopeSelectionStore.Write(doc, _categoryMask, _checkedViewIds);
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

            return report.ToString();
        }

        public string GetName() => "RVTuk.AutoDimensionsCreateDimensionsEventHandler";
    }
}
