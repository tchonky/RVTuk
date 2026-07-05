using System.IO;

namespace RVTuk.Core.Config
{
    public class AppConfig
    {
        public string LibraryFolderPath { get; set; } = string.Empty;

        /// <summary>Last-used output folder for the Area Calc (Rishui Zamin) export, remembered
        /// across sessions so the user doesn't have to re-browse every time.</summary>
        public string AreaCalcOutputFolder { get; set; } = string.Empty;

        // Derived — never stored separately; always lives inside the library folder.
        public string DatabasePath => Path.Combine(LibraryFolderPath, ".Setup", "RVTuk.db");

        // Derived — the Project Comparator's snapshots + Standard live alongside the family DB.
        public string StandardsDatabasePath => Path.Combine(LibraryFolderPath, ".Setup", "RVTuk.Standards.db");

        /// <summary>
        /// Subfolder paths (relative to the library root, using '\' separators) that should be
        /// excluded from deep scans and fast syncs. Families under these folders are skipped during
        /// extraction but their existing DB rows are preserved (not treated as stale).
        /// </summary>
        public System.Collections.Generic.List<string> IgnoredSubfolders { get; set; } = new System.Collections.Generic.List<string>();
    }
}
