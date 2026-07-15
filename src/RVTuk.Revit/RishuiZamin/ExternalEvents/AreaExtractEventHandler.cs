using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.UI;
using RVTuk.Revit.RishuiZamin;

namespace RVTuk.Revit.RishuiZamin.ExternalEvents
{
    /// <summary>
    /// Runs <see cref="AreaExtractor.FromOpenSheet"/> against the active document's open sheet on
    /// Revit's main thread. The background thread blocks on <see cref="WaitForCompletion"/>.
    /// </summary>
    public class AreaExtractEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);

        public IReadOnlyList<ExtractedArea> Result { get; private set; } = Array.Empty<ExtractedArea>();

        public ExtractDiagnostics Diagnostics { get; private set; }

        /// <summary>Set when <see cref="Execute"/> threw; the caller rethrows this after
        /// <see cref="WaitForCompletion"/> instead of silently reporting zero areas.</summary>
        public Exception? Error { get; private set; }

        public void Reset()
        {
            Error = null;
            _done.Reset();
        }

        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                if (uidoc == null)
                {
                    Result = Array.Empty<ExtractedArea>();
                    Diagnostics = default;
                }
                else
                {
                    Result = new AreaExtractor().FromOpenSheet(uidoc, out var diagnostics);
                    Diagnostics = diagnostics;
                }
            }
            catch (Exception ex)
            {
                Result = Array.Empty<ExtractedArea>();
                Error = ex;
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.AreaExtract";
    }
}
