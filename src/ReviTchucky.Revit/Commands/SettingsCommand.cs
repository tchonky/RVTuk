using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ReviTchucky.UI.Views;

namespace ReviTchucky.Revit.Commands
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
