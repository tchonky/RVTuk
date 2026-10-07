using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using RVTuk.Core.FamilyBrowser.Models;
using Microsoft.Data.Sqlite;
using SQLiteConnection = Microsoft.Data.Sqlite.SqliteConnection;
using SQLiteCommand   = Microsoft.Data.Sqlite.SqliteCommand;
using SQLiteParameter = Microsoft.Data.Sqlite.SqliteParameter;

namespace RVTuk.Core.FamilyBrowser.Database
{
    public class IndexRepository : IDisposable
    {
        private readonly SQLiteConnection _connection;
        private readonly string _databasePath;

        public IndexRepository(string databasePath)
        {
            SqliteNative.EnsureLoaded();
            _databasePath = databasePath;

            if (System.IO.Directory.Exists(databasePath))
                throw new ArgumentException(
                    $"IndexDatabasePath points to a directory, not a file: \"{databasePath}\". " +
                    "Open Settings and set the path to a .db file.");

            // Create parent directory if needed (SQLite won't create it automatically)
            var dir = System.IO.Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(dir))
                System.IO.Directory.CreateDirectory(dir);

            // Pooling off: a pooled connection keeps its file handle open after Dispose, so the
            // scan's read-write handle would linger on the shared DB for the rest of the Revit
            // session and block other users (backlog "Read Only DB"). The scan holding the DB
            // *while it runs* is intended; holding it afterwards is not.
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString();
            _connection = new SQLiteConnection(connectionString);
            _connection.Open();

            // A throw below (read-only probe, locked/corrupt DB) never reaches Dispose: close here.
            try
            {
                // WAL is NOT safe across machines on a network filesystem (it relies on host-local
                // shared memory). Use a rollback journal so the DB can live on \\server\share.
                // busy_timeout lets brief writes wait for a lock instead of failing immediately.
                Execute(_connection, "PRAGMA busy_timeout=5000;");
                ProbeWritable();
                Execute(_connection, "PRAGMA journal_mode=DELETE;");
                Execute(_connection, "PRAGMA synchronous=NORMAL;");
                Execute(_connection, "PRAGMA foreign_keys=ON;");
                EnsureSchema(_connection);
            }
            catch
            {
                _connection.Dispose();
                throw;
            }
        }

        // SQLite silently degrades a ReadWriteCreate open to read-only when the file denies
        // write access — e.g. Autodesk Desktop Connector / OneDrive flag a cloud-synced DB
        // ReadOnly while another user holds it. Every IndexRepository caller is a writer
        // (scan/rescan), so surface that state as one clear error up front instead of an
        // "attempt to write a readonly database" from whichever later statement writes first.
        // sqlite3_db_readonly is SQLite's own report of the access it actually got.
        private void ProbeWritable()
        {
            if (SQLitePCL.raw.sqlite3_db_readonly(_connection.Handle, "main") == 1)
                throw new System.IO.IOException(
                    $"The library database is read-only: \"{_databasePath}\". Scans and rescans " +
                    "need write access. If the library lives in a cloud-synced folder (Autodesk " +
                    "Desktop Connector, OneDrive), the sync client may have flagged the file " +
                    "read-only while someone else has it open — try again later, or clear the " +
                    "file's read-only attribute.");
        }

        // The one schema definition, also used by BrowserRepository (which creates a DB no scan
        // has touched yet, and migrates an outdated one). Tables first, then the columns added
        // after a table first shipped.
        private static readonly (string Table, string Create)[] SchemaTables =
        {
            ("Families", @"CREATE TABLE IF NOT EXISTS Families (
                Id INTEGER PRIMARY KEY,
                RelativePath TEXT UNIQUE NOT NULL,
                FileName TEXT NOT NULL,
                ModifiedDate DATETIME NOT NULL,
                FileSize INTEGER NOT NULL,
                Category TEXT,
                IndexedDate DATETIME DEFAULT CURRENT_TIMESTAMP
            )"),
            ("Parameters", @"CREATE TABLE IF NOT EXISTS Parameters (
                Id INTEGER PRIMARY KEY,
                FamilyId INTEGER NOT NULL,
                ParameterName TEXT NOT NULL,
                DataType TEXT NOT NULL,
                IsInstance INTEGER NOT NULL,
                FOREIGN KEY (FamilyId) REFERENCES Families(Id) ON DELETE CASCADE
            )"),
            ("Thumbnail", @"CREATE TABLE IF NOT EXISTS Thumbnail (
                Id INTEGER PRIMARY KEY,
                FamilyId INTEGER UNIQUE NOT NULL,
                PngData BLOB NOT NULL,
                FOREIGN KEY (FamilyId) REFERENCES Families(Id) ON DELETE CASCADE
            )"),
            ("CustomThumbnail", @"CREATE TABLE IF NOT EXISTS CustomThumbnail (
                Id       INTEGER PRIMARY KEY,
                FamilyId INTEGER UNIQUE NOT NULL,
                PngData  BLOB    NOT NULL,
                OleSynced INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (FamilyId) REFERENCES Families(Id) ON DELETE CASCADE
            )"),
        };

        private static readonly (string Table, string Column, string Type)[] SchemaAddedColumns =
        {
            ("Families", "InstructionsXaml", "TEXT"),
            ("Parameters", "ParamGroup", "TEXT"),
            ("Parameters", "Kind", "TEXT"),
            ("Parameters", "Guid", "TEXT"),
            ("Parameters", "Formula", "TEXT"),
            ("Families", "RevitYear", "INTEGER NOT NULL DEFAULT 0"),
            ("Families", "Tags", "TEXT"),
            ("Families", "IsFavorite", "INTEGER NOT NULL DEFAULT 0"),
            ("Families", "ParametersExtracted", "INTEGER NOT NULL DEFAULT 0"),
            ("Families", "Version", "TEXT"),
        };

        internal static void EnsureSchema(SQLiteConnection c)
        {
            foreach (var (_, create) in SchemaTables)
                Execute(c, create);
            foreach (var (table, column, type) in SchemaAddedColumns)
                if (!ColumnExists(c, table, column))
                    Execute(c, $"ALTER TABLE {table} ADD COLUMN {column} {type}");
        }

        // True when EnsureSchema has nothing to do — readable on a read-only connection. Every
        // table has an Id column, so probing it also catches a table that is missing outright.
        internal static bool SchemaIsCurrent(SQLiteConnection c) =>
            SchemaTables.All(t => ColumnExists(c, t.Table, "Id"))
            && SchemaAddedColumns.All(a => ColumnExists(c, a.Table, a.Column));

        internal static bool ColumnExists(SQLiteConnection c, string table, string column)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'";
            return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
        }

        public FamilyModel? GetFamilyByPath(string relativePath)
        {
            // Case-insensitive lookup (preferring an exact-case row if one exists): Windows paths
            // are case-insensitive but the RelativePath key is not, so a case-only rename on disk
            // must still resolve to the existing row — otherwise the family is re-indexed as new
            // and its curated data (tags, favourites, instructions) is lost when the old row is
            // pruned as stale. SQLite's NOCASE folds ASCII only, which covers the drive-letter/
            // Latin part of library paths; Hebrew has no case to fold.
            using var cmd = CreateCommand(
                "SELECT Id, RelativePath, FileName, ModifiedDate, FileSize, Category, IndexedDate, Version FROM Families " +
                "WHERE RelativePath = @path COLLATE NOCASE " +
                "ORDER BY (CASE WHEN RelativePath = @path THEN 0 ELSE 1 END) LIMIT 1");
            AddParam(cmd, "@path", relativePath);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            return ReadFamily(reader);
        }

        /// <summary>
        /// Re-keys a row to the current on-disk casing of its path. Called by the indexer when
        /// <see cref="GetFamilyByPath"/> resolved a row whose stored casing differs from disk,
        /// so every later exact-match write (upserts, stale pruning) hits the same row.
        /// </summary>
        public void UpdateRelativePathCasing(long familyId, string relativePath, string fileName)
        {
            using var cmd = CreateCommand(
                "UPDATE Families SET RelativePath = @path, FileName = @name WHERE Id = @id");
            AddParam(cmd, "@path", relativePath);
            AddParam(cmd, "@name", fileName);
            AddParam(cmd, "@id", familyId);
            cmd.ExecuteNonQuery();
        }

        public List<string> GetAllRelativePaths()
        {
            var paths = new List<string>();
            using var cmd = CreateCommand("SELECT RelativePath FROM Families");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                paths.Add(reader.GetString(0));
            return paths;
        }

        public HashSet<long> GetFamilyIdsWithThumbnail()
        {
            var ids = new HashSet<long>();
            using var cmd = CreateCommand("SELECT DISTINCT FamilyId FROM Thumbnail");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                ids.Add(reader.GetInt64(0));
            return ids;
        }

        public HashSet<long> GetFamilyIdsWithParametersExtracted()
        {
            var ids = new HashSet<long>();
            using var cmd = CreateCommand("SELECT Id FROM Families WHERE ParametersExtracted = 1");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                ids.Add(reader.GetInt64(0));
            return ids;
        }

        public long InsertFamily(string relativePath, string fileName)
        {
            // Create/keep the row WITHOUT marking it current: a new row gets a sentinel size/date
            // (0 / DateTime.MinValue) so a never-extracted new family never matches its file; on a
            // RelativePath conflict we update FileName ONLY, so an existing changed family keeps its
            // old size/date. The real size/date is written later by UpdateFamilyMetadata once
            // extraction succeeds — that, not this insert, is what marks a family up to date. A
            // cancelled extraction therefore leaves the row stale and it is re-scanned next time.
            //
            // Two single statements rather than INSERT…;SELECT in one ExecuteScalar:
            // Microsoft.Data.Sqlite (Revit 2025 / net8) only returns the first result set
            // from a multi-statement command, so the trailing SELECT's Id would be lost.
            // RelativePath is UNIQUE, so the follow-up SELECT resolves the just-written row.
            using (var insert = CreateCommand(@"
                INSERT INTO Families (RelativePath, FileName, ModifiedDate, FileSize)
                VALUES (@path, @name, @modified, @size)
                ON CONFLICT(RelativePath) DO UPDATE SET FileName=@name;"))
            {
                AddParam(insert, "@path", relativePath);
                AddParam(insert, "@name", fileName);
                AddParam(insert, "@modified", DateTime.MinValue.ToString("o"));
                AddParam(insert, "@size", 0L);
                insert.ExecuteNonQuery();
            }

            using (var select = CreateCommand("SELECT Id FROM Families WHERE RelativePath = @path;"))
            {
                AddParam(select, "@path", relativePath);
                return (long)(select.ExecuteScalar() ?? throw new InvalidOperationException("Insert failed"));
            }
        }

        public void UpsertFamilyFileInfo(string relativePath, string fileName, long fileSize, DateTime modifiedDateUtc)
        {
            // A changed file invalidates the stored _Version value (it was read from the old
            // file contents by the deep scan) — clear it rather than let a stale value feed the
            // browser's update check, and drop ParametersExtracted so the next deep scan
            // re-extracts (otherwise the new size/date written here makes the file "look up to
            // date" and it would be skipped forever). Both dates are written by
            // DateTime.ToString("o"), so plain string equality detects "unchanged".
            using var cmd = CreateCommand(@"
                INSERT INTO Families (RelativePath, FileName, ModifiedDate, FileSize)
                VALUES (@path, @name, @modified, @size)
                ON CONFLICT(RelativePath) DO UPDATE SET
                    FileName = excluded.FileName,
                    Version = CASE WHEN Families.ModifiedDate = excluded.ModifiedDate
                                    AND Families.FileSize = excluded.FileSize
                              THEN Families.Version ELSE NULL END,
                    ParametersExtracted = CASE WHEN Families.ModifiedDate = excluded.ModifiedDate
                                                AND Families.FileSize = excluded.FileSize
                                          THEN Families.ParametersExtracted ELSE 0 END,
                    ModifiedDate = excluded.ModifiedDate,
                    FileSize = excluded.FileSize;");
            AddParam(cmd, "@path", relativePath);
            AddParam(cmd, "@name", fileName);
            AddParam(cmd, "@modified", modifiedDateUtc.ToString("o"));
            AddParam(cmd, "@size", fileSize);
            cmd.ExecuteNonQuery();
        }

        public void UpdateFamilyMetadata(long familyId, string? category, IReadOnlyList<ParameterModel> parameters, byte[]? thumbnailPng, int revitYear = 0,
            DateTime modifiedDate = default, long fileSize = 0, string? familyVersion = null)
        {
            using var transaction = _connection.BeginTransaction();
            try
            {
                // Write the file's real size/date HERE, in the same transaction as the extracted
                // metadata: a successful extraction is what marks the row current. (FamilyIndexer
                // inserts a sentinel size/date, so until this commits the family is re-scannable.)
                using var catCmd = CreateCommand("UPDATE Families SET Category=@cat, IndexedDate=@now, RevitYear=@year, ModifiedDate=@modified, FileSize=@size, ParametersExtracted=1, Version=@version WHERE Id=@id", transaction);
                AddParam(catCmd, "@version", (object?)familyVersion ?? DBNull.Value);
                AddParam(catCmd, "@cat", category ?? (object)DBNull.Value);
                AddParam(catCmd, "@now", DateTime.UtcNow.ToString("o"));
                AddParam(catCmd, "@year", revitYear);
                AddParam(catCmd, "@modified", modifiedDate.ToString("o"));
                AddParam(catCmd, "@size", fileSize);
                AddParam(catCmd, "@id", familyId);
                catCmd.ExecuteNonQuery();

                using var delCmd = CreateCommand("DELETE FROM Parameters WHERE FamilyId=@id", transaction);
                AddParam(delCmd, "@id", familyId);
                delCmd.ExecuteNonQuery();

                foreach (var p in parameters)
                {
                    using var pCmd = CreateCommand(
                        "INSERT INTO Parameters (FamilyId, ParameterName, DataType, IsInstance, ParamGroup, Kind, Guid, Formula) " +
                        "VALUES (@fid, @name, @type, @inst, @grp, @kind, @guid, @formula)",
                        transaction);
                    AddParam(pCmd, "@fid", familyId);
                    AddParam(pCmd, "@name", p.ParameterName);
                    AddParam(pCmd, "@type", p.DataType);
                    AddParam(pCmd, "@inst", p.IsInstance ? 1 : 0);
                    AddParam(pCmd, "@grp", (object?)p.ParamGroup ?? DBNull.Value);
                    AddParam(pCmd, "@kind", (object?)p.Kind ?? DBNull.Value);
                    AddParam(pCmd, "@guid", (object?)p.Guid ?? DBNull.Value);
                    AddParam(pCmd, "@formula", (object?)p.Formula ?? DBNull.Value);
                    pCmd.ExecuteNonQuery();
                }

                if (thumbnailPng != null)
                {
                    using var thumbCmd = CreateCommand(
                        "INSERT OR REPLACE INTO Thumbnail (FamilyId, PngData) VALUES (@fid, @png)",
                        transaction);
                    AddParam(thumbCmd, "@fid", familyId);
                    AddParam(thumbCmd, "@png", thumbnailPng);
                    thumbCmd.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public void UpdateThumbnailOnly(long familyId, byte[] thumbnailPng, int revitYear, DateTime modifiedDate, long fileSize)
        {
            using var transaction = _connection.BeginTransaction();
            try
            {
                using var thumbCmd = CreateCommand(
                    "INSERT OR REPLACE INTO Thumbnail (FamilyId, PngData) VALUES (@fid, @png)", transaction);
                AddParam(thumbCmd, "@fid", familyId);
                AddParam(thumbCmd, "@png", thumbnailPng);
                thumbCmd.ExecuteNonQuery();

                // Same staleness rule as UpsertFamilyFileInfo: a changed file invalidates the
                // stored _Version and needs its parameters re-extracted (this path refreshes
                // only the thumbnail).
                using var famCmd = CreateCommand(
                    "UPDATE Families SET RevitYear=@year, " +
                    "Version = CASE WHEN ModifiedDate=@modified AND FileSize=@size THEN Version ELSE NULL END, " +
                    "ParametersExtracted = CASE WHEN ModifiedDate=@modified AND FileSize=@size THEN ParametersExtracted ELSE 0 END, " +
                    "ModifiedDate=@modified, FileSize=@size, IndexedDate=@now WHERE Id=@id",
                    transaction);
                AddParam(famCmd, "@year", revitYear);
                AddParam(famCmd, "@modified", modifiedDate.ToString("o"));
                AddParam(famCmd, "@size", fileSize);
                AddParam(famCmd, "@now", DateTime.UtcNow.ToString("o"));
                AddParam(famCmd, "@id", familyId);
                famCmd.ExecuteNonQuery();

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public void DeleteStaleEntries(IEnumerable<string> validRelativePaths)
        {
            // Ignore-case like the scanner's own path set: a row whose stored casing differs from
            // the scanned path is still the same file on a Windows filesystem, not a stale row.
            var valid = new HashSet<string>(validRelativePaths, StringComparer.OrdinalIgnoreCase);
            var paths = GetAllRelativePaths();

            // One transaction, not an autocommit (journal round-trip over SMB) per row. The
            // Parameters/Thumbnail/CustomThumbnail rows go with each family (ON DELETE CASCADE).
            using var transaction = _connection.BeginTransaction();
            using var cmd = CreateCommand("DELETE FROM Families WHERE RelativePath=@path", transaction);
            var pathParam = cmd.Parameters.Add("@path", SqliteType.Text);
            foreach (var path in paths)
            {
                if (valid.Contains(path)) continue;
                pathParam.Value = path;
                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
        }

        private SQLiteCommand CreateCommand(string sql, IDbTransaction? transaction = null)
        {
            var cmd = (SQLiteCommand)_connection.CreateCommand();
            cmd.CommandText = sql;
            if (transaction != null)
            {
                cmd.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            }
            return cmd;
        }

        private static void AddParam(IDbCommand cmd, string name, object? value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        private static FamilyModel ReadFamily(IDataReader r) => new FamilyModel
        {
            Id           = r.GetInt64(0),
            RelativePath = r.GetString(1),
            FileName     = r.GetString(2),
            ModifiedDate = DbConvert.ParseUtc(r.GetString(3)),
            FileSize     = r.GetInt64(4),
            Category     = r.IsDBNull(5) ? null : r.GetString(5),
            IndexedDate  = DbConvert.ParseUtc(r.GetString(6)),
            Version      = r.IsDBNull(7) ? null : r.GetString(7)
        };

        private static void Execute(SQLiteConnection c, string sql)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        public void Dispose() => _connection.Dispose();
    }
}
