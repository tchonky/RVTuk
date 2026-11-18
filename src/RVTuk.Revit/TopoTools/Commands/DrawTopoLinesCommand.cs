using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.Commands
{
    /// <summary>
    /// Draw Lines: arms the session (<see cref="TopoSession.Armed"/>) and starts Revit's own Detail
    /// Line tool in the session's view, opening that view first when another view of the session's
    /// document is active. There is no API to preset the tool's line style, so the session's updater
    /// restyles each new line in the session's view instead. A press that returns Cancelled leaves
    /// the session as armed or disarmed as it was.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class DrawTopoLinesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (!TopoSessionCommands.TryGet(out var session, out var view)) return Result.Cancelled;

            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;

            // Activating another document ends the session, so this should not happen; if it does,
            // the session's view cannot be opened from here: only a view of the active document can.
            if (uiDoc == null || !session.IsFor(uiDoc.Document))
            {
                TaskDialog.Show(TopoSessionCommands.Title, GoBack(view));
                return Result.Cancelled;
            }

            // Pressed in another view: the session's view is opened first, so the tool starts there.
            // A command may change the active view while it has no transaction open; when Revit
            // refuses all the same (a read-only document, say), the user is told to go back.
            if (uiDoc.ActiveView?.Id != session.ViewId)
            {
                try
                {
                    uiDoc.ActiveView = view;
                }
                catch (Exception ex)
                {
                    Application.LogDiagnostic($"Topo Draw Lines: opening the session's view failed: {ex}");
                    TaskDialog.Show(TopoSessionCommands.Title, GoBack(view));
                    return Result.Cancelled;
                }
            }

            var detailLine = RevitCommandId.LookupPostableCommandId(PostableCommand.DetailLine);
            if (detailLine == null || !uiApp.CanPostCommand(detailLine))
            {
                TaskDialog.Show(TopoSessionCommands.Title, "Revit cannot start the Detail Line tool right now.");
                return Result.Cancelled;
            }

            // The posted tool starts once this command has returned, so the session is armed before
            // its first line.
            session.Armed = true;
            uiApp.PostCommand(detailLine);
            return Result.Succeeded;
        }

        private static string GoBack(View view) => $"Go back to {view.Name} to draw topo lines.";
    }
}
