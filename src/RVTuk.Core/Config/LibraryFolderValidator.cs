using System;
using System.IO;

namespace RVTuk.Core.Config
{
    /// <summary>
    /// Validates that a chosen library folder can serve as the family-library root: either it
    /// already contains the database, or the folder is writable so a new one can be created.
    /// </summary>
    public static class LibraryFolderValidator
    {
        public const string DbMissingNoWriteMessage =
            "Database not found, and no permission to write in this folder to create a new one.";

        /// <summary>
        /// Returns null when <paramref name="libraryFolderPath"/> is usable, otherwise a
        /// user-facing message describing why it cannot be used.
        /// </summary>
        public static string? Validate(string libraryFolderPath)
        {
            if (string.IsNullOrWhiteSpace(libraryFolderPath))
                return "Library folder path is required.";
            if (!Directory.Exists(libraryFolderPath))
                return "Library folder does not exist.";

            var setupDir = Path.Combine(libraryFolderPath, ".Setup");
            var dbPath = Path.Combine(setupDir, "RVTuk.db");

            // An existing DB is enough — browsing works even on a read-only share.
            if (File.Exists(dbPath))
                return null;

            // No DB yet: we must be able to write here to create a new one. Probe the deepest
            // existing folder so we don't create .Setup just to test a folder we may not use.
            var probeDir = Directory.Exists(setupDir) ? setupDir : libraryFolderPath;
            return CanWriteInto(probeDir) ? null : DbMissingNoWriteMessage;
        }

        private static bool CanWriteInto(string directory)
        {
            try
            {
                var probe = Path.Combine(directory, "._rvtuk_write_probe_" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
