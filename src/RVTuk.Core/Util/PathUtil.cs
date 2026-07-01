using System;
using System.Collections.Generic;
using System.IO;

namespace RVTuk.Core.Util
{
    public static class PathUtil
    {
        /// <summary>
        /// Recursively enumerates files matching <paramref name="searchPattern"/> under
        /// <paramref name="root"/> WITHOUT throwing on directories or files that exceed the
        /// Windows MAX_PATH (260) limit or that the user cannot access. .NET Framework's
        /// <c>Directory.GetFiles(..., AllDirectories)</c> aborts the whole walk the instant it
        /// meets one over-long/inaccessible path; this skips only the offending subtree and keeps
        /// going, so a single deeply-nested family can't break an entire library scan. Both the
        /// deep-scan indexer (Core) and the browser sync (UI) go through here.
        /// </summary>
        public static IEnumerable<string> SafeEnumerateFiles(string root, string searchPattern,
            Func<string, bool>? skipDirectory = null)
        {
            var pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                var dir = pending.Pop();

                string[] files;
                try { files = Directory.GetFiles(dir, searchPattern); }
                catch { files = Array.Empty<string>(); } // PathTooLong / Unauthorized / IO / Security
                foreach (var file in files)
                    yield return file;

                string[] subDirs;
                try { subDirs = Directory.GetDirectories(dir); }
                catch { subDirs = Array.Empty<string>(); }
                foreach (var sub in subDirs)
                {
                    // Skip whole subtrees the caller wants ignored (e.g. ignored library
                    // subfolders) so we never walk — or count — the files inside them.
                    if (skipDirectory != null && skipDirectory(sub)) continue;
                    pending.Push(sub);
                }
            }
        }

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

        /// <summary>
        /// Returns <c>true</c> if <paramref name="relativePath"/> falls under any folder in
        /// <paramref name="ignored"/>. Each ignored entry is normalised (trimmed, leading/trailing
        /// '\\' and '/' stripped). An entry "Archive" matches "Archive\\old\\x.rfa" and "Archive"
        /// itself, but NOT "ArchiveStuff\\x.rfa" — the check is always on a backslash segment
        /// boundary. Comparison is case-insensitive. Empty/whitespace entries are skipped.
        /// </summary>
        public static bool IsUnderIgnoredFolder(string relativePath, IEnumerable<string> ignored)
        {
            if (ignored == null) return false;
            foreach (var raw in ignored)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var entry = raw.Trim().Trim('\\', '/');
                if (string.IsNullOrEmpty(entry)) continue;

                // Match: path equals the entry, or path starts with entry + a separator.
                // Both '\\' and '/' count as boundaries — callers may pass either form.
                if (relativePath.Equals(entry, StringComparison.OrdinalIgnoreCase) ||
                    relativePath.StartsWith(entry + "\\", StringComparison.OrdinalIgnoreCase) ||
                    relativePath.StartsWith(entry + "/", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
