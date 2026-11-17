using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.Commands
{
    /// <summary>Cancel (✗) on the Topo Edit panel: <see cref="TopoSessionActions.Cancel"/>.</summary>
    [Transaction(TransactionMode.Manual)]
    public class TopoCancelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            TopoSessionActions.Cancel();
    }
}
