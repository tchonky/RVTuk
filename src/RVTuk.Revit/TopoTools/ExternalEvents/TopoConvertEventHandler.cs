using System;
using System.Threading;
using Autodesk.Revit.UI;

namespace RVTuk.Revit.TopoTools.ExternalEvents
{
    /// <summary>
    /// Runs the ⇄ conversion on Revit's main thread. Its dialogs are shown from here, on that
    /// thread, while the pane's worker thread waits. ConvertRunner owns the transactions, so a
    /// failure comes back as text rather than as an exception.
    /// </summary>
    public class TopoConvertEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);

        public string Summary { get; private set; } = "";

        public void Reset() => _done.Reset();
        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var uiDoc = app.ActiveUIDocument;
                if (uiDoc?.Document == null)
                {
                    Summary = "No document is open.";
                    return;
                }

                Summary = ConvertRunner.Run(uiDoc);
            }
            catch (Exception ex)
            {
                Summary = "Convert failed: " + ex.Message;
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.TopoToolsConvertEventHandler";
    }
}
