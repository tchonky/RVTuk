using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.Models.Comparison;
using RVTuk.Revit.Extraction;

namespace RVTuk.Revit.ExternalEvents
{
    /// <summary>Runs the registered extractors against an open document (the active one, or a
    /// specific open document) on Revit's main thread. The background thread blocks on _done.</summary>
    public class CaptureSnapshotEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new ManualResetEventSlim(false);

        public Document? TargetDocument { get; set; }            // null = use active document
        public List<ICategoryExtractor> Extractors { get; set; } = new List<ICategoryExtractor>();

        public SnapshotMeta? ResultMeta { get; private set; }
        public List<CategorySnapshot> ResultCategories { get; private set; } = new List<CategorySnapshot>();
        public string? Error { get; private set; }

        public void Prepare(Document? doc, List<ICategoryExtractor> extractors)
        {
            TargetDocument = doc;
            Extractors = extractors;
            ResultMeta = null;
            ResultCategories = new List<CategorySnapshot>();
            Error = null;
            _done.Reset();
        }

        public void WaitForCompletion() => _done.Wait();
        public bool WaitForCompletion(int timeoutMs) => _done.Wait(timeoutMs);

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = TargetDocument ?? app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    Error = "No document is open to capture.";
                    return;
                }

                ResultMeta = SnapshotMetaFactory.Build(app, doc, "Project", doc.PathName);
                foreach (var extractor in Extractors)
                {
                    try { ResultCategories.Add(extractor.Extract(doc)); }
                    catch { /* skip a category whose extraction fails */ }
                }
            }
            catch (Exception e)
            {
                Error = e.Message;
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.CaptureSnapshotEventHandler";
    }
}
