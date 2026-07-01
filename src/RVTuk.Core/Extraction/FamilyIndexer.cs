using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using RVTuk.Core.Database;
using RVTuk.Core.Models;
using RVTuk.Core.Util;

namespace RVTuk.Core.Extraction
{
    public class FamilyIndexer
    {
        private readonly IndexRepository _repository;
        private readonly string _libraryRoot;
        private readonly IReadOnlyList<string> _ignoredSubfolders;

        /// <summary>Families skipped because their full path exceeds Windows MAX_PATH (set by the last Scan).</summary>
        public int SkippedLongPath { get; private set; }

        /// <summary>Families skipped because they live under an ignored subfolder (set by the last Scan).</summary>
        public int SkippedIgnored { get; private set; }

        /// <summary>Families whose thumbnail was committed directly without queuing Revit parameter extraction (set by the last Scan).</summary>
        public int ThumbnailOnlyCount { get; private set; }

        public FamilyIndexer(IndexRepository repository, string libraryRootPath,
            IReadOnlyList<string> ignoredSubfolders = null)
        {
            _repository = repository;
            _libraryRoot = libraryRootPath;
            _ignoredSubfolders = ignoredSubfolders ?? new List<string>();
        }

        /// <summary>
        /// Scans the library folder. <paramref name="includeThumbnails"/> and
        /// <paramref name="includeParameters"/> are independent: a family needs a facet
        /// refreshed if its file changed, or it is simply missing that facet's data. Families
        /// needing only a thumbnail refresh are committed directly (no Revit engine involved);
        /// families needing parameters are returned as work items for Phase 2 (Revit-engine
        /// extraction), carrying an already-extracted thumbnail if that facet was also requested.
        /// Both flags false is a filenames-only sync: add new families, prune deleted ones.
        /// </summary>
        public IReadOnlyList<ExtractionWorkItem> Scan(
            Action<string, int, int> progressCallback,
            CancellationToken cancellationToken = default,
            bool includeThumbnails = false,
            bool includeParameters = false)
        {
            // Robust walk: skips over-long/inaccessible paths instead of aborting the whole scan.
            // Ignored subfolders are pruned from the walk entirely, so their (often huge) file
            // counts never inflate the progress total or slow the scan.
            var rfaFiles = new List<string>(PathUtil.SafeEnumerateFiles(_libraryRoot, "*.rfa",
                dir => PathUtil.IsUnderIgnoredFolder(PathUtil.GetRelativePath(_libraryRoot, dir), _ignoredSubfolders)));
            int total = rfaFiles.Count;
            var workItems = new List<ExtractionWorkItem>();
            var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            SkippedLongPath = 0;
            SkippedIgnored = 0;
            ThumbnailOnlyCount = 0;

            // Preloaded once per scan so the per-file loop never issues an extra query per family.
            var hasThumbnail = includeThumbnails ? _repository.GetFamilyIdsWithThumbnail() : new HashSet<long>();
            var paramsExtracted = includeParameters ? _repository.GetFamilyIdsWithParametersExtracted() : new HashSet<long>();

            // Protect families that were indexed before their folder was ignored: keep their rows
            // (the browser just hides them) instead of pruning them as stale. We don't walk the
            // ignored folders on disk, so we read these straight from the DB. Skipped entirely when
            // nothing is ignored, to avoid an extra full-table read on every scan.
            if (_ignoredSubfolders.Count > 0)
            {
                foreach (var dbPath in _repository.GetAllRelativePaths())
                {
                    if (PathUtil.IsUnderIgnoredFolder(dbPath, _ignoredSubfolders))
                    {
                        scannedPaths.Add(dbPath);
                        SkippedIgnored++;
                    }
                }
            }

            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fullPath = rfaFiles[i];

                // A family whose full path exceeds Windows MAX_PATH cannot be opened by Revit on
                // .NET Framework; skip it so it neither aborts the scan nor triggers Revit's
                // "path too long" dialog during metadata extraction.
                if (fullPath.Length >= 260)
                {
                    SkippedLongPath++;
                    progressCallback(Path.GetFileName(fullPath), i + 1, total);
                    continue;
                }

                string relativePath = PathUtil.GetRelativePath(_libraryRoot, fullPath);
                string fileName = Path.GetFileName(fullPath);

                progressCallback(fileName, i + 1, total);

                scannedPaths.Add(relativePath);

                var info = new FileInfo(fullPath);
                long fileSize = info.Length;
                DateTime modifiedDate = info.LastWriteTimeUtc;

                var existing = _repository.GetFamilyByPath(relativePath);

                // A case-only rename on disk must not orphan the row: re-key it to the on-disk
                // casing so the exact-match writes below hit it and the stale prune keeps it.
                if (existing != null && !string.Equals(existing.RelativePath, relativePath, StringComparison.Ordinal))
                    _repository.UpdateRelativePathCasing(existing.Id, relativePath, fileName);

                bool fileChanged = existing == null
                    || existing.FileSize != fileSize
                    || Math.Abs((existing.ModifiedDate - modifiedDate).TotalSeconds) > 1;

                bool needsThumbnail = includeThumbnails
                    && (fileChanged || existing == null || !hasThumbnail.Contains(existing.Id));
                bool needsParameters = includeParameters
                    && (fileChanged || existing == null || !paramsExtracted.Contains(existing.Id));

                if (!needsThumbnail && !needsParameters)
                {
                    // Filenames-only sync (also the "neither checkbox" case): keep the row's
                    // name/size/date current with no extraction. Real values written directly —
                    // there is nothing to extract afterward, so no sentinel/resumability dance.
                    _repository.UpsertFamilyFileInfo(relativePath, fileName, fileSize, modifiedDate);
                    continue;
                }

                if (needsParameters)
                {
                    // Create/keep the row (preserving its Id and curated data) but do NOT store the
                    // real size/date yet — InsertFamily writes a sentinel for new rows and leaves an
                    // existing row's old size/date untouched. The real values ride on the work item
                    // and are committed only once extraction succeeds (UpdateFamilyMetadata). That
                    // way a cancelled family still looks stale and is re-scanned next time.
                    long familyId = _repository.InsertFamily(relativePath, fileName);

                    byte[]? thumbnail = null;
                    int revitYear = 0;
                    if (needsThumbnail)
                        (thumbnail, revitYear) = ThumbnailExtractor.ExtractFromRfa(fullPath);

                    workItems.Add(new ExtractionWorkItem
                    {
                        FamilyId = familyId,
                        FullPath = fullPath,
                        RelativePath = relativePath,
                        ThumbnailPng = thumbnail,
                        FileRevitYear = revitYear,
                        ModifiedDate = modifiedDate,
                        FileSize = fileSize
                    });
                }
                else
                {
                    // Needs only a thumbnail refresh: a plain file read, no Revit engine involved,
                    // so commit it directly instead of queuing a Phase-2 work item.
                    var (thumbnail, revitYear) = ThumbnailExtractor.ExtractFromRfa(fullPath);
                    long familyId = existing?.Id ?? _repository.InsertFamily(relativePath, fileName);

                    if (thumbnail != null)
                    {
                        _repository.UpdateThumbnailOnly(familyId, thumbnail, revitYear, modifiedDate, fileSize);
                        ThumbnailOnlyCount++;
                    }
                    else
                    {
                        // Extraction failed (e.g. no embedded preview) — keep name/size/date
                        // current; leave the Thumbnail table untouched so this family is retried
                        // on the next thumbnails-enabled scan.
                        _repository.UpsertFamilyFileInfo(relativePath, fileName, fileSize, modifiedDate);
                    }
                }
            }

            _repository.DeleteStaleEntries(scannedPaths);
            return workItems;
        }
    }
}
