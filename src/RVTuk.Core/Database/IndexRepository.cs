using System;
using System.Collections.Generic;
using System.Data;
using RVTuk.Core.Models;
using Microsoft.Data.Sqlite;
using SQLiteConnection = Microsoft.Data.Sqlite.SqliteConnection;
using SQLiteCommand   = Microsoft.Data.Sqlite.SqliteCommand;
using SQLiteParameter = Microsoft.Data.Sqlite.SqliteParameter;

namespace RVTuk.Core.Database
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

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();
            _connection = new SQLiteConnection(connectionString);
            _connection.Open();

            // WAL is NOT safe across machines on a network filesystem (it relies on host-local
            // shared memory). Use a rollback journal so the DB can live on \\server\share.
            // busy_timeout lets brief writes wait for a lock instead of failing immediately.
            Execute("PRAGMA busy_timeout=5000;");
            Execute("PRAGMA journal_mode=DELETE;");
            Execute("PRAGMA synchronous=NORMAL;");
            Execute("PRAGMA foreign_keys=ON;");
            CreateSchemaIfNeeded();
            MigrateSchema();
        }

        private void CreateSchemaIfNeeded()
        {
            Execute(@"
                CREATE TABLE IF NOT EXISTS Families (
                    Id INTEGER PRIMARY KEY,
                    RelativePath TEXT UNIQUE NOT NULL,
                    FileName TEXT NOT NULL,
                    ModifiedDate DATETIME NOT NULL,
                    FileSize INTEGER NOT NULL,
                    Category TEXT,
                    IndexedDate DATETIME DEFAULT CURRENT_TIMESTAMP
                );
                CREATE TABLE IF NOT EXISTS Parameters (
                    Id INTEGER PRIMARY KEY,
                    FamilyId INTEGER NOT NULL,
                    ParameterName TEXT NOT NULL,
                    DataType TEXT NOT NULL,
                    IsInstance INTEGER NOT NULL,
                    FOREIGN KEY (FamilyId) REFERENCES Families(Id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS Thumbnail (
                    Id INTEGER PRIMARY KEY,
                    FamilyId INTEGER UNIQUE NOT NULL,
                    PngData BLOB NOT NULL,
                    FOREIGN KEY (FamilyId) REFERENCES Families(Id) ON DELETE CASCADE
                );");
        }

        private void MigrateSchema()
        {
            // Add InstructionsXaml column to Families if missing
            using var checkCmd = _connection.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Families') WHERE name='InstructionsXaml'";
            var count = (long)(checkCmd.ExecuteScalar() ?? 0L);
            if (count == 0)
                Execute("ALTER TABLE Families ADD COLUMN InstructionsXaml TEXT");

            Execute(@"CREATE TABLE IF NOT EXISTS CustomThumbnail (
                Id       INTEGER PRIMARY KEY,
                FamilyId INTEGER UNIQUE NOT NULL,
                PngData  BLOB    NOT NULL,
                OleSynced INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (FamilyId) REFERENCES Families(Id) ON DELETE CASCADE
            )");

            Execute(@"CREATE TABLE IF NOT EXISTS FamilyImage (
                Id        INTEGER PRIMARY KEY,
                FamilyId  INTEGER NOT NULL,
                FileName  TEXT    NOT NULL,
                Caption   TEXT,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (FamilyId) REFERENCES Families(Id) ON DELETE CASCADE
            )");

            foreach (var col in new[] { "ParamGroup", "Kind", "Guid", "Formula" })
            {
                using var c = _connection.CreateCommand();
                c.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('Parameters') WHERE name='{col}'";
                if ((long)(c.ExecuteScalar() ?? 0L) == 0)
                    Execute($"ALTER TABLE Parameters ADD COLUMN {col} TEXT");
            }

            // Add RevitYear column to Families if missing
            using var yearCheck = _connection.CreateCommand();
            yearCheck.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Families') WHERE name='RevitYear'";
            if ((long)(yearCheck.ExecuteScalar() ?? 0L) == 0)
                Execute("ALTER TABLE Families ADD COLUMN RevitYear INTEGER NOT NULL DEFAULT 0");

            using var tagsCheck = _connection.CreateCommand();
            tagsCheck.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Families') WHERE name='Tags'";
            if ((long)(tagsCheck.ExecuteScalar() ?? 0L) == 0)
                Execute("ALTER TABLE Families ADD COLUMN Tags TEXT");

            using var favCheck = _connection.CreateCommand();
            favCheck.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Families') WHERE name='IsFavorite'";
            if ((long)(favCheck.ExecuteScalar() ?? 0L) == 0)
                Execute("ALTER TABLE Families ADD COLUMN IsFavorite INTEGER NOT NULL DEFAULT 0");

            using var paramsExtractedCheck = _connection.CreateCommand();
            paramsExtractedCheck.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Families') WHERE name='ParametersExtracted'";
            if ((long)(paramsExtractedCheck.ExecuteScalar() ?? 0L) == 0)
                Execute("ALTER TABLE Families ADD COLUMN ParametersExtracted INTEGER NOT NULL DEFAULT 0");
        }

        public FamilyModel? GetFamilyByPath(string relativePath)
        {
            using var cmd = CreateCommand(
                "SELECT Id, RelativePath, FileName, ModifiedDate, FileSize, Category, IndexedDate FROM Families WHERE RelativePath = @path");
            AddParam(cmd, "@path", relativePath);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            return ReadFamily(reader);
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
            using var cmd = CreateCommand(@"
                INSERT INTO Families (RelativePath, FileName, ModifiedDate, FileSize)
                VALUES (@path, @name, @modified, @size)
                ON CONFLICT(RelativePath) DO UPDATE SET
                    FileName = excluded.FileName,
                    ModifiedDate = excluded.ModifiedDate,
                    FileSize = excluded.FileSize;");
            AddParam(cmd, "@path", relativePath);
            AddParam(cmd, "@name", fileName);
            AddParam(cmd, "@modified", modifiedDateUtc.ToString("o"));
            AddParam(cmd, "@size", fileSize);
            cmd.ExecuteNonQuery();
        }

        public void UpdateFamilyMetadata(long familyId, string? category, IReadOnlyList<ParameterModel> parameters, byte[]? thumbnailPng, int revitYear = 0,
            DateTime modifiedDate = default, long fileSize = 0)
        {
            using var transaction = _connection.BeginTransaction();
            try
            {
                // Write the file's real size/date HERE, in the same transaction as the extracted
                // metadata: a successful extraction is what marks the row current. (FamilyIndexer
                // inserts a sentinel size/date, so until this commits the family is re-scannable.)
                using var catCmd = CreateCommand("UPDATE Families SET Category=@cat, IndexedDate=@now, RevitYear=@year, ModifiedDate=@modified, FileSize=@size, ParametersExtracted=1 WHERE Id=@id", transaction);
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

                using var famCmd = CreateCommand(
                    "UPDATE Families SET RevitYear=@year, ModifiedDate=@modified, FileSize=@size, IndexedDate=@now WHERE Id=@id",
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
            var valid = new HashSet<string>(validRelativePaths);
            foreach (var path in GetAllRelativePaths())
            {
                if (!valid.Contains(path))
                {
                    // Look up Id before deleting so we can clean up the gallery folder.
                    long id = 0;
                    using (var lookup = CreateCommand("SELECT Id FROM Families WHERE RelativePath=@path"))
                    {
                        AddParam(lookup, "@path", path);
                        var scalar = lookup.ExecuteScalar();
                        if (scalar != null && scalar != DBNull.Value)
                            id = (long)scalar;
                    }

                    using var cmd = CreateCommand("DELETE FROM Families WHERE RelativePath=@path");
                    AddParam(cmd, "@path", path);
                    cmd.ExecuteNonQuery();

                    if (id != 0)
                    {
                        try
                        {
                            var folder = GalleryRoot(id);
                            if (System.IO.Directory.Exists(folder))
                                System.IO.Directory.Delete(folder, true);
                        }
                        catch { /* best-effort; never let a file-delete failure propagate */ }
                    }
                }
            }
        }

        public void ClearAll()
        {
            Execute("DELETE FROM Thumbnail; DELETE FROM Parameters; DELETE FROM Families;");

            try
            {
                var g = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_databasePath)!, "Gallery");
                if (System.IO.Directory.Exists(g))
                    System.IO.Directory.Delete(g, true);
            }
            catch { /* best-effort */ }
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
            IndexedDate  = DbConvert.ParseUtc(r.GetString(6))
        };

        private string GalleryRoot(long familyId) =>
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_databasePath)!, "Gallery", familyId.ToString());

        private void Execute(string sql)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        public void Dispose() => _connection.Dispose();
    }
}
