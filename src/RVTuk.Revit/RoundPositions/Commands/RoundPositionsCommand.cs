using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.RoundPositions.Commands
{
    /// <summary>
    /// Shows the Round Positions pane. Deliberately does not scan on open: a docked pane can sit
    /// open all day, and a full traversal has to run on Revit's main thread.
    ///
    /// Everything is wrapped: a pane that fails to show must say why, or it looks exactly like a
    /// dead button.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class RoundPositionsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (Application.RoundPositionsPaneViewModel is null)
                {
                    message = "The Round Positions pane was not registered when RVTuk started " +
                        "(RegisterRoundPositions is off in this build).";
                    TaskDialog.Show("RVTuk — Round Positions", message);
                    return Result.Failed;
                }

                commandData.Application.GetDockablePane(RoundPositionsPaneProvider.PaneId).Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("RVTuk — Round Positions (error)", ex.ToString());
                return Result.Failed;
            }
        }
    }
}
