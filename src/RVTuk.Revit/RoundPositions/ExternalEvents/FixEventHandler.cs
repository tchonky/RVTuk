using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.UI;
using RVTuk.Core.RoundPositions;

namespace RVTuk.Revit.RoundPositions.ExternalEvents
{
    /// <summary>
    /// Marshals a fix onto Revit's main thread. <see cref="Error"/> is not optional: the runner
    /// throws by design when Revit rolls the transaction back, and that message has to reach the
    /// pane's status line rather than being swallowed.
    /// </summary>
    public class FixEventHandler : IExternalEventHandler
    {
        private static readonly RoundingReport Empty =
            new RoundingReport(0, 0, 0, Array.Empty<SkippedElement>(), Array.Empty<ReleasedConstraint>());

        private readonly ManualResetEventSlim _done = new(false);

        private bool _allowHalfCentimetres;
        private ISet<long> _ids = new HashSet<long>();
        private CancellationToken _cancellation;

        public RoundingReport Report { get; private set; } = Empty;

        public Exception? Error { get; private set; }

        public void Prepare(bool allowHalfCentimetres, ISet<long> ids, CancellationToken cancellation)
        {
            _allowHalfCentimetres = allowHalfCentimetres;
            _ids = ids;
            _cancellation = cancellation;
            Report = Empty;
            Error = null;
            _done.Reset();
        }

        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                Report = doc == null
                    ? Empty
                    : RoundPositionsRunner.Fix(doc, _allowHalfCentimetres, _ids, _cancellation);
            }
            catch (Exception ex)
            {
                Error = ex;
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.RoundPositionsFixEventHandler";
    }
}
