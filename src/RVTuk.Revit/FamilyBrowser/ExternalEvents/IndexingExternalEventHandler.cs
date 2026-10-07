using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.UI;
using RVTuk.Core.FamilyBrowser.Database;
using RVTuk.Core.FamilyBrowser.Models;
using RVTuk.Revit.FamilyBrowser.Extraction;

namespace RVTuk.Revit.FamilyBrowser.ExternalEvents
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

        /// <summary>The DB write failed (locked, disk full, share gone) — the waiter reports it.</summary>
        public string? Error { get; private set; }

        /// <summary>Revit opened the family and its metadata was written.</summary>
        public bool Extracted { get; private set; }

        public void PrepareAndWait(ExtractionWorkItem item, IndexRepository repository, FamilyMetadataExtractor extractor)
        {
            CurrentItem = item;
            Repository = repository;
            Extractor = extractor;
            Error = null;
            Extracted = false;
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
                bool tooNew = CurrentItem.FileRevitYear > 0
                    && int.TryParse(app.Application.VersionNumber, out int runningYear)
                    && CurrentItem.FileRevitYear > runningYear;

                var metadata = tooNew ? null : Extractor.ExtractMetadata(CurrentItem.FullPath);
                if (metadata is { } m)
                {
                    // Pass the file's real size/date through: writing them here (only after a
                    // successful extraction) is what marks the row current. A cancelled family is
                    // never updated, so it stays stale and is re-scanned next time.
                    Repository.UpdateFamilyMetadata(CurrentItem.FamilyId, m.Category, m.Parameters, CurrentItem.ThumbnailPng,
                        CurrentItem.FileRevitYear, CurrentItem.ModifiedDate, CurrentItem.FileSize, m.Version);
                    Extracted = true;
                }
                else if (CurrentItem.ThumbnailPng != null)
                {
                    // Too new, locked or unreadable: keep the fresh thumbnail but leave the row's
                    // metadata alone and unextracted, so a later scan (or a newer Revit) retries
                    // instead of stamping empty data as done.
                    Repository.UpdateThumbnailOnly(CurrentItem.FamilyId, CurrentItem.ThumbnailPng,
                        CurrentItem.FileRevitYear, CurrentItem.ModifiedDate, CurrentItem.FileSize);
                }
            }
            catch (System.Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                _done.Set();
            }
        }

        public string GetName() => "RVTuk.IndexingExternalEventHandler";
    }
}
