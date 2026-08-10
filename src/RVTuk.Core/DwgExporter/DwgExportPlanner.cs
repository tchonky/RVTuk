using System;
using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.DwgExporter
{
    /// <summary>
    /// Pre-flight check before any file is written: duplicate filenames (views would
    /// overwrite each other — always an abort) and files already on disk (needs one
    /// overwrite confirmation). Pure: disk access is injected.
    /// </summary>
    public static class DwgExportPlanner
    {
        public static DwgExportPlan Check(IReadOnlyList<PlannedExportFile> files, Func<string, bool> fileExists)
        {
            return new DwgExportPlan
            {
                Files = files,
                Duplicates = FindDuplicates(files),
                ExistingFileNames = files.Select(f => f.FileName).Where(fileExists).ToList(),
            };
        }

        /// <summary>Names produced by more than one view, each reported once under the spelling
        /// that occurred first, listing every view — across models — that would write it.</summary>
        private static IReadOnlyList<DuplicateFileName> FindDuplicates(IReadOnlyList<PlannedExportFile> files)
        {
            return files
                .GroupBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => new DuplicateFileName
                {
                    FileName = g.First().FileName,
                    Sources = g.Select(f => f.Source).ToList(),
                })
                .ToList();
        }
    }
}
