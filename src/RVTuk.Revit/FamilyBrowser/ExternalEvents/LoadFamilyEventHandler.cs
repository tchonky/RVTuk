using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using RVTuk.Core.FamilyBrowser.Util;
using RVTuk.Revit.FamilyBrowser;

namespace RVTuk.Revit.FamilyBrowser.ExternalEvents
{
    public class LoadFamilyEventHandler : IExternalEventHandler
    {
        private readonly RequestGate _gate = new();

        public string? FamilyPath { get; set; }
        /// <summary>Update All: the project the user confirmed; null = load into whatever is active.</summary>
        public string? ExpectedDocumentKey { get; set; }
        public bool Success { get; private set; }
        public string? ErrorMessage { get; private set; }

        // Twin: FamilyBrowserViewModel.ProjectChangedMessage (the UI cannot see this one).
        internal const string ProjectChangedMessage = "The active project changed. Press ⟳ and try again.";

        public void Prepare(string familyPath, string? expectedDocumentKey)
        {
            FamilyPath = familyPath;
            ExpectedDocumentKey = expectedDocumentKey;
            Success = false;
            ErrorMessage = null;
            _gate.Begin();
        }

        public void WaitForCompletion() => _gate.Wait(RequestGate.StartTimeout);

        public void Execute(UIApplication app)
        {
            if (!_gate.TryStart()) return; // withdrawn: the caller already reported "Revit is busy"
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null || FamilyPath == null)
                {
                    ErrorMessage = "No active document.";
                    return;
                }
                // Open in Family Editor leaves a family active while the browser stays clickable;
                // loading into it would nest the family (and save into the library .rfa).
                if (doc.IsFamilyDocument)
                {
                    ErrorMessage = FamilyDocumentActive;
                    return;
                }
                if (ExpectedDocumentKey != null && ProjectKey.Of(doc) != ExpectedDocumentKey)
                {
                    ErrorMessage = ProjectChangedMessage;
                    return;
                }

                var options = new KeepValuesLoadOptions();
                using var tx = new Transaction(doc, "Load Family");
                tx.Start();
                bool loaded;
                Family? family;
                // This load notes its own source below, in the same transaction (one undo step).
                FamilyLoadWatch.SuppressDepth++;
                try { loaded = doc.LoadFamily(FamilyPath, options, out family); }
                finally { FamilyLoadWatch.SuppressDepth--; }
                // An identical family already in the project: Revit returns false without asking
                // the load options. That is "already current", not a failure.
                Success = loaded || (!options.FamilyFoundCalled && IsLoaded(doc, FamilyPath));
                if (Success) NoteSource(doc, family, FamilyPath);
                tx.Commit();
                if (!Success)
                    ErrorMessage = "Revit did not load the family (it may be invalid, or the load was rejected).";
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                _gate.Complete();
            }
        }

        private static bool IsLoaded(Document doc, string familyPath) => FindByFileName(doc, familyPath) != null;

        private static Family? FindByFileName(Document doc, string familyPath)
        {
            var name = FamilyFileName.WithoutRfaExtension(Path.GetFileName(familyPath));
            return new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>()
                .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        // Notes the library file on the family, so ⟳ can tell later whether it changed since. Best
        // effort: a family that can't take the note (someone else owns it in a workshared model)
        // just shows as "can't tell".
        private static void NoteSource(Document doc, Family? family, string familyPath)
        {
            try
            {
                family ??= FindByFileName(doc, familyPath);
                if (family == null) return;
                if (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, family.Id) == CheckoutStatus.OwnedByOtherUser)
                    return;
                string? root = null;
                try { root = Core.Shared.Config.ConfigManager.LoadConfig().LibraryFolderPath; } catch { }
                var relative = FamilySourceStamp.LibraryRelativePath(familyPath, root);
                if (relative != null) FamilySourceStamp.Write(family, new FileInfo(familyPath), relative);
            }
            catch (Exception ex) { RVTuk.Revit.Application.LogDiagnostic($"Family source note failed: {ex}"); }
        }

        public string GetName() => "RVTuk.LoadFamilyEventHandler";

        internal const string FamilyDocumentActive =
            "The active window is a family in the Family Editor. Switch to the project window and try again.";

        // The project's values always win (decided 2026-11-18): a library update brings new
        // geometry and parameters but never resets type values the project team changed.
        private class KeepValuesLoadOptions : IFamilyLoadOptions
        {
            public bool FamilyFoundCalled { get; private set; }

            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                FamilyFoundCalled = true;
                overwriteParameterValues = false;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
                out FamilySource source, out bool overwriteParameterValues)
            {
                FamilyFoundCalled = true;
                source = FamilySource.Family;
                overwriteParameterValues = false;
                return true;
            }
        }
    }
}
