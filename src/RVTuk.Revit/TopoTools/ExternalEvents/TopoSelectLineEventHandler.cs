using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.ExternalEvents
{
    /// <summary>
    /// Selects one topo line in the view from its row in the pane — the other half of the loop that
    /// makes an invisible height workable, the first half being the pane highlighting rows for
    /// whatever is selected in the view.
    ///
    /// Selects without zooming: the row is clicked to answer "which line is this", and yanking the
    /// camera around (or worse, opening another view, as ShowElements can) is not what was asked.
    /// </summary>
    public class TopoSelectLineEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);
        private long _lineId;

        public void Prepare(long lineId) => _lineId = lineId;
        public void Reset() => _done.Reset();
        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var uiDoc = app.ActiveUIDocument;
                if (uiDoc?.Document == null) return;

                var id = new ElementId(_lineId);
                if (uiDoc.Document.GetElement(id) == null) return;

                uiDoc.Selection.SetElementIds(new List<ElementId> { id });
            }
            catch
            {
                // A selection that will not take is not worth interrupting the user over.
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.TopoToolsSelectLineEventHandler";
    }
}
