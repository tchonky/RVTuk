using System;
using System.IO;

namespace RVTuk.Core.Shared.Config
{
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
        /// open document silently falls back to the first available entry.</summary>
        public string DwgExportFolder { get; set; } = string.Empty;
        public string DwgExportPdfSetupName { get; set; } = string.Empty;
        public string DwgExportDwgSetupName { get; set; } = string.Empty;
        public string DwgExportSheetSetName { get; set; } = string.Empty;
        public bool DwgExportUseCurrentWindow { get; set; }

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
