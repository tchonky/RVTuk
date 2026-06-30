using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.UI;
using RVTuk.Core.Database;
using RVTuk.Core.Models;
using RVTuk.Revit.Extraction;

namespace RVTuk.Revit.ExternalEvents
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

                // Skip families newer than the running Revit version — opening such a family
                // document triggers native processing that can crash Revit.
                string? category = null;
                IReadOnlyList<RVTuk.Core.Models.ParameterModel> parameters = System.Array.Empty<RVTuk.Core.Models.ParameterModel>();

                bool tooNew = CurrentItem.FileRevitYear > 0
                    && int.TryParse(app.Application.VersionNumber, out int runningYear)
                    && CurrentItem.FileRevitYear > runningYear;

                if (!tooNew)
                {
                    try { (category, parameters) = Extractor.ExtractMetadata(CurrentItem.FullPath); }
                    catch { /* skip family if extraction fails */ }
                }

                // Pass the file's real size/date through: writing them here (only after a
                // successful extraction) is what marks the row current. A cancelled family is
                // never updated, so it stays stale and is re-scanned next time.
                Repository.UpdateFamilyMetadata(CurrentItem.FamilyId, category, parameters, CurrentItem.ThumbnailPng, CurrentItem.FileRevitYear,
                    CurrentItem.ModifiedDate, CurrentItem.FileSize);
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.IndexingExternalEventHandler";
    }
}
