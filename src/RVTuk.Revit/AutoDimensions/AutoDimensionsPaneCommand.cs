using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// Shows the Auto Dimensions pane and refreshes it. Refresh returns immediately (it does its
    /// Revit work on a background thread via an ExternalEvent), so this command never waits on an
    /// event it is itself blocking.
    ///
    /// Everything is wrapped: a pane that fails to show must say why. The first version of this
    /// command had no handler, so a pane Revit could not place looked exactly like a dead button.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class AutoDimensionsPaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (Application.AutoDimensionsPaneViewModel is null)
                {
                    message = "The Auto Dimensions pane was not registered when RVTuk started " +
                        "(RegisterAutoDimensions is off in this build).";
                    TaskDialog.Show("RVTuk – Auto Dimensions", message);
                    return Result.Failed;
                }

                var pane = commandData.Application.GetDockablePane(AutoDimensionsPaneProvider.PaneId);
                pane.Show();
                Application.AutoDimensionsPaneViewModel.Refresh();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("RVTuk – Auto Dimensions (error)", ex.ToString());
                return Result.Failed;
            }
        }
    }
}
