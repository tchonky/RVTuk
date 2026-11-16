using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.Commands
{
    /// <summary>
    /// Accept: Update, then close the session. Lines and texts stay in the view. The session closes
    /// also when the update reported problems (its summary says what was skipped), but not when the
    /// update could not run at all — then the user can fix it, or Cancel.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class TopoAcceptCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (!TopoUpdateCommand.Run(commandData.Application)) return Result.Cancelled;

            TopoSession.End();
            return Result.Succeeded;
        }
    }
}
