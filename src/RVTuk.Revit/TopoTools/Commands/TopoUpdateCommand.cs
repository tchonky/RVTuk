using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools.Commands
{
    /// <summary>Update: labels the view's topo lines, then applies them to the session's element.</summary>
    [Transaction(TransactionMode.Manual)]
    public class TopoUpdateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            Run(alwaysShowSummary: false) ? Result.Succeeded : Result.Cancelled;

        /// <summary>
        /// The Update routine, shared with Accept. One transaction, "Topo Update": Height Labels,
        /// then the apply, reading the heights after labelling. The target is read fresh; if it was
        /// deleted the session ends. A committed update shows its summary when
        /// <paramref name="alwaysShowSummary"/> is true or the report's
        /// <see cref="TopoSessionReport.ShowAfterUpdate"/> is, and nothing otherwise. Whatever stops
        /// it — no session, its view or element gone, a refusal, a rolled-back commit, a failure —
        /// still shows its message. Returns true when the update was committed — also when its
        /// report has problems (the summary names them).
        /// </summary>
        /// <param name="alwaysShowSummary">True for Accept, which always shows the summary; false for
        /// the Update button, which shows it only when something needs attention.</param>
        public static bool Run(bool alwaysShowSummary)
        {
            if (!TopoSessionCommands.TryGet(out var session, out var view)) return false;

            var doc = session.Document;
            var element = doc.GetElement(session.TargetId);
            var label = TopoSessionCommands.LabelOf(element, session.TargetId);
            if (element == null)
            {
                TopoSession.End();
                TaskDialog.Show(TopoSessionCommands.Title,
                    $"{label}, which this session was shaping, no longer exists. The Topo Edit session is closed.");
                return false;
            }

            var refusal = SlabRefusals.Of(doc, element);
            if (refusal != null)
            {
                TaskDialog.Show(TopoSessionCommands.Title, $"{label}: {Sentence.WithoutFullStop(refusal)}. Nothing was applied.");
                return false;
            }

            var target = SlabTarget.From(doc, element);
            if (target == null)
            {
                TaskDialog.Show(TopoSessionCommands.Title, $"{label}: its outline cannot be read. Nothing was applied.");
                return false;
            }

            TopoSessionReport report;
            using (var tx = new Transaction(doc, "Topo Update"))
            {
                tx.Start();
                try
                {
                    int unlinked = HeightTexts.Label(doc, view, session.Created);
                    report = TopoSessionRunner.Apply(
                        doc, view, target, TopoEditPanel.SpacingFeet, HeightTexts.ReadHeights(doc, view));
                    report.UnlinkedTexts = unlinked;

                    // Failure processing (the user cancelling a Revit error dialog, say) can roll
                    // the commit back: then nothing was applied, and Accept must keep the session.
                    if (tx.Commit() != TransactionStatus.Committed)
                    {
                        TaskDialog.Show(TopoSessionCommands.Title, TopoSessionCommands.RolledBack);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                    Application.LogDiagnostic($"Topo Update failed: {ex}");
                    TaskDialog.Show(TopoSessionCommands.Title, $"The update failed and was undone: {ex.Message}");
                    return false;
                }
            }

            // Update keeps quiet unless something needs attention or nothing was applied (a silent
            // Update would then hide why nothing changed). Accept always shows the summary.
            if (!alwaysShowSummary && !report.ShowAfterUpdate) return true;

            // Heights are measured like a Survey Point level, not from the marker, so the first
            // summary shown in a session names the marker's own height; the report leaves the
            // note out when it rounds to 0. Read here, once a summary is sure to show, so a quiet
            // Update does not use the note up. One read per session, even a failed one.
            if (!session.MarkerNoted)
            {
                try
                {
                    report.SurveyPointMarkerMetres =
                        BasePoint.GetSurveyPoint(doc).SharedPosition.Z * SurveyHeightText.MetresPerFoot;
                }
                catch (Exception ex)
                {
                    Application.LogDiagnostic($"Topo Update: reading the Survey Point marker failed: {ex}");
                }
                session.MarkerNoted = true;
            }

            // Revit's UI API has no supported way to write the status bar, so the summary is a
            // dialog: a warning one when the report names problems, a plain one otherwise.
            var dialog = new TaskDialog(TopoSessionCommands.Title)
            {
                MainInstruction = report.HasProblems ? $"{label}: updated, with problems" : $"{label}: updated",
                MainContent = report.Summarise(),
                MainIcon = report.HasProblems ? TaskDialogIcon.TaskDialogIconWarning : TaskDialogIcon.TaskDialogIconNone,
            };
            dialog.Show();
            return true;
        }
    }
}
