using System;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.AutoDimensions;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// The single-view ribbon entry point: dimension every wall crossing a Dimensions_Line detail
    /// line in the active view. Deliberately walls-only and active-view-only — the scope pane
    /// (AutoDimensionsPaneCommand) is the multi-view, multi-category entry point.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class AutoDimensionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                return Run(commandData, ref message);
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("RVTuk – Auto Dimensions (error)", ex.ToString());
                return Result.Failed;
            }
        }

        private static Result Run(ExternalCommandData commandData, ref string message)
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            var doc = uiDoc.Document;
            var view = uiDoc.ActiveView;
            var tally = new DimensionRunTally();

            using (var tx = new Transaction(doc, "Auto Dimensions"))
            {
                tx.Start();

                try
                {
                    DimensionLineStyle.EnsureExists(doc);
                    var referenceLines = DimensionRunner.CollectReferenceLines(doc, view);
                    DimensionRunner.RunPair(doc, referenceLines, view, DimensionCategories.Walls, tally);
                }
                catch
                {
                    tx.RollBack();
                    throw;
                }

                tx.Commit();
            }

            ShowSummary(tally);
            return Result.Succeeded;
        }

        private static void ShowSummary(DimensionRunTally tally)
        {
            var summary = new StringBuilder();
            if (tally.Created == 0 && tally.Skipped == 0)
            {
                summary.Append("No Dimensions_Line lines found — the line style now exists in " +
                    "this project; draw reference lines and run again.");
            }
            else
            {
                summary.Append($"{tally.Created} dimension(s) created, {tally.Skipped} line(s) skipped " +
                    "(no walls found).");
            }
            TaskDialog.Show("RVTuk – Auto Dimensions", summary.ToString());
        }
    }
}
