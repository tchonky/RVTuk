using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using RVTuk.Core.DwgExporter;

namespace RVTuk.Revit.DwgExporter
{
    /// <summary>
    /// Works out which fonts a DWG export setup maps text to, and finds the SHX files for
    /// those it can. SHX fonts are the ones that actually go missing on a receiver's machine
    /// — TrueType is resolved by Windows and is not ours to redistribute — so only SHX is
    /// located here; everything else comes back unresolved for the report to list.
    /// </summary>
    internal static class FontCollector
    {
        /// <summary>The Fonts directories of any Autodesk product installed on this machine.
        /// AutoCAD ships the standard SHX set; DWG TrueView carries it too.</summary>
        public static IReadOnlyList<string> FontFolders()
        {
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            };

            var folders = new List<string>();
            foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r)))
            {
                var autodesk = Path.Combine(root, "Autodesk");
                if (!Directory.Exists(autodesk)) continue;

                foreach (var product in SafeDirectories(autodesk))
                {
                    var fonts = Path.Combine(product, "Fonts");
                    if (Directory.Exists(fonts)) folders.Add(fonts);
                }
            }
            return folders;
        }

        /// <summary>Every destination font the setup maps to, with the SHX file behind it when
        /// one could be found.</summary>
        public static IReadOnlyList<ResolvedFont> Collect(
            Document doc, string dwgSetupName, out List<string> warnings)
        {
            warnings = new List<string>();
            var names = DestinationFontNames(doc, dwgSetupName, warnings);
            if (names.Count == 0) return new List<ResolvedFont>();

            var folders = FontFolders();
            if (folders.Count == 0)
            {
                warnings.Add(
                    "No Autodesk font folder on this machine, so no SHX font could be bundled. " +
                    "The receiver must already have any SHX the drawings use.");
            }

            return names
                .Select(name => new ResolvedFont { Name = name, FilePath = FindShx(folders, name) })
                .ToList();
        }

        private static IReadOnlyList<string> DestinationFontNames(
            Document doc, string dwgSetupName, List<string> warnings)
        {
            try
            {
                if (dwgSetupName == DwgExportDefaults.DefaultDwgSetupName)
                    return new List<string>(); // stock options carry no font table worth reading

                var settings = ExportDWGSettings.FindByName(doc, dwgSetupName);
                if (settings == null) return new List<string>();

                var table = settings.GetDWGExportOptions().GetExportFontTable();
                if (table == null) return new List<string>();

                return table.GetValues()
                    .Select(info => info.DestinationFontName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                warnings.Add("Could not read the export setup's font table: " + ex.Message);
                return new List<string>();
            }
        }

        /// <summary>The table stores a font name, which may or may not carry the extension, so
        /// try both spellings. No match means TrueType (or simply absent) — either way it is
        /// listed for the receiver rather than bundled.</summary>
        private static string? FindShx(IReadOnlyList<string> folders, string name)
        {
            var candidates = name.EndsWith(".shx", StringComparison.OrdinalIgnoreCase)
                ? new[] { name }
                : new[] { name + ".shx" };

            foreach (var folder in folders)
            {
                foreach (var candidate in candidates)
                {
                    var path = Path.Combine(folder, candidate);
                    if (File.Exists(path)) return path;
                }
            }
            return null;
        }

        private static IEnumerable<string> SafeDirectories(string path)
        {
            try { return Directory.EnumerateDirectories(path); }
            catch { return Enumerable.Empty<string>(); }
        }
    }
}
