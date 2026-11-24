using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RVTuk.Core.TopoTools;
using PickCancelled = Autodesk.Revit.Exceptions.OperationCanceledException;

namespace RVTuk.Revit.TopoTools.Commands
{
    /// <summary>
    /// Pick in Steps: with a step in the Height box ("+0.1", "-0.1"), the first topo line clicked
    /// starts the run and keeps its height; each next line clicked takes the height of the line
    /// before plus the step, whatever height it had (<see cref="HeightSteps"/>). One undo step per
    /// click, until Esc. Exactly one line selected when it is pressed is the start.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class PickStepsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (!TopoSessionCommands.TryGet(out var session, out var view)) return Result.Cancelled;

            if (!HeightSteps.TryCreate(TopoEditPanel.BoxText, out var steps))
            {
                TaskDialog.Show(TopoSessionCommands.Title,
                    "Pick in Steps needs a step in the Height box: +0.1 to go up 10 cm from line to line, " +
                    "-0.1 to go down. Type it and press Enter, then Pick in Steps again.");
                return Result.Cancelled;
            }

            var uiDoc = commandData.Application.ActiveUIDocument;
            if (uiDoc == null || !session.IsFor(uiDoc.Document)) return Result.Cancelled;
            var doc = uiDoc.Document;
            if (!PickHeightCommand.OpenSessionView(uiDoc, view)) return Result.Cancelled;

            try
            {
                string note = "";

                // One line selected: the run starts from it. More than one has no order to step in.
                var preselected = uiDoc.Selection.GetElementIds();
                if (preselected.Count > 0)
                {
                    var lineIds = TopoEditPanel.LinesOf(doc, view, preselected, out var notes);
                    if (lineIds.Count == 1)
                    {
                        notes.TryGetValue(lineIds[0], out var startNote);
                        if (!steps.Start(startNote?.Text)) note = "The selected line has no readable height. ";
                    }
                    else if (lineIds.Count > 1)
                        note = "Several lines were selected: click the one to start from. ";
                    uiDoc.Selection.SetElementIds(new List<ElementId>());
                }

                while (true)
                {
                    string prompt = steps.Last == null
                        ? $"{note}Click the line to start from: it keeps its height. Esc to finish."
                        : $"{note}At {steps.Last}: click the next line to make it {steps.NextLabel ?? "(out of range)"}. Esc to finish.";

                    Reference picked;
                    try
                    {
                        picked = uiDoc.Selection.PickObject(ObjectType.Element,
                            new PickHeightCommand.TopoPickFilter(doc, view), prompt);
                    }
                    catch (PickCancelled)
                    {
                        return Result.Succeeded;
                    }

                    var ids = TopoEditPanel.LinesOf(doc, view, new List<ElementId> { picked.ElementId }, out var pickNotes);
                    note = "";
                    if (ids.Count == 0) continue;
                    pickNotes.TryGetValue(ids[0], out var pickedNote);

                    if (steps.Last == null)
                    {
                        if (!steps.Start(pickedNote?.Text)) note = "That line has no readable height. ";
                        continue;
                    }

                    var next = steps.NextLabel;
                    if (next == null || !HeightBoxInput.TryParse("=" + next, out var set))
                    {
                        note = $"That line keeps its height: {TopoEditPanel.LeftAloneReason}. ";
                        continue;
                    }

                    if (TopoEditPanel.ApplyHeight(doc, view, session, set, ids, pickNotes, out _))
                        steps.Accept(next);
                    else
                        note = TopoSessionCommands.RolledBack + " ";
                }
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"Topo Pick in Steps failed: {ex}");
                TaskDialog.Show(TopoSessionCommands.Title, $"Pick in Steps stopped: {ex.Message}");
                return Result.Failed;
            }
        }
    }
}
