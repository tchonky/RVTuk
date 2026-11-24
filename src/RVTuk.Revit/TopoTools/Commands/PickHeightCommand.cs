using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using PickCancelled = Autodesk.Revit.Exceptions.OperationCanceledException;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace RVTuk.Revit.TopoTools.Commands
{
    /// <summary>
    /// Pick: does what the Height box holds ("43" sets, "+0.1" raises, "-0.1" lowers) to each topo
    /// line clicked, one undo step per click, until Esc. Lines already selected when it is pressed
    /// get it first, as one step. Clicking a line's height text counts as clicking the line; a
    /// line clicked again gets it again, so "+0.1" twice raises 0.20.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class PickHeightCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (!TopoSessionCommands.TryGet(out var session, out var view)) return Result.Cancelled;

            if (!TopoEditPanel.TryReadInput(out var input))
            {
                TaskDialog.Show(TopoSessionCommands.Title,
                    "Type in the Height box first and press Enter: a height (43) to give each line you click, " +
                    "or a move (+0.1, -0.1) to raise or lower it on each click.");
                return Result.Cancelled;
            }

            var uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null || !session.IsFor(uiDoc.Document)) return Result.Cancelled;
            var doc = uiDoc.Document;

            if (!OpenSessionView(uiDoc, view)) return Result.Cancelled;

            try
            {
                string note = "";

                var preselected = uiDoc.Selection.GetElementIds();
                if (preselected.Count > 0)
                {
                    var lineIds = TopoEditPanel.LinesOf(doc, view, preselected, out var notes);
                    if (lineIds.Count > 0)
                    {
                        if (!TopoEditPanel.ApplyHeight(doc, view, session, input, lineIds, notes, out int leftAlone))
                            note = TopoSessionCommands.RolledBack + " ";
                        else if (leftAlone > 0)
                            note = (leftAlone == 1 ? "1 selected line keeps its height" : $"{leftAlone} selected lines keep their height") + ". ";
                    }
                    uiDoc.Selection.SetElementIds(new List<ElementId>());
                }

                while (true)
                {
                    Reference picked;
                    try
                    {
                        picked = uiDoc.Selection.PickObject(ObjectType.Element, new TopoPickFilter(doc, view),
                            $"{note}Click topo lines to {input.Action}. Esc to finish.");
                    }
                    catch (PickCancelled)
                    {
                        return Result.Succeeded;
                    }

                    var ids = TopoEditPanel.LinesOf(doc, view, new List<ElementId> { picked.ElementId }, out var pickNotes);
                    if (ids.Count == 0)
                    {
                        note = "";
                        continue;
                    }

                    if (!TopoEditPanel.ApplyHeight(doc, view, session, input, ids, pickNotes, out int keptAlone))
                        note = TopoSessionCommands.RolledBack + " ";
                    else
                        note = keptAlone > 0 ? $"That line keeps its height: {TopoEditPanel.LeftAloneReason}. " : "";
                }
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"Topo Pick failed: {ex}");
                TaskDialog.Show(TopoSessionCommands.Title, $"Pick stopped: {ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>
        /// Clicks land in the active view: the session's view is opened first, as Draw Lines does.
        /// False, after saying so, when it cannot be opened.
        /// </summary>
        internal static bool OpenSessionView(UIDocument uiDoc, View view)
        {
            if (uiDoc.ActiveView?.Id == view.Id) return true;
            try
            {
                uiDoc.ActiveView = view;
                return true;
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"Topo Pick: opening the session's view failed: {ex}");
                TaskDialog.Show(TopoSessionCommands.Title, $"Open the view \"{view.Name}\" and press Pick again.");
                return false;
            }
        }

        /// <summary>Only topo lines of the session's view and their height texts can be clicked.</summary>
        internal sealed class TopoPickFilter : ISelectionFilter
        {
            private readonly HashSet<long> _allowed;

            public TopoPickFilter(Document doc, View view)
            {
                var notes = HeightTexts.ReadNotes(doc, view, out var lineIds);
                _allowed = new HashSet<long>(lineIds.Concat(notes.Values.Select(text => text.Id.Value)));
            }

            public bool AllowElement(Element elem) => elem != null && _allowed.Contains(elem.Id.Value);

            public bool AllowReference(Reference reference, XYZ position) => false;
        }
    }
}
