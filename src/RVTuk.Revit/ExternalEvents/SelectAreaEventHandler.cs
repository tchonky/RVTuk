using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.ExternalEvents
{
    /// <summary>
    /// Selects a single element (an extracted Area) by id in the active document, on Revit's main
    /// thread. Used to jump from a row in the Area Submission UI to the corresponding Area in the model.
    /// </summary>
    public class SelectAreaEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);

        public long ElementId { get; private set; }
        public bool Success { get; private set; }
        public string? ErrorMessage { get; private set; }

        public void Prepare(long elementId)
        {
            ElementId = elementId;
            Success = false;
            ErrorMessage = null;
            _done.Reset();
        }

        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc == null)
                {
                    ErrorMessage = "No active document.";
                    return;
                }

                // ElementId(Int64) exists on both Revit 2024 and 2025 (2024 added it as a
                // forward-compat overload alongside the now-deprecated ElementId(int) ctor;
                // see AreaExtractor.Raw()'s matching note on ElementId.Value).
                var id = new ElementId(ElementId);
                uidoc.Selection.SetElementIds(new List<ElementId> { id });
                Success = true;
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

        public string GetName() => "RVTuk.SelectArea";
    }
}
