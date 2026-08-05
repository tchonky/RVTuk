using System;
using System.Collections.Generic;
using System.IO;

namespace RVTuk.Core.Shared.Config
{
    /// <summary>One model's remembered DWG export output folder (see AppConfig.DwgExportFolders).</summary>
    public class DwgExportFolderEntry
    {
        public string ModelKey { get; set; } = string.Empty;
        public string Folder { get; set; } = string.Empty;
    }

    public class AppConfig
    {
        /// <summary>Name of the folder inside the library root that holds the shared databases.</summary>
        public const string DbFolderName = ".DB";

        /// <summary>Pre-rename name of the DB folder; migrated to <see cref="DbFolderName"/> on sight.</summary>
        public const string LegacyDbFolderName = ".Setup";

        public string LibraryFolderPath { get; set; } = string.Empty;

        /// <summary>Last-used output folder for the Area Calc (Rishui Zamin) export, remembered
        /// across sessions so the user doesn't have to re-browse every time.</summary>
        public string AreaCalcOutputFolder { get; set; } = string.Empty;

        /// <summary>Last-used marker encoding for the Area Calc export, remembered across
        /// sessions so the user doesn't have to re-pick it every time.</summary>
        public RishuiZamin.MarkerForm AreaCalcMarkerForm { get; set; } = RishuiZamin.MarkerForm.FormA;

        /// <summary>Last-used values for the DWG Export dialog, remembered across sessions.
        /// Setup/set names are matched by name next time; a name that no longer exists in the
        /// open document silently falls back to the first available entry. The output folder is
        /// remembered per model (<see cref="DwgExportFolders"/>); <see cref="DwgExportFolder"/>
        /// is the global fallback a model without its own entry inherits.</summary>
        public string DwgExportFolder { get; set; } = string.Empty;
        public string DwgExportPdfSetupName { get; set; } = string.Empty;
        public string DwgExportDwgSetupName { get; set; } = string.Empty;
        public string DwgExportSheetSetName { get; set; } = string.Empty;
        public bool DwgExportUseCurrentWindow { get; set; }

        /// <summary>Last-used naming setup for non-sheet views. Empty means the built-in
        /// "&lt;View Name&gt;" entry (the view's own name) — the behaviour before views could
        /// take a rule, and still the default, so an absent key and the default agree.</summary>
        public string DwgExportViewNamingSetupName { get; set; } = string.Empty;

        /// <summary>False (the absent-key value) means PDFs and DWGs share one folder.</summary>
        public bool DwgExportSeparatePdfFolder { get; set; }

        /// <summary>False (the absent-key value) means an extra model missing a named export
        /// setup is skipped, rather than having the setup created in it.</summary>
        public bool DwgExportCopyMissingSetups { get; set; }

        /// <summary>False (the absent-key value) means no transmittal archive is written.</summary>
        public bool DwgExportCreateZip { get; set; }

        /// <summary>Topo Tools: distance between generated toposolid points, in centimetres.
        /// Reads back as 0 from any config file written before this property existed — net48's
        /// DataContractJsonSerializer skips property initializers (same trap the DWG format
        /// checkboxes below dodge by inverting their meaning). A spacing has no usable zero, so
        /// the pane treats anything &lt;= 0 as "use the default" instead. That also absorbs the
        /// rename from the millimetre key this started as: the old key is simply not read, and the
        /// default takes over.</summary>
        public double TopoPointSpacingCentimetres { get; set; } = 100;

        /// <summary>Last-used format checkboxes, stored inverted/additive (false = the
        /// out-of-box state "DWG on, PDF off"): net48's DataContractJsonSerializer skips
        /// property initializers, so a true-default property would flip to false when
        /// loading a config file written before this feature existed.</summary>
        public bool DwgExportDwgOff { get; set; }
        public bool DwgExportPdfOn { get; set; }

        /// <summary>Per-model output folders, most-recently-used last, capped at
        /// <see cref="DwgExportFolderCap"/> so the config file can't grow unbounded.</summary>
        public System.Collections.Generic.List<DwgExportFolderEntry> DwgExportFolders { get; set; } =
            new System.Collections.Generic.List<DwgExportFolderEntry>();

        public const int DwgExportFolderCap = 30;

        /// <summary>Per-model PDF output folders, used only when
        /// <see cref="DwgExportSeparatePdfFolder"/> is on; same MRU/cap rules as
        /// <see cref="DwgExportFolders"/>, with <see cref="DwgExportPdfFolder"/> as the
        /// global fallback.</summary>
        public string DwgExportPdfFolder { get; set; } = string.Empty;

        public List<DwgExportFolderEntry> DwgExportPdfFolders { get; set; } =
            new List<DwgExportFolderEntry>();

        /// <summary>The remembered output folder for a model (key = document path, or title for
        /// unsaved documents), falling back to the global last-used folder.</summary>
        public string GetDwgExportFolder(string modelKey)
            => Lookup(DwgExportFolders, modelKey) ?? DwgExportFolder;

        /// <summary>Upserts the model's folder (MRU: entry moves to the end; oldest entries are
        /// dropped past the cap) and updates the global fallback.</summary>
        public void SetDwgExportFolder(string modelKey, string folder)
        {
            DwgExportFolder = folder;
            DwgExportFolders = Upsert(DwgExportFolders, modelKey, folder);
        }

        /// <summary>The remembered PDF folder for a model, falling back to the global PDF
        /// folder and then to the DWG folder — where PDFs went before they could have one of
        /// their own, so an upgraded config keeps behaving the same.</summary>
        public string GetDwgExportPdfFolder(string modelKey)
        {
            var perModel = Lookup(DwgExportPdfFolders, modelKey);
            if (perModel != null) return perModel;
            return string.IsNullOrWhiteSpace(DwgExportPdfFolder)
                ? GetDwgExportFolder(modelKey)
                : DwgExportPdfFolder;
        }

        public void SetDwgExportPdfFolder(string modelKey, string folder)
        {
            DwgExportPdfFolder = folder;
            DwgExportPdfFolders = Upsert(DwgExportPdfFolders, modelKey, folder);
        }

        private static string? Lookup(List<DwgExportFolderEntry>? entries, string modelKey)
        {
            var entry = entries?.Find(e =>
                string.Equals(e.ModelKey, modelKey, StringComparison.OrdinalIgnoreCase));
            return entry != null && !string.IsNullOrWhiteSpace(entry.Folder) ? entry.Folder : null;
        }

        /// <summary>MRU upsert. Takes and returns the list because net48's
        /// DataContractJsonSerializer skips property initializers: a list absent from an older
        /// config file arrives null, and dereferencing it would throw before the dialog opens.</summary>
        private static List<DwgExportFolderEntry> Upsert(
            List<DwgExportFolderEntry>? entries, string modelKey, string folder)
        {
            var list = entries ?? new List<DwgExportFolderEntry>();
            if (string.IsNullOrWhiteSpace(modelKey)) return list;

            list.RemoveAll(e => string.Equals(e.ModelKey, modelKey, StringComparison.OrdinalIgnoreCase));
            list.Add(new DwgExportFolderEntry { ModelKey = modelKey, Folder = folder });
            while (list.Count > DwgExportFolderCap) list.RemoveAt(0);
            return list;
        }

        // Derived — never stored separately; always lives inside the library folder.
        public string DatabasePath => Path.Combine(LibraryFolderPath, DbFolderName, "RVTuk.db");

        /// <summary>
        /// Subfolder paths (relative to the library root, using '\' separators) that should be
        /// excluded from deep scans and fast syncs. Families under these folders are skipped during
        /// extraction but their existing DB rows are preserved (not treated as stale).
        /// </summary>
        public System.Collections.Generic.List<string> IgnoredSubfolders { get; set; } = new System.Collections.Generic.List<string>();

        /// <summary>
        /// Regular expressions matched (case-insensitively) against the file NAME; matching
        /// families get the same treatment as ignored subfolders — skipped by scans, hidden in
        /// the browser, existing DB rows preserved. Seeded with the Revit backup-file pattern
        /// (e.g. "Door.0001.rfa") so backups never clutter the library. See
        /// <see cref="Util.IgnoredFileMatcher"/> for the matching rules.
        /// </summary>
        public System.Collections.Generic.List<string> IgnoredFilePatterns { get; set; } =
            new System.Collections.Generic.List<string> { @".*\.\d{4}\.rfa" };

        /// <summary>
        /// One-time folder rename for libraries created before the DB folder was renamed from
        /// ".Setup" to ".DB": renames the old folder (databases and all) so curated data —
        /// instructions, tags, favourites, custom thumbnails — survives the update. No-op when
        /// ".DB" already exists or there is nothing to migrate. Best-effort: if the rename fails
        /// (folder locked by another Revit session, read-only share), callers proceed exactly as
        /// they would for a brand-new library. Call before touching <see cref="DatabasePath"/>.
        /// </summary>
        public static void MigrateLegacyDbFolder(string libraryFolderPath)
        {
            if (string.IsNullOrWhiteSpace(libraryFolderPath)) return;
            try
            {
                var newDir = Path.Combine(libraryFolderPath, DbFolderName);
                var oldDir = Path.Combine(libraryFolderPath, LegacyDbFolderName);
                if (!Directory.Exists(newDir) && Directory.Exists(oldDir))
                    Directory.Move(oldDir, newDir);
            }
            catch
            {
                // Locked or unwritable — leave the legacy folder alone; a fresh .DB gets created.
            }
        }
    }
}
