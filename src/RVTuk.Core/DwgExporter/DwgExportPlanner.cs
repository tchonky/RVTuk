using System;
using System.Collections.Generic;
using System.Linq;

namespace RVTuk.Core.DwgExporter
{
    /// <summary>
    /// Pre-flight check before any file is written: duplicate filenames (sheets would
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
                DuplicateNames = FileNameComposer.FindDuplicates(files.Select(f => f.FileName)),
                ExistingFileNames = files.Select(f => f.FileName).Where(fileExists).ToList(),
            };
        }
    }
}
