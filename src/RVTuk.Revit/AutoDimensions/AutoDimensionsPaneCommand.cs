using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.AutoDimensions
{
    /// <summary>
    /// Shows the scope pane and refreshes it. Refresh returns immediately (it does its Revit work
    /// on a background thread via an ExternalEvent), so this command never waits on an event it
    /// is itself blocking.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class AutoDimensionsPaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var pane = commandData.Application.GetDockablePane(AutoDimensionsPaneProvider.PaneId);
            pane.Show();
            Application.AutoDimensionsPaneViewModel.Refresh();
            return Result.Succeeded;
        }
    }
}
