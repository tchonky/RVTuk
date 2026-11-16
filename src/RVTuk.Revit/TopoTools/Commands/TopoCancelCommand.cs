using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.Commands
{
    /// <summary>
    /// Cancel: closes the session and undoes its edits as far as its records allow — no
    /// transaction stays open across a session, so this is not a rollback. In one transaction,
    /// "Topo Cancel": the session's new topo lines are deleted and their points taken off the
    /// target; the session's other new height texts are deleted; height texts typed over are put
    /// back. Points that Update applied for lines that still exist stay (Ctrl+Z undoes them).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class TopoCancelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var session = TopoSession.Current;
            if (session == null)
            {
                TopoEditPanel.Hide();
                TaskDialog.Show(TopoSessionCommands.Title, "No Topo Edit session is open.");
                return Result.Cancelled;
            }

            // End first, so the updater is off: the deletes and restores below must not be
            // restyled, labelled or taken as typed heights.
            TopoSession.End();

            var doc = session.Document;
            int linesDeleted, pointsRemoved = 0, textsDeleted, textsRestored = 0;

            using (var tx = new Transaction(doc, "Topo Cancel"))
            {
                tx.Start();
                try
                {
                    // 1. The session's new topo lines that still exist (the user may have undone some).
                    var lineIds = session.Created
                        .Where(id => doc.GetElement(id) is CurveElement line && TopoLineStyle.IsTopoLine(line))
                        .ToList();
                    if (lineIds.Count > 0) doc.Delete(lineIds);
                    linesDeleted = lineIds.Count;

                    // 2. Their points on the target, if it still exists and has any of them. Only
                    // these lines count as gone: other lines' points are not touched.
                    var removed = new HashSet<long>(lineIds.Select(id => id.Value));
                    var element = doc.GetElement(session.TargetId);
                    if (element != null && removed.Count > 0 &&
                        TopoLedgerStore.Read(element).Keys.Any(removed.Contains) &&
                        SlabTarget.From(doc, element) is SlabTarget target)
                    {
                        var outcome = TopoPointApplier.Apply(
                            target, removed, new Dictionary<long, LinePointsZ>(),
                            lineId => !removed.Contains(lineId));
                        pointsRemoved = outcome.Removed;
                    }

                    // 3. The session's other new texts still there (deleting a line keeps its text).
                    var textIds = session.Created.Where(id => doc.GetElement(id) is TextNote).ToList();
                    if (textIds.Count > 0) doc.Delete(textIds);
                    textsDeleted = textIds.Count;

                    // 4. Height texts typed over during the session, put back.
                    foreach (var pair in session.TextSnapshot)
                    {
                        if (doc.GetElement(pair.Key) is not TextNote note) continue;
                        if ((note.Text ?? "").Trim() == pair.Value.Trim()) continue;
                        note.Text = pair.Value;
                        textsRestored++;
                    }

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    Application.LogDiagnostic($"Topo Cancel failed: {ex}");
                    TaskDialog.Show(TopoSessionCommands.Title,
                        $"The session is closed, but undoing its edits failed and nothing was undone: {ex.Message}");
                    return Result.Failed;
                }
            }

            TaskDialog.Show(TopoSessionCommands.Title,
                "Topo Edit cancelled. " +
                $"Deleted {Count(linesDeleted, "new line", "new lines")} and {Count(textsDeleted, "new height text", "new height texts")}, " +
                $"removed {Count(pointsRemoved, "point", "points")} of those lines, " +
                $"put back {Count(textsRestored, "changed height", "changed heights")}.\n\n" +
                "Points that Update applied for lines that still exist stay in place; Ctrl+Z undoes them.");
            return Result.Succeeded;
        }

        private static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";
    }
}
