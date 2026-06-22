using System;
using System.IO;

namespace ReviTchucky.Core.Util
{
    public static class PathUtil
    {
        /// <summary>
        /// Returns <paramref name="fullPath"/> relative to <paramref name="root"/> using
        /// '\' separators. This is the single source of truth for the value stored in
        /// <c>Families.RelativePath</c> (the UNIQUE key). It must stay deterministic across
        /// build targets, because the index database is shared by every Revit version on the
        /// machine — the deep-scan indexer (Core) and the browser sync (UI) both call this so
        /// the same file always produces the same key. A plain prefix-strip is used (rather than
        /// <c>Path.GetRelativePath</c>, which only exists on net5+) so net48 and net8 builds
        /// agree byte-for-byte.
        /// </summary>
        public static string GetRelativePath(string root, string fullPath)
        {
            if (string.IsNullOrEmpty(root)) return fullPath;

            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                && !root.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                root += Path.DirectorySeparatorChar;

            return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(root.Length)
                : fullPath;
        }
    }
}
