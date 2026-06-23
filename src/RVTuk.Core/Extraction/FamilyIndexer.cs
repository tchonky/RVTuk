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
            CancellationToken cancellationToken = default)
        {
            // Robust walk: skips over-long/inaccessible paths instead of aborting the whole scan.
            var rfaFiles = new List<string>(PathUtil.SafeEnumerateFiles(_libraryRoot, "*.rfa"));
            int total = rfaFiles.Count;
            var workItems = new List<ExtractionWorkItem>();
            var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            SkippedLongPath = 0;
            SkippedIgnored = 0;

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

                if (PathUtil.IsUnderIgnoredFolder(relativePath, _ignoredSubfolders))
                {
                    // Family is in an ignored subfolder: skip extraction but add to scannedPaths
                    // so DeleteStaleEntries does not remove any already-indexed rows for it.
                    SkippedIgnored++;
                    scannedPaths.Add(relativePath);
                    continue;
                }

                scannedPaths.Add(relativePath);

                var info = new FileInfo(fullPath);
                long fileSize = info.Length;
                DateTime modifiedDate = info.LastWriteTimeUtc;

                var existing = _repository.GetFamilyByPath(relativePath);

                bool needsExtraction = existing == null
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
