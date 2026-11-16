using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.Commands
{
    /// <summary>
    /// TEMPORARY: the Topo Tools button while the edit session is being built. The button stays
    /// registered under its internal name because the contextual Modify-tab button posts it by
    /// that id (see <see cref="ContextualTopoButton"/>). Task 9 replaces this body.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class TopoStartCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            TaskDialog.Show("Topo Tools", "The new Topo Tools is being built.");
            return Result.Succeeded;
        }
    }
}
