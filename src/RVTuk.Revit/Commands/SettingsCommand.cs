using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.UI.Views;

namespace RVTuk.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class SettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var window = new SettingsWindow();
            window.ShowDialog();
            return Result.Succeeded;
        }
    }
}
