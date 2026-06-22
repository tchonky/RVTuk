using System;
using System.Collections.Generic;
using System.Data;
using ReviTchucky.Core.Models;

#if REVIT2024
using System.Data.SQLite;
#else
using Microsoft.Data.Sqlite;
using SQLiteConnection = Microsoft.Data.Sqlite.SqliteConnection;
using SQLiteCommand   = Microsoft.Data.Sqlite.SqliteCommand;
using SQLiteParameter = Microsoft.Data.Sqlite.SqliteParameter;
#endif

namespace ReviTchucky.Core.Database
{
    public class IndexRepository : IDisposable
    {
        private readonly SQLiteConnection _connection;

        public IndexRepository(string databasePath)
        {
            if (System.IO.Directory.Exists(databasePath))
                throw new ArgumentException(
                    $"IndexDatabasePath points to a directory, not a file: \"{databasePath}\". " +
                    "Open Settings and set the path to a .db file.");

            // Create parent directory if needed (SQLite won't create it automatically)
            var dir = System.IO.Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(dir))
                System.IO.Directory.CreateDirectory(dir);

#if REVIT2024
            var connectionString = $"Data Source={databasePath};Version=3;";
#else
            var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate
            }.ToString();
#endif
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

            foreach (var col in new[] { "ParamGroup", "Kind", "Guid", "Formula" })
            {
                using var c = _connection.CreateCommand();
                c.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('Parameters') WHERE name='{col}'";
                if ((long)(c.ExecuteScalar() ?? 0L) == 0)
                    Execute($"ALTER TABLE Parameters ADD COLUMN {col} TEXT");
            }
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

        public long InsertFamily(string relativePath, string fileName, DateTime modifiedDate, long fileSize)
        {
            // Two single statements rather than INSERT…;SELECT in one ExecuteScalar:
            // Microsoft.Data.Sqlite (Revit 2025 / net8) only returns the first result set
            // from a multi-statement command, so the trailing SELECT's Id would be lost.
            // RelativePath is UNIQUE, so the follow-up SELECT resolves the just-written row.
            using (var insert = CreateCommand(@"
                INSERT INTO Families (RelativePath, FileName, ModifiedDate, FileSize)
                VALUES (@path, @name, @modified, @size)
                ON CONFLICT(RelativePath) DO UPDATE SET FileName=@name, ModifiedDate=@modified, FileSize=@size;"))
            {
                AddParam(insert, "@path", relativePath);
                AddParam(insert, "@name", fileName);
                AddParam(insert, "@modified", modifiedDate.ToString("o"));
                AddParam(insert, "@size", fileSize);
                insert.ExecuteNonQuery();
            }

            using (var select = CreateCommand("SELECT Id FROM Families WHERE RelativePath = @path;"))
            {
                AddParam(select, "@path", relativePath);
                return (long)(select.ExecuteScalar() ?? throw new InvalidOperationException("Insert failed"));
            }
        }

        public void UpdateFamilyMetadata(long familyId, string? category, IReadOnlyList<ParameterModel> parameters, byte[]? thumbnailPng)
        {
            using var transaction = _connection.BeginTransaction();
            try
            {
                using var catCmd = CreateCommand("UPDATE Families SET Category=@cat, IndexedDate=@now WHERE Id=@id", transaction);
                AddParam(catCmd, "@cat", category ?? (object)DBNull.Value);
                AddParam(catCmd, "@now", DateTime.UtcNow.ToString("o"));
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

        public void DeleteStaleEntries(IEnumerable<string> validRelativePaths)
        {
            var valid = new HashSet<string>(validRelativePaths);
            foreach (var path in GetAllRelativePaths())
            {
                if (!valid.Contains(path))
                {
                    using var cmd = CreateCommand("DELETE FROM Families WHERE RelativePath=@path");
                    AddParam(cmd, "@path", path);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void ClearAll()
        {
            Execute("DELETE FROM Thumbnail; DELETE FROM Parameters; DELETE FROM Families;");
        }

        private SQLiteCommand CreateCommand(string sql, IDbTransaction? transaction = null)
        {
            var cmd = (SQLiteCommand)_connection.CreateCommand();
            cmd.CommandText = sql;
            if (transaction != null)
            {
#if REVIT2024
                cmd.Transaction = (System.Data.SQLite.SQLiteTransaction)transaction;
#else
                cmd.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
#endif
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

        private void Execute(string sql)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        public void Dispose() => _connection.Dispose();
    }
}
