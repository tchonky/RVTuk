using System;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.ExternalEvents
{
    /// <summary>
    /// Creates the Topo_Line style and binds TOPO_Elevation, on Revit's main thread and inside one
    /// transaction. Follows the LevelDiscoveryEventHandler ping-pong pattern: the pane raises this
    /// from the pool and blocks on WaitForCompletion.
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
                        TopoElevationParameter.EnsureBound(doc);
                        tx.Commit();
                        Summary = "Ready: draw detail lines on the Topo_Line style and give each a " +
                                  "TOPO_Elevation.";
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
