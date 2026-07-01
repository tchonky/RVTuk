using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.ExternalEvents
{
    public class LoadFamilyEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);

        public string? FamilyPath { get; set; }
        public bool Success { get; private set; }
        public string? ErrorMessage { get; private set; }

        public void Prepare(string familyPath)
        {
            FamilyPath = familyPath;
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
                if (doc == null || FamilyPath == null)
                {
                    ErrorMessage = "No active document.";
                    return;
                }

                using var tx = new Transaction(doc, "Load Family");
                tx.Start();
                bool loaded = doc.LoadFamily(FamilyPath, new OverwriteLoadOptions(), out _);
                tx.Commit();
                Success = loaded;
                if (!loaded)
                    ErrorMessage = "Revit did not load the family (it may be invalid, or the load was rejected).";
            }
            catch (System.Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.LoadFamilyEventHandler";

        private class OverwriteLoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
            {
                overwriteParameterValues = true;
                return true;
            }

            public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse,
                out FamilySource source, out bool overwriteParameterValues)
            {
                source = FamilySource.Family;
                overwriteParameterValues = true;
                return true;
            }
        }
    }
}
