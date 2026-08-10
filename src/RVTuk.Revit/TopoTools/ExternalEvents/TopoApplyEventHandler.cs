using System;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.ExternalEvents
{
    /// <summary>
    /// Runs the points onto the toposolids on Revit's main thread. The transaction lives inside
    /// TopoRunner.Apply, so a failure there rolls back and comes out as a message rather than an
    /// exception.
    /// </summary>
    public class TopoApplyEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);
        private double _spacingCentimetres = 100;

        public string Summary { get; private set; } = "";

        public void Prepare(double spacingCentimetres) => _spacingCentimetres = spacingCentimetres;
        public void Reset() => _done.Reset();
        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var uiDoc = app.ActiveUIDocument;
                var doc = uiDoc?.Document;
                if (doc == null)
                {
                    Summary = "No document is open.";
                    return;
                }

                double spacingFeet = UnitUtils.ConvertToInternalUnits(
                    _spacingCentimetres, UnitTypeId.Centimeters);

                Summary = TopoRunner.Apply(doc, uiDoc!.ActiveView, spacingFeet);
            }
            catch (Exception ex)
            {
                Summary = "Topo Tools failed: " + ex.Message;
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.TopoToolsApplyEventHandler";
    }
}
