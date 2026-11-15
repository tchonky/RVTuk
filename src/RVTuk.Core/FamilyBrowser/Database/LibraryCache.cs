using System;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace RVTuk.Core.FamilyBrowser.Database
{
    public enum CacheRefreshStatus
    {
        /// <summary>The library database changed; a fresh copy was taken.</summary>
        Updated,
        /// <summary>The local copy already matches the library database.</summary>
        UpToDate,
        /// <summary>The library database could not be read (offline, locked, half-synced);
        /// the previous local copy is used.</summary>
        Stale,
        /// <summary>No library database and no previous copy: nothing to browse yet.</summary>
        Unavailable,
    }

    public sealed class CacheRefreshResult
    {
        public CacheRefreshStatus Status { get; }
        /// <summary>The local copy to open; null when there is none.</summary>
        public string? DatabasePath { get; }
        /// <summary>When the library database the local copy came from was last saved (UTC).</summary>
        public DateTime? CopyOfUtc { get; }
        public string? Error { get; }

        public CacheRefreshResult(CacheRefreshStatus status, string? databasePath, DateTime? copyOfUtc,
            string? error = null)
        {
            Status = status;
            DatabasePath = databasePath;
            CopyOfUtc = copyOfUtc;
            Error = error;
        }
    }

    /// <summary>
    /// A local copy of the shared library database for users who can only read the library.
    /// The library sits on a cloud-sync folder (Desktop Connector); browsing it in place would
    /// hold the file open and stop the sync client from updating it, and the client may hand
    /// over a half-written file while it syncs. So readers browse a copy that is replaced only
    /// when the source changed AND the new copy passes SQLite's integrity check.
    /// </summary>
    public static class LibraryCache
    {
        /// <summary>
        /// True when this user can write the library database: the file (or, before the first
        /// scan, its folder) accepts writes. A view-only Autodesk Docs user gets read-only files
        /// from Desktop Connector, so this is false for them.
        /// </summary>
        public static bool CanWrite(string databasePath)
        {
            try
            {
                if (File.Exists(databasePath))
                {
                    if ((File.GetAttributes(databasePath) & FileAttributes.ReadOnly) != 0) return false;
                    // Opening for write changes nothing on disk (no timestamp, no sync).
                    using (new FileStream(databasePath, FileMode.Open, FileAccess.ReadWrite,
                               FileShare.ReadWrite | FileShare.Delete)) { }
                    return true;
                }

                // A new library has no .DB folder yet: probe the nearest folder that exists.
                var dir = Path.GetDirectoryName(databasePath);
                while (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) dir = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(dir)) return false;
                var probe = Path.Combine(dir, ".rvtuk-write-probe-" + Guid.NewGuid().ToString("N"));
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write,
                           FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                return true;
            }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }
        }

        /// <summary>The local copy last taken for this library, or null when there is none.</summary>
        public static CacheRefreshResult Current(string cacheDir)
        {
            var stamp = ReadStamp(cacheDir);
            var path = stamp == null ? null : Path.Combine(cacheDir, stamp.FileName);
            return path != null && File.Exists(path)
                ? new CacheRefreshResult(CacheRefreshStatus.Stale, path, stamp!.WriteUtc)
                : new CacheRefreshResult(CacheRefreshStatus.Unavailable, null, null);
        }

        /// <summary>
        /// Brings the local copy in <paramref name="cacheDir"/> up to date with
        /// <paramref name="sourceDb"/>. Each copy gets its own file name, so a copy still open in
        /// this or another Revit session never blocks taking a newer one; older copies are
        /// deleted once nothing holds them. Never throws for an unreadable source: the result
        /// says which copy to open.
        /// </summary>
        public static CacheRefreshResult Refresh(string sourceDb, string cacheDir)
        {
            var current = Current(cacheDir);

            CacheRefreshResult Keep(string error) => new CacheRefreshResult(
                current.Status, current.DatabasePath, current.CopyOfUtc, error);

            Stamp before;
            try
            {
                if (!File.Exists(sourceDb)) return Keep("The library database was not found.");
                before = Stamp.Of(sourceDb, string.Empty);
            }
            catch (Exception ex) { return Keep(ex.Message); }

            var old = ReadStamp(cacheDir);
            if (current.DatabasePath != null && before.SameSource(old))
                return new CacheRefreshResult(CacheRefreshStatus.UpToDate, current.DatabasePath, before.WriteUtc);

            var fileName = "RVTuk-" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture) + ".db";
            var dest = Path.Combine(cacheDir, fileName);
            var tmp = dest + ".tmp";
            try
            {
                Directory.CreateDirectory(cacheDir);
                CopyShared(sourceDb, tmp);

                // A sync client that rewrote the file mid-copy leaves a torn copy: discard it.
                if (!Stamp.Of(sourceDb, string.Empty).SameSource(before))
                    return Keep("The library database changed while it was being copied.");
                if (!PassesIntegrityCheck(tmp))
                    return Keep("The library database copy is incomplete or damaged.");

                File.Move(tmp, dest);
                WriteStamp(cacheDir, new Stamp(before.WriteUtc, before.Length, fileName));
                DeleteOldCopies(cacheDir, fileName);
                return new CacheRefreshResult(CacheRefreshStatus.Updated, dest, before.WriteUtc);
            }
            catch (Exception ex)
            {
                return Keep(ex.Message);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort */ }
            }
        }

        // Copies still open elsewhere fail to delete and are retried on the next refresh.
        private static void DeleteOldCopies(string cacheDir, string keep)
        {
            foreach (var f in Directory.GetFiles(cacheDir, "RVTuk-*.db"))
            {
                if (string.Equals(Path.GetFileName(f), keep, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(f); } catch { /* in use */ }
            }
        }

        // Tolerates the sync client (or the librarian's Revit) holding the source open.
        private static void CopyShared(string source, string dest)
        {
            using var src = new FileStream(source, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
            src.CopyTo(dst);
        }

        private static bool PassesIntegrityCheck(string path)
        {
            SqliteNative.EnsureLoaded();
            try
            {
                using var c = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Mode = SqliteOpenMode.ReadOnly,
                    Pooling = false, // release the file so it can be moved right after
                }.ToString());
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "PRAGMA quick_check;";
                return string.Equals(cmd.ExecuteScalar() as string, "ok", StringComparison.OrdinalIgnoreCase);
            }
            catch (SqliteException) { return false; }
        }

        private const string StampFileName = "current.stamp";

        private static Stamp? ReadStamp(string cacheDir)
        {
            try
            {
                var path = Path.Combine(cacheDir, StampFileName);
                if (!File.Exists(path)) return null;
                var parts = File.ReadAllText(path).Split('|');
                if (parts.Length != 3) return null;
                return new Stamp(
                    new DateTime(long.Parse(parts[0], CultureInfo.InvariantCulture), DateTimeKind.Utc),
                    long.Parse(parts[1], CultureInfo.InvariantCulture),
                    parts[2]);
            }
            catch { return null; }
        }

        private static void WriteStamp(string cacheDir, Stamp s) =>
            File.WriteAllText(Path.Combine(cacheDir, StampFileName),
                s.WriteUtc.Ticks.ToString(CultureInfo.InvariantCulture) + "|" +
                s.Length.ToString(CultureInfo.InvariantCulture) + "|" + s.FileName);

        // The source's identity (last write + size) and the local copy taken of it.
        private sealed class Stamp
        {
            public DateTime WriteUtc { get; }
            public long Length { get; }
            public string FileName { get; }

            public Stamp(DateTime writeUtc, long length, string fileName)
            {
                WriteUtc = writeUtc;
                Length = length;
                FileName = fileName;
            }

            public static Stamp Of(string path, string fileName)
            {
                var fi = new FileInfo(path);
                return new Stamp(fi.LastWriteTimeUtc, fi.Length, fileName);
            }

            public bool SameSource(Stamp? other) =>
                other != null && other.WriteUtc == WriteUtc && other.Length == Length;
        }
    }
}
