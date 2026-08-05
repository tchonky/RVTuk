using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace RVTuk.Core.DwgExporter
{
    /// <summary>
    /// Writes the transmittal archive. The only piece of the transmittal that touches disk.
    /// A source file that can't be read — deleted between the snapshot and now, or locked by
    /// another application — is skipped and reported rather than aborting the whole bundle.
    /// </summary>
    public static class TransmittalWriter
    {
        public const string ReportEntryName = "TRANSMITTAL.txt";

        /// <summary>Returns the files that had to be skipped, one message each.</summary>
        public static IReadOnlyList<string> Write(
            string archivePath, TransmittalContents contents, string reportText)
        {
            var warnings = new List<string>();

            if (File.Exists(archivePath)) File.Delete(archivePath);

            using (var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ReportEntryName };

                foreach (var entry in contents.Entries)
                {
                    try
                    {
                        var name = Unique(used, entry.EntryName);
                        zip.CreateEntryFromFile(entry.SourcePath, name);
                    }
                    catch (Exception ex)
                    {
                        warnings.Add(entry.SourcePath + ": not bundled (" + ex.Message + ")");
                    }
                }

                var report = zip.CreateEntry(ReportEntryName);
                using var writer = new StreamWriter(report.Open(), new UTF8Encoding(false));
                writer.Write(reportText);
            }

            return warnings;
        }

        /// <summary>Two folders can yield the same basename; keep both rather than lose one.</summary>
        private static string Unique(HashSet<string> used, string name)
        {
            if (used.Add(name)) return name;

            var stem = Path.GetFileNameWithoutExtension(name);
            var extension = Path.GetExtension(name);
            for (int i = 2; ; i++)
            {
                var candidate = stem + " (" + i + ")" + extension;
                if (used.Add(candidate)) return candidate;
            }
        }
    }
}
