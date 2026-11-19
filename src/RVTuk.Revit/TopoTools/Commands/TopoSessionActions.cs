using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools.Commands
{
    /// <summary>
    /// Accept (✓) and Cancel (✗) as plain calls, so that what runs them without their buttons —
    /// Topo Tools pressed during a session, the save prompt — does exactly what the buttons do.
    /// </summary>
    internal static class TopoSessionActions
    {
        /// <summary>
        /// Accept: Update, always showing its summary (the Update button shows it only when something
        /// needs attention), then close the session. Lines and texts stay in the view. The session
        /// closes also when the update reported problems (its summary says what was skipped), but not
        /// when the update was not committed (refused, rolled back or failed) — then the user can fix
        /// it, or Cancel.
        /// </summary>
        /// <returns>True when the update was committed, its summary shown, and the session closed.
        /// False when the update was not committed: the session then stays open, unless there was
        /// none or the update found its view or its element gone and closed it.</returns>
        internal static bool Accept()
        {
            if (!TopoUpdateCommand.Run(alwaysShowSummary: true)) return false;

            TopoSession.End();
            return true;
        }

        /// <summary>
        /// Cancel: closes the session and undoes its edits as far as its records allow — no
        /// transaction stays open across a session, so this is not a rollback. In one transaction,
        /// "Topo Cancel": the session's new topo lines are deleted and their points taken off the
        /// target; the session's other new height texts are deleted; height texts typed over are put
        /// back; the outline corners the session's Updates moved go back to where they were at its
        /// start (<see cref="RestoreCorners"/>). Points that Update applied for lines that still
        /// exist stay; the next Update reconciles them. The session ends only once that transaction
        /// has committed, so a failed Cancel can be pressed again.
        /// </summary>
        internal static Result Cancel()
        {
            var session = TopoSession.Current;
            if (session == null)
            {
                TopoEditPanel.Hide();
                TaskDialog.Show(TopoSessionCommands.Title, "No Topo Edit session is open.");
                return Result.Cancelled;
            }

            var doc = session.Document;
            int linesDeleted, pointsRemoved = 0, textsDeleted, textsRestored = 0, cornersRestored;

            using (var tx = new Transaction(doc, "Topo Cancel"))
            {
                try
                {
                    // The updater goes off first: the deletes and restores below must not be
                    // restyled, labelled or taken as typed heights. Inside the try, so a Start that
                    // throws still switches it back on. The session ends only after the commit.
                    TopoSessionUpdater.Disable();
                    tx.Start();

                    // 1. The session's new topo lines that still exist (the user may have undone some).
                    // Created keeps ids across an undo (updaters do not run on undo) and Revit may
                    // give an undone id to a new element, so each id must still be a topo detail
                    // line of the session's view.
                    var lineIds = session.Created
                        .Where(id => doc.GetElement(id) is CurveElement line
                            && line.CurveElementType == CurveElementType.DetailCurve
                            && line.OwnerViewId == session.ViewId
                            && TopoLineStyle.IsTopoLine(line))
                        .ToList();
                    if (lineIds.Count > 0) doc.Delete(lineIds);
                    linesDeleted = lineIds.Count;

                    // 2. Their points on the target, if it still exists and has any of them. Only
                    // these lines count as gone: other lines' points are not touched.
                    var removed = new HashSet<long>(lineIds.Select(id => id.Value));
                    var element = doc.GetElement(session.TargetId);

                    // Read before this step, which can switch shape editing on (see step 5).
                    bool shapedBefore = element != null && SlabShape.EditorOf(element)?.IsEnabled == true;

                    if (element != null && removed.Count > 0 &&
                        TopoLedgerStore.Read(element).Keys.Any(removed.Contains) &&
                        SlabTarget.From(doc, element) is SlabTarget target)
                    {
                        var outcome = TopoPointApplier.Apply(
                            target, removed, new Dictionary<long, LinePointsZ>(),
                            lineId => !removed.Contains(lineId), corners: null);
                        pointsRemoved = outcome.Removed;
                    }

                    // 3. The session's other new texts still there (deleting a line keeps its text),
                    // again only where the id is still a height text of the session's view.
                    var textIds = session.Created
                        .Where(id => doc.GetElement(id) is TextNote note
                            && note.OwnerViewId == session.ViewId
                            && HeightTextType.IsHeightText(note))
                        .ToList();
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

                    // 5. The outline corners the session's Updates moved, back where they were at
                    // its start — from the start's record, never by the rule.
                    cornersRestored = RestoreCorners(doc, session, shapedBefore);

                    if (tx.Commit() != TransactionStatus.Committed)
                    {
                        TopoSessionUpdater.Enable();
                        TaskDialog.Show(TopoSessionCommands.Title,
                            TopoSessionCommands.RolledBack + " The Topo Edit session is still open; press Cancel to try again.");
                        return Result.Cancelled;
                    }
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    TopoSessionUpdater.Enable();
                    Application.LogDiagnostic($"Topo Cancel failed: {ex}");
                    TaskDialog.Show(TopoSessionCommands.Title,
                        $"Undoing the session's edits failed and nothing was undone: {ex.Message}\n\n" +
                        "The Topo Edit session is still open; press Cancel to try again.");
                    return Result.Failed;
                }
            }

            TopoSession.End();

            TaskDialog.Show(TopoSessionCommands.Title,
                "Topo Edit cancelled. " +
                $"Deleted {Count(linesDeleted, "new line", "new lines")} and {Count(textsDeleted, "new height text", "new height texts")}, " +
                $"removed {Count(pointsRemoved, "point", "points")} of those lines, " +
                $"put back {PutBack(textsRestored, cornersRestored)}.\n\n" +
                "The surface keeps the heights from the last Update for the lines that still exist; " +
                "the next Update reconciles them.");
            return Result.Succeeded;
        }

        /// <summary>
        /// Cancel's corner step, inside its transaction: when the target still exists, was shaped
        /// when Cancel began, and its corner record may have changed since the start of the session
        /// (<see cref="OutlineCornerPlanner.CancelHasCornerWork"/>), each corner whose record changed goes back to its height at the start
        /// (<see cref="TopoSession.CornerStart"/>'s set height), or to its own height when the start
        /// had no record for it (<see cref="OutlineCornerPlanner.Restore"/>); corners moved by hand
        /// are left alone. No line is read, so no corner the session left alone moves. The record
        /// written is the start's, except that a corner that could not be put back keeps a record
        /// of where it stands, so the next Update does not take it for a hand edit.
        /// </summary>
        /// <param name="shapedBefore">Whether the target's shape editing was on before Cancel's
        /// step 2, which can switch it on to take line points out.</param>
        /// <returns>How many corners reached the height they were put back to.</returns>
        private static int RestoreCorners(Document doc, TopoSession session, bool shapedBefore)
        {
            var element = doc.GetElement(session.TargetId);
            if (element == null) return 0;

            // Not only "the record text is unchanged": an Update can write a record that was stale
            // at the start again, bit for bit, after raising its corners once more.
            if (!OutlineCornerPlanner.CancelHasCornerWork(
                    OutlineCornerStore.Raw(element), session.CornerSnapshot, session.CornerStart))
                return 0;

            // Plain when Cancel began: an Update leaves an element plain only when it raised no
            // corner, so this is Reset Shape by hand during the session. Every corner then stands
            // at the element's top, its own height, and Cancel leaves it there rather than raising
            // corners the user reset; the record left behind means nothing and the next Update
            // drops it. Step 2 may have switched shape editing on since, hence the state read
            // before it.
            var editor = SlabShape.EditorOf(element);
            if (editor == null || !shapedBefore || !editor.IsEnabled) return 0;

            // The deletes above changed the shape: vertices are read after a regenerate
            // (RaiseVertexTo does not regenerate first).
            doc.Regenerate();
            var vertices = new List<XyzPoint>();
            foreach (SlabShapeVertex vertex in editor.SlabShapeVertices)
                vertices.Add(SlabShape.ToPoint(vertex.Position));

            var plan = OutlineCornerPlanner.Restore(
                session.CornerStart, OutlineCornerStore.Read(element), TopoSession.OutlineVertices(element, vertices));

            // Each corner reports the Z it reached; after a failed raise, where it still stands.
            double? reference = null;
            var raised = new Dictionary<(double X, double Y), double>();
            foreach (var raise in plan.Raises)
                raised[(raise.At.X, raise.At.Y)] =
                    TopoPointApplier.RaiseVertexTo(doc, element, editor, raise.At, raise.ToZ, ref reference)?.Z ?? raise.At.Z;

            var putBack = new Dictionary<(double X, double Y), double>();
            foreach (var back in plan.PutBacks)
                putBack[(back.At.X, back.At.Y)] =
                    TopoPointApplier.RaiseVertexTo(doc, element, editor, back.At, back.ToZ, ref reference)?.Z ?? back.At.Z;

            var outcome = OutlineCornerPlanner.Settle(plan, raised, putBack);
            if (OutlineCornerRecordCodec.Encode(outcome.Records) != OutlineCornerStore.Raw(element))
                OutlineCornerStore.Write(element, outcome.Records);

            return plan.Raises.Count + plan.PutBacks.Count - outcome.Lost;
        }

        /// <summary>What Cancel put back: the changed heights, as always, and the outline corners
        /// only when there are any — then without "0 changed heights".</summary>
        private static string PutBack(int texts, int corners)
        {
            string heights = Count(texts, "changed height", "changed heights");
            if (corners == 0) return heights;

            string outline = Count(corners, "outline corner", "outline corners");
            return texts == 0 ? outline : $"{heights} and {outline}";
        }

        private static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";
    }
}
