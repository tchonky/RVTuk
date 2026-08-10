using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// Shows the Topo Tools pane and refreshes it. Refresh returns immediately (it does its Revit
    /// work on a background thread via an ExternalEvent), so this command never waits on an event
    /// it is itself blocking. Everything is wrapped: a pane that fails to show must say why.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class TopoToolsPaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (Application.TopoToolsPaneViewModel is null)
                {
                    message = "The Topo Tools pane was not registered when RVTuk started " +
                        "(RegisterTopoTools is off in this build).";
                    TaskDialog.Show("RVTuk – Topo Tools", message);
                    return Result.Failed;
                }

                var pane = commandData.Application.GetDockablePane(TopoToolsPaneProvider.PaneId);
                pane.Show();
                Application.TopoToolsPaneViewModel.Refresh();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("RVTuk – Topo Tools (error)", ex.ToString());
                return Result.Failed;
            }
        }
    }
}
