using System;
using System.Threading;
using Autodesk.Revit.UI;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions.ExternalEvents
{
    /// <summary>
    /// Marshals a scan onto Revit's main thread. The scan is read-only, but the Revit API may
    /// still only be touched on its own thread, so it goes through an external event like
    /// everything else.
    /// </summary>
    public class ScanEventHandler : IExternalEventHandler
    {
        private static readonly ScanResult Empty =
            new ScanResult(0, 0, Array.Empty<DirtyElement>(), Array.Empty<SkippedElement>());

        private readonly ManualResetEventSlim _done = new(false);

        private bool _allowHalfCentimetres;
        private CancellationToken _cancellation;

        public ScanResult Result { get; private set; } = Empty;

        public Exception? Error { get; private set; }

        public void Prepare(bool allowHalfCentimetres, CancellationToken cancellation)
        {
            _allowHalfCentimetres = allowHalfCentimetres;
            _cancellation = cancellation;
            Result = Empty;
            Error = null;
            _done.Reset();
        }

        /// <summary>
        /// False means Revit never ran the event. Bounded on purpose: an unbounded wait here is
        /// indistinguishable from a hang, and the pane would sit on "Working…" for ever with no
        /// way back.
        /// </summary>
        public bool WaitForCompletion(TimeSpan timeout) => _done.Wait(timeout);

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                Result = doc == null
                    ? Empty
                    : RoundPositionsRunner.Scan(doc, _allowHalfCentimetres, _cancellation);
            }
            catch (Exception ex)
            {
                // Never let this escape: an unhandled throw here kills Revit's event loop, and the
                // caller would block on WaitForCompletion forever.
                Error = ex;
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.RoundPositionsScanEventHandler";
    }
}
