using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RVTuk.Core.DwgExporter
{
    /// <summary>What appeared in the output folder while one drawing was being exported.</summary>
    public class DrawingProduction
    {
        public PlannedExportFile File { get; set; } = new PlannedExportFile();
        public IReadOnlyList<string> ProducedFiles { get; set; } = new List<string>();
    }

    /// <summary>One drawing's archive: where it goes, and what belongs in it.</summary>
    public class DrawingBundle
    {
        public string ArchivePath { get; set; } = "";
        public IReadOnlyList<string> Files { get; set; } = new List<string>();
    }

    /// <summary>
    /// Splits a run into one archive per drawing, named after it.
    ///
    /// Drawings are attributed exactly: snapshotting the output folder between views tells us
    /// which `.dwg` files each one produced, including the xref'd view drawings a setup with
    /// MergedViews off emits alongside a sheet, which must travel with it.
    ///
    /// Everything else — images above all — goes into every archive. Revit writes one copy of
    /// an image for the whole run, during whichever drawing happened to use it first, and
    /// without a DWG reader we cannot learn who else references it. Duplicating bytes beats
    /// shipping a broken link.
    ///
    /// Two kinds of file are never bundled: PDFs, because this is a CAD transmittal, and
    /// archives, so a zip already sitting in the folder is never swallowed by the next one.
    /// A drawing that produced nothing of its own gets no archive at all — one holding
    /// nothing but other sheets' images would claim a handover that isn't there.
    /// </summary>
    public static class PerDrawingTransmittal
    {
        public static IReadOnlyList<DrawingBundle> Plan(
            string outputFolder, IReadOnlyList<DrawingProduction> productions)
        {
            var shared = productions
                .SelectMany(p => p.ProducedFiles)
                .Where(path => Bundleable(path) && !IsDrawing(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var bundles = new List<DrawingBundle>();
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var production in productions)
            {
                var own = production.ProducedFiles.Where(Bundleable).ToList();
                if (own.Count == 0) continue;

                var name = production.File.FileName;
                if (string.IsNullOrWhiteSpace(name) || !claimed.Add(name)) continue;

                bundles.Add(new DrawingBundle
                {
                    ArchivePath = Path.Combine(outputFolder, name + ".zip"),
                    Files = own.Concat(shared).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                });
            }

            return bundles;
        }

        private static bool Bundleable(string path)
            => !HasExtension(path, ".pdf") && !HasExtension(path, ".zip");

        private static bool IsDrawing(string path) => HasExtension(path, ".dwg");

        private static bool HasExtension(string path, string extension)
            => string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase);
    }
}
