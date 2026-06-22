using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ReviTchucky.Core.Database;
using ReviTchucky.Core.Models;
using ReviTchucky.Core.Util;

namespace ReviTchucky.Core.Extraction
{
    public class FamilyIndexer
    {
        private readonly IndexRepository _repository;
        private readonly string _libraryRoot;

        public FamilyIndexer(IndexRepository repository, string libraryRootPath)
        {
            _repository = repository;
            _libraryRoot = libraryRootPath;
        }

        /// <summary>
        /// Scans the library folder. Returns files needing Revit API metadata extraction.
        /// Thumbnails (OLE) are extracted here and embedded in the work items.
        /// </summary>
        public IReadOnlyList<ExtractionWorkItem> Scan(
            Action<string, int, int> progressCallback,
            CancellationToken cancellationToken = default)
        {
            var rfaFiles = Directory.GetFiles(_libraryRoot, "*.rfa", SearchOption.AllDirectories);
            int total = rfaFiles.Length;
            var workItems = new List<ExtractionWorkItem>();
            var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fullPath = rfaFiles[i];
                string relativePath = PathUtil.GetRelativePath(_libraryRoot, fullPath);
                string fileName = Path.GetFileName(fullPath);

                progressCallback(fileName, i + 1, total);
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
