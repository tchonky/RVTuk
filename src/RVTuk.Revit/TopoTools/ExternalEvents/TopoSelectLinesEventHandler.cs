using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.ExternalEvents
{
    /// <summary>
    /// Mirrors the pane's highlighted rows into Revit's selection — the other half of the loop that
    /// makes an invisible height workable, the first half being the pane highlighting rows for
    /// whatever is selected in the view. Highlighting a row *is* selecting the line; there is no
    /// separate button for it.
    ///
    /// Selects without zooming: a row is clicked to answer "which line is this", and yanking the
    /// camera around (or worse, opening another view, as ShowElements can) is not what was asked.
    /// </summary>
    public class TopoSelectLinesEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);
        private IReadOnlyList<long> _lineIds = Array.Empty<long>();

        public void Prepare(IReadOnlyList<long> lineIds) => _lineIds = lineIds ?? Array.Empty<long>();
        public void Reset() => _done.Reset();
        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var uiDoc = app.ActiveUIDocument;
                var doc = uiDoc?.Document;
                if (doc == null) return;

                var ids = _lineIds
                    .Select(id => new ElementId(id))
                    .Where(id => doc.GetElement(id) != null)
                    .ToList();

                uiDoc!.Selection.SetElementIds(ids);
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

        public string GetName() => "RVTuk.TopoToolsSelectLinesEventHandler";
    }
}
