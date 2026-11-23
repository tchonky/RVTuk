using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.RishuiZamin.ExternalEvents
{
    /// <summary>
    /// Selects elements (extracted Areas) by id in the active document, on Revit's main thread.
    /// Used to mirror the Area Calc window's selection into the model.
    /// </summary>
    public class SelectAreaEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);

        public IReadOnlyList<long> ElementIds { get; private set; } = Array.Empty<long>();
        public bool Success { get; private set; }
        public string? ErrorMessage { get; private set; }

        public void Prepare(IReadOnlyList<long> elementIds)
        {
            ElementIds = elementIds;
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

                uidoc.Selection.SetElementIds(ElementIds.Select(ToId).ToList());
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

        // ElementId(Int64) exists from Revit 2024 (see AreaExtractor.Raw()'s note on
        // ElementId.Value). Revit 2023 (KKarea) only has the int ctor; area element ids always
        // fit in 32 bits there.
#if REVIT2023
        private static ElementId ToId(long id) => new ElementId((int)id);
#else
        private static ElementId ToId(long id) => new ElementId(id);
#endif

        public string GetName() => "RVTuk.SelectArea";
    }
}
