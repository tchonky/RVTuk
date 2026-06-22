using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.UI;
using ReviTchucky.Core.Database;
using ReviTchucky.Core.Models;
using ReviTchucky.Revit.Extraction;

namespace ReviTchucky.Revit.ExternalEvents
{
    /// <summary>
    /// Executes on Revit's main thread. Called once per family needing extraction.
    /// The background indexing thread blocks on _done until this completes.
    /// </summary>
    public class IndexingExternalEventHandler : IExternalEventHandler
    {
        private readonly ManualResetEventSlim _done = new(false);

        // Set by the background thread before raising the event
        public ExtractionWorkItem? CurrentItem { get; set; }
        public IndexRepository? Repository { get; set; }
        public FamilyMetadataExtractor? Extractor { get; set; }

        public void PrepareAndWait(ExtractionWorkItem item, IndexRepository repository, FamilyMetadataExtractor extractor)
        {
            CurrentItem = item;
            Repository = repository;
            Extractor = extractor;
            _done.Reset();
        }

        public void WaitForCompletion() => _done.Wait();

        public void Execute(UIApplication app)
        {
            try
            {
                if (CurrentItem == null || Repository == null || Extractor == null)
                    return;

                // Skip ExtractPartAtomFromFamilyFile for families newer than the running Revit
                // version — opening them triggers native geometry processing that crashes Revit.
                string? category = null;
                IReadOnlyList<ReviTchucky.Core.Models.ParameterModel> parameters = System.Array.Empty<ReviTchucky.Core.Models.ParameterModel>();

                bool tooNew = CurrentItem.FileRevitYear > 0
                    && int.TryParse(app.Application.VersionNumber, out int runningYear)
                    && CurrentItem.FileRevitYear > runningYear;

                if (!tooNew)
                {
                    try { (category, parameters) = Extractor.ExtractMetadata(CurrentItem.FullPath); }
                    catch { /* skip family if extraction fails */ }
                }

                Repository.UpdateFamilyMetadata(CurrentItem.FamilyId, category, parameters, CurrentItem.ThumbnailPng);
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "ReviTchucky.IndexingExternalEventHandler";
    }
}
