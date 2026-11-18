using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.UI;
using RVTuk.Core.FamilyBrowser.Database;
using RVTuk.Core.FamilyBrowser.Models;
using RVTuk.Revit.FamilyBrowser.Extraction;

using RVTuk.Core.FamilyBrowser.Util;

namespace RVTuk.Revit.FamilyBrowser.ExternalEvents
{
    /// <summary>
    /// Executes on Revit's main thread. Called once per family needing extraction.
    /// The background indexing thread blocks on _gate until this completes.
    /// </summary>
    public class IndexingExternalEventHandler : IExternalEventHandler
    {
        private readonly RequestGate _gate = new();

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
            _gate.Begin();
        }

        public void WaitForCompletion() => _gate.Wait(RequestGate.StartTimeout);

        public void Execute(UIApplication app)
        {
            if (!_gate.TryStart()) return; // withdrawn: the caller already reported "Revit is busy"
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
                _gate.Complete();
            }
        }

        public string GetName() => "RVTuk.IndexingExternalEventHandler";
    }
}
