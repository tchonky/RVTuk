using System;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.FamilyBrowser.ExternalEvents
{
    /// <summary>
    /// Edits a family that lives only in the open project (no library .rfa behind it).
    /// With a SaveAsPath: saves it there (Save to Library). Without: saves it to a temp
    /// .rfa named exactly after the family and opens that in the Family Editor — an
    /// in-memory EditFamily document has no UI window, so activating it requires the
    /// temp-file round-trip; "Load into Project" from the editor still updates the same
    /// family because Revit matches families by name.
    /// </summary>
    public class EditProjectFamilyEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);

        public string? FamilyName { get; private set; }
        public string? SaveAsPath { get; private set; }
        public bool Success { get; private set; }
        public string? ErrorMessage { get; private set; }

        public void Prepare(string familyName, string? saveAsPath)
        {
            FamilyName = familyName;
            SaveAsPath = saveAsPath;
            Success = false;
            ErrorMessage = null;
            _done.Reset();
        }

        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null || string.IsNullOrEmpty(FamilyName))
                {
                    ErrorMessage = "No active document.";
                    return;
                }

                var target = SaveAsPath ?? Path.Combine(
                    Path.GetTempPath(), "RVTuk", "FamilyEdit", FamilyName + ".rfa");

                // Opened from here before and still open: SaveAs over an open file fails, so
                // just bring that editor back.
                if (SaveAsPath == null && app.Application.Documents.Cast<Document>()
                        .Any(d => string.Equals(d.PathName, target, StringComparison.OrdinalIgnoreCase)))
                {
                    app.OpenAndActivateDocument(target);
                    Success = true;
                    return;
                }

                if (doc.IsFamilyDocument)
                {
                    ErrorMessage = LoadFamilyEventHandler.FamilyDocumentActive;
                    return;
                }

                var family = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .FirstOrDefault(f => string.Equals(f.Name, FamilyName, StringComparison.Ordinal));
                if (family == null)
                {
                    ErrorMessage = $"Family '{FamilyName}' was not found in the project.";
                    return;
                }
                if (family.IsInPlace)
                {
                    ErrorMessage = "In-place families cannot be edited or saved as .rfa files.";
                    return;
                }
                if (!family.IsEditable)
                {
                    ErrorMessage = "This family is not editable.";
                    return;
                }

                var famDoc = doc.EditFamily(family);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    famDoc.SaveAs(target, new SaveAsOptions { OverwriteExistingFile = true });
                }
                finally
                {
                    // Close before OpenAndActivateDocument: after SaveAs this doc IS the
                    // file at 'target', and Revit refuses to open a path twice.
                    famDoc.Close(false);
                }

                if (SaveAsPath == null)
                    app.OpenAndActivateDocument(target);

                Success = true;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.EditProjectFamilyEventHandler";
    }
}
