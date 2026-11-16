using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.TopoTools;
using RVTuk.Revit.Shared;

namespace RVTuk.Revit.TopoTools.Commands
{
    /// <summary>
    /// Topo Tools: opens a Topo Edit session on the one selected toposolid or floor in a plan view
    /// and shows the Topo Edit panel. The button keeps its internal name
    /// (<see cref="Application.TopoToolsButtonName"/>): the contextual Modify-tab button posts it
    /// by that id (see <see cref="ContextualTopoButton"/>).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class TopoStartCommand : IExternalCommand
    {
        private const string Title = "Topo Tools";
        private const string SelectOne = "Select one toposolid or floor in a plan view, then press Topo Tools.";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var open = TopoSession.Current;
            if (open != null)
            {
                var label = TopoSessionCommands.LabelOf(open.Document.GetElement(open.TargetId), open.TargetId);
                TopoEditPanel.Show();
                TaskDialog.Show(Title, $"A Topo Edit session is already open on {label}.");
                return Result.Cancelled;
            }

            var uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null || uiDoc.ActiveView is not ViewPlan view)
            {
                TaskDialog.Show(Title, SelectOne);
                return Result.Cancelled;
            }

            var doc = uiDoc.Document;
            var ids = uiDoc.Selection.GetElementIds();
            var element = ids.Count == 1 ? doc.GetElement(ids.First()) : null;
            if (element is not (Toposolid or Floor))
            {
                TaskDialog.Show(Title, SelectOne);
                return Result.Cancelled;
            }

            var elementLabel = TopoSessionCommands.LabelOf(element, element.Id);
            Transaction? tx = null;
            try
            {
                var refusal = SlabRefusals.Of(doc, element);
                if (refusal != null)
                {
                    TaskDialog.Show(Title, $"{elementLabel}: {Sentence.WithoutFullStop(refusal)}.");
                    return Result.Cancelled;
                }

                if (SlabTarget.From(doc, element) == null)
                {
                    TaskDialog.Show(Title, $"{elementLabel}: its outline cannot be read.");
                    return Result.Cancelled;
                }

                // The styles must exist before the session: the updater restyles and labels new lines with them.
                if (!LineStyleCreator.Exists(doc, TopoLineStyle.LineStyleName) || !HeightTextType.Exists(doc))
                {
                    tx = new Transaction(doc, "Topo Tools — create styles");
                    tx.Start();
                    LineStyleCreator.EnsureExists(doc, TopoLineStyle.LineStyleName);
                    HeightTextType.EnsureExists(doc);
                    if (tx.Commit() != TransactionStatus.Committed)
                    {
                        TaskDialog.Show(Title,
                            "The Topo Tools line style and height text type could not be created — " +
                            "Revit rolled the change back. No Topo Edit session was started.");
                        return Result.Cancelled;
                    }
                }

                TopoSession.Start(doc, view, element);
                TopoEditPanel.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                if (tx != null && tx.HasStarted() && !tx.HasEnded()) tx.RollBack();

                // No half-started session: one that started before the failure (the panel would
                // not show, say) is closed again, so Topo Tools can simply be pressed again. A
                // session open before this press returned early above, so this one is ours.
                TopoSession.End();

                Application.LogDiagnostic($"Topo Tools start failed: {ex}");
                TaskDialog.Show(Title, $"{elementLabel}: the Topo Edit session could not start: {ex.Message}");
                return Result.Failed;
            }
            finally
            {
                tx?.Dispose();
            }
        }
    }
}
