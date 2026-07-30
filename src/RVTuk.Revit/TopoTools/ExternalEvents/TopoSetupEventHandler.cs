using System;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.ExternalEvents
{
    /// <summary>
    /// Creates the Topo_Line style, on Revit's main thread and inside one transaction. Follows the
    /// LevelDiscoveryEventHandler ping-pong pattern: the pane raises this from the pool and blocks
    /// on WaitForCompletion.
    ///
    /// The style is now all setup does. It also bound a TOPO_Elevation shared parameter until that
    /// turned out to be impossible on the Lines category — see TopoElevationStore.
    /// </summary>
    public class TopoSetupEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);

        public string Summary { get; private set; } = "";

        public void Reset() => _done.Reset();
        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    Summary = "No document is open.";
                    return;
                }

                using (var tx = new Transaction(doc, "Topo Tools — set up this project"))
                {
                    tx.Start();
                    try
                    {
                        TopoLineStyle.EnsureExists(doc);
                        tx.Commit();
                        Summary = "Ready: draw detail lines on the Topo_Line style, then set each " +
                                  "one's height in the list below.";
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        Summary = "Setup failed, and nothing was changed: " + ex.Message;
                    }
                }
            }
            catch (Exception ex)
            {
                Summary = "Setup failed: " + ex.Message;
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.TopoToolsSetupEventHandler";
    }
}
