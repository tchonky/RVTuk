using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.Commands
{
    /// <summary>
    /// Draw Lines: starts Revit's own Detail Line tool. There is no API to preset its line style,
    /// so the session's updater restyles each new line in the session's view instead.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class DrawTopoLinesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (!TopoSessionCommands.TryGet(out var session, out var view)) return Result.Cancelled;

            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;
            if (uiDoc == null || !session.IsFor(uiDoc.Document) || uiDoc.ActiveView?.Id != session.ViewId)
            {
                TaskDialog.Show(TopoSessionCommands.Title, $"Go back to {view.Name} to draw topo lines.");
                return Result.Cancelled;
            }

            var detailLine = RevitCommandId.LookupPostableCommandId(PostableCommand.DetailLine);
            if (detailLine == null || !uiApp.CanPostCommand(detailLine))
            {
                TaskDialog.Show(TopoSessionCommands.Title, "Revit cannot start the Detail Line tool right now.");
                return Result.Cancelled;
            }

            uiApp.PostCommand(detailLine);
            return Result.Succeeded;
        }
    }
}
