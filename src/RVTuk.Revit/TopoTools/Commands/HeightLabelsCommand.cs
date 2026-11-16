using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.Commands
{
    /// <summary>Height Labels: one height text per topo line in the session's view, re-centred.</summary>
    [Transaction(TransactionMode.Manual)]
    public class HeightLabelsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (!TopoSessionCommands.TryGet(out var session, out var view)) return Result.Cancelled;

            int unlinked;
            using (var tx = new Transaction(session.Document, "Topo Height Labels"))
            {
                tx.Start();
                unlinked = HeightTexts.Label(session.Document, view, session.Created);
                if (tx.Commit() != TransactionStatus.Committed)
                {
                    TaskDialog.Show(TopoSessionCommands.Title, TopoSessionCommands.RolledBack);
                    return Result.Cancelled;
                }
            }

            if (unlinked > 0)
            {
                TaskDialog.Show(TopoSessionCommands.Title, unlinked == 1
                    ? "1 height text is not linked to any line and was left alone."
                    : $"{unlinked} height texts are not linked to any line and were left alone.");
            }
            return Result.Succeeded;
        }
    }
}
