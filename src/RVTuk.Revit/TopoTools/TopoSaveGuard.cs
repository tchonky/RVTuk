using System;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using RVTuk.Revit.TopoTools.Commands;

namespace RVTuk.Revit.TopoTools
{
    /// <summary>
    /// Saving or syncing the document a Topo Edit session is open on asks first: Accept, then save;
    /// Cancel the session, then save; or save as it is. Its box, "Don't ask again until this Topo
    /// Edit ends", sets <see cref="TopoSession.DontAskOnSave"/>. Esc, or closing the dialog, saves as
    /// it is, and the save is never called off: Esc at such a prompt did not stop Revit from closing
    /// (Revit 2024, 2026-11-18), so Esc means the same in every case.
    /// <para>DocumentSaving, DocumentSavingAs (every Save As, including a new project's first save)
    /// and DocumentSynchronizingWithCentral share one handler: their arguments all derive from
    /// RevitAPIPreDocEventArgs (2024–2027). Their handlers may change any document not in read-only
    /// mode, so Accept and Cancel run inside the event, on the session's document, and no view is
    /// switched. Every failure is caught and logged in one line, and the save goes on.</para>
    /// </summary>
    internal static class TopoSaveGuard
    {
        /// <summary>Hooks the save and sync events. Call once from OnStartup. Never throws.</summary>
        internal static void Install(UIControlledApplication app)
        {
            try
            {
                app.ControlledApplication.DocumentSaving += OnSaving;
                app.ControlledApplication.DocumentSavingAs += OnSaving;
                app.ControlledApplication.DocumentSynchronizingWithCentral += OnSaving;
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"Topo Edit save prompt: install failed: {ex}");
            }
        }

        /// <summary>
        /// Asks only when the session is on the document being saved, its box was not ticked, and
        /// the document is not in read-only mode (no transaction could start, so neither Accept nor
        /// Cancel could run). For a model opened from a read-only file, a plain Save cannot write the
        /// file back, so it does not ask; a Save As, the only way to keep such a model's work, asks
        /// like any other. Only a plain Save's arguments are DocumentSavingEventArgs: those of
        /// DocumentSavingAs and DocumentSynchronizingWithCentral do not derive from it (2024–2027).
        /// Not asking saves as it is.
        /// </summary>
        private static void OnSaving(object? sender, RevitAPIPreDocEventArgs e)
        {
            try
            {
                var session = TopoSession.Current;
                var doc = e.Document;
                if (session == null || !session.IsFor(doc) || session.DontAskOnSave) return;
                if (doc.IsReadOnly || (doc.IsReadOnlyFile && e is DocumentSavingEventArgs)) return;

                var label = TopoSessionCommands.LabelOf(doc.GetElement(session.TargetId), session.TargetId);
                var dialog = new TaskDialog(TopoSessionCommands.Title)
                {
                    MainInstruction = $"Topo Edit is still open on {label}",
                    MainContent = "Changes to its lines and heights since the last Update are not on it yet.",
                    VerificationText = "Don't ask again until this Topo Edit ends",
                    AllowCancellation = true,
                    // A reflexive Enter does what Esc does: saves as it is, changing nothing.
                    DefaultButton = TaskDialogResult.CommandLink3,
                };
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Accept, then save");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Cancel the session, then save");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Save as it is");

                var choice = dialog.Show();
                bool dontAskAgain = dialog.WasVerificationChecked();

                try
                {
                    // Each shows its own dialog. "Save as it is", Esc and the dialog closed do nothing.
                    if (choice == TaskDialogResult.CommandLink1) TopoSessionActions.Accept();
                    else if (choice == TaskDialogResult.CommandLink2) TopoSessionActions.Cancel();
                }
                finally
                {
                    // Also when the action threw: the session then stays open, and the box still
                    // counts. Read from the session, not from what Accept returned: Accept can return
                    // false and still have ended the session (its view or element gone).
                    if (ReferenceEquals(TopoSession.Current, session)) session.DontAskOnSave = dontAskAgain;
                }
            }
            catch (Exception ex)
            {
                Application.LogDiagnostic($"Topo Edit save prompt ({e?.GetType().Name}) failed: {ex}");
            }
        }
    }
}
