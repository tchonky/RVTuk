using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.Commands
{
    /// <summary>
    /// Floor ⇄ Topo: converts the selected floors to toposolids and the selected toposolids to
    /// floors. The pane's former ⇄ button, now a plain command; <see cref="ConvertRunner"/> does
    /// the work and its summary is shown in a TaskDialog.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class FloorTopoConvertCommand : IExternalCommand
    {
        private const string Title = "Floor ⇄ Topo";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null)
            {
                TaskDialog.Show(Title, "No document is open.");
                return Result.Cancelled;
            }

            TaskDialog.Show(Title, ConvertRunner.Run(uiDoc));
            return Result.Succeeded;
        }
    }
}
