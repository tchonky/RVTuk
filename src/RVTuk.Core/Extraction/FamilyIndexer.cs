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

        public FamilyIndexer(IndexRepository repository, string libraryRootPath,
            IReadOnlyList<string> ignoredSubfolders = null)
        {
            _repository = repository;
            _libraryRoot = libraryRootPath;
            _ignoredSubfolders = ignoredSubfolders ?? new List<string>();
        }

        /// <summary>
        /// Scans the library folder. Returns files needing Revit API metadata extraction.
        /// Thumbnails (OLE) are extracted here and embedded in the work items.
        /// </summary>
        public IReadOnlyList<ExtractionWorkItem> Scan(
            Action<string, int, int> progressCallback,
            CancellationToken cancellationToken = default,
            bool forceReextractAll = false)
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

                // "Re-scan All Families" forces re-extraction of every family regardless of
                // whether its file changed; the default incremental scan only re-extracts new
                // or modified files. Either way the upsert keeps the family's row Id, so curated
                // data (instructions, tags, favourites, custom thumbnails, gallery) is preserved.
                bool needsExtraction = forceReextractAll
                    || existing == null
                    || existing.FileSize != fileSize
                    || Math.Abs((existing.ModifiedDate - modifiedDate).TotalSeconds) > 1;

                if (!needsExtraction) continue;

                long familyId = _repository.InsertFamily(relativePath, fileName, modifiedDate, fileSize);
                var (thumbnail, revitYear) = ThumbnailExtractor.ExtractFromRfa(fullPath);

                workItems.Add(new ExtractionWorkItem
                {
                    FamilyId = familyId,
                    FullPath = fullPath,
                    RelativePath = relativePath,
                    ThumbnailPng = thumbnail,
                    FileRevitYear = revitYear
                });
            }

            _repository.DeleteStaleEntries(scannedPaths);
            return workItems;
        }
    }
}
