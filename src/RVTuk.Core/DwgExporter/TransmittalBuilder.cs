using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RVTuk.Core.DwgExporter
{
    /// <summary>
    /// Decides what belongs in the transmittal archive, given the files an export produced and
    /// the fonts its setup maps to. Pure — no disk access; the caller supplies both lists.
    /// </summary>
    public static class TransmittalBuilder
    {
        private static readonly string[] ImageExtensions =
            { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".gif" };

        public static TransmittalContents Build(
            IReadOnlyList<string> producedFiles,
            IReadOnlyList<ResolvedFont> fonts,
            string archivePath)
        {
            var entries = producedFiles
                .Where(path => !IsExtension(path, ".pdf"))
                .Where(path => !string.Equals(path, archivePath, StringComparison.OrdinalIgnoreCase))
                .Select(path => new TransmittalEntry
                {
                    SourcePath = path,
                    EntryName = Path.GetFileName(path),
                    Kind = KindOf(path),
                })
                .ToList();

            // Several text styles commonly map to one SHX, so bundle each file once.
            entries.AddRange(fonts
                .Where(f => !string.IsNullOrWhiteSpace(f.FilePath))
                .GroupBy(f => f.FilePath!, StringComparer.OrdinalIgnoreCase)
                .Select(g => new TransmittalEntry
                {
                    SourcePath = g.Key,
                    EntryName = Path.GetFileName(g.Key),
                    Kind = TransmittalKind.Font,
                }));

            return new TransmittalContents
            {
                Entries = entries,
                FontsNotIncluded = fonts
                    .Where(f => string.IsNullOrWhiteSpace(f.FilePath))
                    .Select(f => f.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            };
        }

        /// <summary>What a produced file is, judged by extension — all we can judge it by.</summary>
        public static TransmittalKind KindOf(string path)
        {
            if (IsExtension(path, ".dwg")) return TransmittalKind.Drawing;
            return ImageExtensions.Any(ext => IsExtension(path, ext))
                ? TransmittalKind.Image
                : TransmittalKind.Other;
        }

        private static bool IsExtension(string path, string extension)
            => string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase);
    }
}
