using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.UI;
using RVTuk.Revit.AreaSubmission;

namespace RVTuk.Revit.ExternalEvents
{
    /// <summary>
    /// Runs <see cref="AreaExtractor.FromOpenSheet"/> against the active document's open sheet on
    /// Revit's main thread. The background thread blocks on <see cref="WaitForCompletion"/>.
    /// </summary>
    public class AreaExtractEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);

        public IReadOnlyList<ExtractedArea> Result { get; private set; } = Array.Empty<ExtractedArea>();

        public void Reset() => _done.Reset();
        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                Result = uidoc == null
                    ? Array.Empty<ExtractedArea>()
                    : new AreaExtractor().FromOpenSheet(uidoc);
            }
            catch
            {
                Result = Array.Empty<ExtractedArea>();
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.AreaExtract";
    }
}
