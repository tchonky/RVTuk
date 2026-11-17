using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.Commands
{
    /// <summary>Accept (✓) on the Topo Edit panel: <see cref="TopoSessionActions.Accept"/>.</summary>
    [Transaction(TransactionMode.Manual)]
    public class TopoAcceptCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            TopoSessionActions.Accept() ? Result.Succeeded : Result.Cancelled;
    }
}
