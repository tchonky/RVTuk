using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.UI;
using RVTuk.Core.RishuiZamin;
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

        /// <summary>The document the last extraction read; setting a usage checks that it is
        /// still the active one.</summary>
        public Autodesk.Revit.DB.Document? Document { get; private set; }

        /// <summary>The Table tab's plot area and limits stored in <see cref="Document"/>; empty
        /// when there are none or reading them failed.</summary>
        public PlotLimits PlotLimits { get; private set; } = PlotLimits.Empty;

        /// <summary>Identifies <see cref="Document"/> to the window (its path, else its title), so
        /// it can tell a re-read of the same project from a switch to another one.</summary>
        public string DocumentKey { get; private set; } = "";

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
                Document = uidoc?.Document;
                DocumentKey = Document == null ? "" :
                    string.IsNullOrEmpty(Document.PathName) ? Document.Title : Document.PathName;
                try { PlotLimits = Document == null ? PlotLimits.Empty : PlotLimitsStore.Read(Document); }
                catch { PlotLimits = PlotLimits.Empty; }   // never fail the area read over the limits
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
