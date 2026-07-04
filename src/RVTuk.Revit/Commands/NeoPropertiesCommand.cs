using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Revit.NeoProperties;

namespace RVTuk.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class NeoPropertiesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var pane = commandData.Application.GetDockablePane(NeoPropertiesPaneProvider.PaneId);
            pane.Show();
            return Result.Succeeded;
        }
    }
}
