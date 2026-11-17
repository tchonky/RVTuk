using System;
using System.Collections.Generic;
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
            var uiDoc = commandData.Application.ActiveUIDocument;
            return TopoSession.Current != null ? AskAboutOpenSession(uiDoc) : Start(uiDoc);
        }

        /// <summary>
        /// The start, with no session open: a session on the one toposolid or floor in
        /// <paramref name="selection"/>, or in the current selection when that is null.
        /// </summary>
        private static Result Start(UIDocument? uiDoc, ICollection<ElementId>? selection = null)
        {
            if (uiDoc == null || uiDoc.ActiveView is not ViewPlan view)
            {
                TaskDialog.Show(Title, SelectOne);
                return Result.Cancelled;
            }

            var doc = uiDoc.Document;
            var ids = selection ?? uiDoc.Selection.GetElementIds();
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
                // not show, say) is closed again, so Topo Tools can simply be pressed again. The
                // start runs only while no session is open, so this one is ours.
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

        /// <summary>
        /// Topo Tools pressed during a session asks what to do with it: go to it, accept it (as ✓)
        /// or cancel it (as ✗). Closing the dialog changes nothing. Once accepting or cancelling has
        /// closed the session, a selection of one other toposolid or floor, read before asking,
        /// goes on into <see cref="Start"/>, as pressing Topo Tools with it selected then would. The
        /// Accept or Cancel stands even when that start is refused or fails.
        /// </summary>
        private static Result AskAboutOpenSession(UIDocument? uiDoc)
        {
            // A session whose view is gone has nothing to go to: TryGet closes it and says so.
            if (!TopoSessionCommands.TryGet(out var open, out var view)) return Result.Cancelled;

            // Read before asking: what to start on afterwards is what was selected when Topo Tools
            // was pressed.
            var selection = uiDoc?.Selection.GetElementIds();
            var label = TopoSessionCommands.LabelOf(open.Document.GetElement(open.TargetId), open.TargetId);
            TopoEditPanel.Show();

            var dialog = new TaskDialog(Title)
            {
                MainInstruction = $"Topo Edit is open on {label} in {view.Name}.",
                AllowCancellation = true,
                // A reflexive Enter goes to the session, which changes nothing in the model.
                DefaultButton = TaskDialogResult.CommandLink1,
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Go to it",
                "Opens that view and selects that element.");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Accept it",
                "As ✓; then, if one other toposolid or floor is selected, a session starts on it.");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Cancel it",
                "As ✗; then the same.");

            Result result;
            switch (dialog.Show())
            {
                case TaskDialogResult.CommandLink1:
                    return GoTo(uiDoc, open, view, label);
                case TaskDialogResult.CommandLink2:
                    result = TopoSessionActions.Accept() ? Result.Succeeded : Result.Cancelled;
                    break;
                case TaskDialogResult.CommandLink3:
                    result = TopoSessionActions.Cancel();
                    break;
                default:
                    return Result.Cancelled; // Esc or the dialog closed: nothing was done.
            }

            if (TopoSession.Current != null || !HoldsOneOtherSlab(uiDoc, selection, open)) return result;

            // Revit undoes everything a command committed when it returns anything but Succeeded:
            // a refused or failed restart must not undo the Accept or Cancel that already ran.
            var started = Start(uiDoc, selection);
            return result == Result.Succeeded ? Result.Succeeded : started;
        }

        /// <summary>Go to it: the session's view becomes the active one, with the session's element
        /// selected.</summary>
        private static Result GoTo(UIDocument? uiDoc, TopoSession open, ViewPlan view, string label)
        {
            // Activating another document ends the session, so this should not happen; if it does,
            // there is nothing in the active document to switch to.
            if (uiDoc == null || !open.IsFor(uiDoc.Document))
            {
                TaskDialog.Show(Title,
                    $"Topo Edit is open on {label} in another document. Switch to that document, then press Topo Tools again.");
                return Result.Cancelled;
            }

            try
            {
                uiDoc.ActiveView = view;
                if (open.Document.GetElement(open.TargetId) != null)
                    uiDoc.Selection.SetElementIds(new List<ElementId> { open.TargetId });
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"Topo Tools go to session failed: {ex}");
                TaskDialog.Show(Title, $"Could not go to {label} in {view.Name}: {ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>True when <paramref name="selection"/> is one toposolid or floor that is still in
        /// the document and is not the target of the session just closed.</summary>
        private static bool HoldsOneOtherSlab(UIDocument? uiDoc, ICollection<ElementId>? selection, TopoSession closed)
        {
            if (uiDoc == null || selection == null || selection.Count != 1) return false;

            var id = selection.First();
            if (closed.IsFor(uiDoc.Document) && id == closed.TargetId) return false;
            return uiDoc.Document.GetElement(id) is Toposolid or Floor;
        }
    }
}
