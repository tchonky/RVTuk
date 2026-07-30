using System;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.TopoTools;

namespace RVTuk.Revit.TopoTools.ExternalEvents
{
    /// <summary>
    /// Marshals one discovery pass onto Revit's main thread for the pane, which triggers it from a
    /// background thread (raise + WaitForCompletion on the UI thread would deadlock). Read-only —
    /// no transaction.
    /// </summary>
    public class TopoDiscoveryEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);
        private double _spacingMillimetres = 1000;

        public TopoScope Result { get; private set; } = TopoScope.Unavailable("", "No document is open.");

        public void Prepare(double spacingMillimetres) => _spacingMillimetres = spacingMillimetres;
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
                    Result = TopoScope.Unavailable("", "No document is open.");
                    return;
                }

                double spacingFeet = UnitUtils.ConvertToInternalUnits(
                    _spacingMillimetres, UnitTypeId.Millimeters);

                Result = TopoRunner.Discover(doc, uiDoc!.ActiveView, spacingFeet);
            }
            catch (Exception ex)
            {
                Result = TopoScope.Unavailable("", "Could not read this view: " + ex.Message);
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.TopoToolsDiscoveryEventHandler";
    }
}
