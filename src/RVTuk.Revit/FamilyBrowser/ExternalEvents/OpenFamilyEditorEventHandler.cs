using System.Threading;
using Autodesk.Revit.UI;

using RVTuk.Core.FamilyBrowser.Util;

namespace RVTuk.Revit.FamilyBrowser.ExternalEvents
{
    public class OpenFamilyEditorEventHandler : IExternalEventHandler
    {
        private readonly RequestGate _gate = new();

        public string? FamilyPath { get; set; }
        public string? ErrorMessage { get; private set; }

        public void Prepare(string familyPath)
        {
            FamilyPath = familyPath;
            ErrorMessage = null;
            _gate.Begin();
        }

        public void WaitForCompletion() => _gate.Wait(RequestGate.StartTimeout);

        public void Execute(UIApplication app)
        {
            if (!_gate.TryStart()) return; // withdrawn: the caller already reported "Revit is busy"
            try
            {
                if (FamilyPath == null)
                {
                    ErrorMessage = "No family path.";
                    return;
                }
                // Application.OpenDocumentFile opens the file at the data level only — it does
                // not create/activate a UI window, so nothing visibly happens. The UI-level
                // UIApplication.OpenAndActivateDocument(string) both opens and activates the
                // document window (what a double-click in Explorer produces). A .rfa opens as a
                // family document (Family Editor mode) automatically based on its content.
                app.OpenAndActivateDocument(FamilyPath);
            }
            catch (System.Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                _gate.Complete();
            }
        }

        public string GetName() => "RVTuk.OpenFamilyEditorEventHandler";
    }
}
