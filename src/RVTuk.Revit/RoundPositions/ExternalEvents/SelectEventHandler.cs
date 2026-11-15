using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.RoundPositions.ExternalEvents
{
    /// <summary>
    /// Highlights a tree node's elements in the model. The pane is not modal, so this is the point
    /// of it: scan, look at the model, fix a group, look again.
    ///
    /// Deliberately has no WaitForCompletion. The pane's WPF thread is Revit's main thread, so a
    /// caller waiting here would block the very thread that has to run this — and nothing needs the
    /// result anyway.
    /// </summary>
    public class SelectEventHandler : IExternalEventHandler
    {
        private IReadOnlyList<long> _ids = new List<long>();

        public void Prepare(IReadOnlyList<long> ids) => _ids = ids;

        public void Execute(UIApplication app)
        {
            try
            {
                app.ActiveUIDocument?.Selection.SetElementIds(
                    _ids.Select(id => new ElementId(id)).ToList());
            }
            catch
            {
                // A selection that fails is not worth interrupting the pane for — an id can have
                // been deleted since the scan.
            }
        }

        public string GetName() => "RVTuk.RoundPositionsSelectEventHandler";
    }
}
