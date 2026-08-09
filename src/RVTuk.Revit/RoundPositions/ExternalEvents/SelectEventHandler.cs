using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.RoundPositions.ExternalEvents
{
    /// <summary>Highlights a tree node's elements in the model. The pane is not modal, so this is
    /// the point of it: scan, look at the model, fix a group, look again.</summary>
    public class SelectEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);

        private IReadOnlyList<long> _ids = new List<long>();

        public void Prepare(IReadOnlyList<long> ids)
        {
            _ids = ids;
            _done.Reset();
        }

        public void WaitForCompletion() => _done.Wait();

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
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.RoundPositionsSelectEventHandler";
    }
}
