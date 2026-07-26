using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using RVTuk.Core.FamilyBrowser.Models;
using RVTuk.Core.FamilyBrowser.Util;
using Microsoft.Data.Sqlite;
using SQLiteConnection = Microsoft.Data.Sqlite.SqliteConnection;
using SQLiteCommand   = Microsoft.Data.Sqlite.SqliteCommand;

namespace RVTuk.Core.FamilyBrowser.Database
{
    public class BrowserRepository : IDisposable
    {
        private readonly string _databasePath;
        private readonly SQLiteConnection _connection; // persistent READ-ONLY connection

        // False when the DB predates the Version/ParametersExtracted columns AND the best-effort
        // migration below couldn't run (read-only share, DB locked). Reads then surface a null
        // Version — the version check is simply off until a writable open (here or a deep scan)
        // migrates the schema.
        private readonly bool _hasVersionColumns;

        // Same degradation story for the Parameters table (it has carried IsInstance since the
        // base schema, but a foreign pre-RVTuk DB may lack the table entirely): without it the
        // instance-version flag simply stays false.
        private readonly bool _hasParameterInfo;

        // Serialises use of the shared read connection: the browser reads from several
        // ThreadPool threads at once (Sync, per-selection detail loads, rescan thumbnail
        // refresh) and a SqliteConnection must not run concurrent commands.
        private readonly object _readGate = new object();

        public BrowserRepository(string databasePath)
        {
            SqliteNative.EnsureLoaded();
            _databasePath = databasePath;

            // Fresh install (no scan has run yet): create the DB + schema so the browser can
            // open, empty. Best-effort — if the share is read-only for this user and there is
            // truly no DB, OpenRead below throws and the browser surfaces that.
            if (!File.Exists(databasePath))
            {
                try
                {
                    using var init = OpenWrite();
                    EnsureSchema(init);
                }
                catch { /* read-only share or locked */ }
            }

            _connection = OpenRead();
            ExecuteOn(_connection, "PRAGMA busy_timeout=5000;");
            ExecuteOn(_connection, "PRAGMA foreign_keys=ON;");

            // Migrate only when the schema is actually behind. The everyday open of a current
            // DB must never take a write handle on the shared file — one user's Revit holding
            // write locks is what blocked the whole office (backlog "Read Only DB").
            if (!SchemaIsCurrent(_connection))
            {
                try
                {
                    using var init = OpenWrite();
                    EnsureSchema(init);
                }
                catch { /* read-only share or locked; reads degrade via _hasVersionColumns */ }
            }

            _hasVersionColumns = ColumnExists(_connection, "Families", "Version")
                              && ColumnExists(_connection, "Families", "ParametersExtracted");
            _hasParameterInfo  = ColumnExists(_connection, "Parameters", "IsInstance");
        }

        // One probe per table (its newest migrated column), so a DB that predates any of the
        // ALTERs — or lacks a table outright — reports "behind" and triggers the write-open
        // migration above. Mirrors what EnsureSchema creates; keep the two in sync.
        private static bool SchemaIsCurrent(SQLiteConnection c) =>
            ColumnExists(c, "Families", "ParametersExtracted")
            && ColumnExists(c, "Families", "Version")
            && ColumnExists(c, "Families", "IsFavorite")
            && ColumnExists(c, "Families", "Tags")
            && ColumnExists(c, "Families", "RevitYear")
            && ColumnExists(c, "Families", "InstructionsXaml")
            && ColumnExists(c, "Parameters", "Formula")
            && ColumnExists(c, "Parameters", "Guid")
            && ColumnExists(c, "Parameters", "Kind")
            && ColumnExists(c, "Parameters", "ParamGroup")
            && ColumnExists(c, "Thumbnail", "PngData")
            && ColumnExists(c, "CustomThumbnail", "OleSynced");

        private static bool ColumnExists(SQLiteConnection c, string table, string column)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'";
            return (long)(cmd.ExecuteScalar() ?? 0L) > 0;
        }

        // Persistent read-only connection for all Get* methods. Pooling off: pooled
        // connections keep the file handle open after Dispose, and a handle lingering for a
        // whole Revit session is exactly what blocked other users on the shared DB.
        private SQLiteConnection OpenRead()
        {
            var c = new SQLiteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            c.Open();
            return c;
        }

        // Short-lived read-write connection for the occasional admin/edit write. Pooling off
        // so Dispose really releases the write handle (see OpenRead).
        private SQLiteConnection OpenWrite()
        {
            var c = new SQLiteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString());
            c.Open();
            ExecuteOn(c, "PRAGMA busy_timeout=5000;");
            ExecuteOn(c, "PRAGMA journal_mode=DELETE;");
            ExecuteOn(c, "PRAGMA synchronous=NORMAL;");
            ExecuteOn(c, "PRAGMA foreign_keys=ON;");
            return c;
        }

        // Runs a write action against a fresh read-write connection, then closes it.
        private void WithWrite(Action<SQLiteConnection> action)
        {
            using var c = OpenWrite();
            action(c);
        }

        // Runs a read against the shared read-only connection under the gate.
        private T WithRead<T>(Func<T> read)
        {
            lock (_readGate) return read();
        }

        private static void ExecuteOn(SQLiteConnection c, string sql)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private void EnsureSchema(SQLiteConnection c)
        {
            ExecuteOn(c, @"
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
                );
                CREATE TABLE IF NOT EXISTS CustomThumbnail (
                    Id       INTEGER PRIMARY KEY,
                    FamilyId INTEGER UNIQUE NOT NULL,
                    PngData  BLOB    NOT NULL,
                    OleSynced INTEGER NOT NULL DEFAULT 1,
                    FOREIGN KEY (FamilyId) REFERENCES Families(Id) ON DELETE CASCADE
                );");

            using var checkCmd = c.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Families') WHERE name='InstructionsXaml'";
            if ((long)(checkCmd.ExecuteScalar() ?? 0L) == 0)
                ExecuteOn(c, "ALTER TABLE Families ADD COLUMN InstructionsXaml TEXT");

            foreach (var col in new[] { "ParamGroup", "Kind", "Guid", "Formula" })
            {
                using var pc = c.CreateCommand();
                pc.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('Parameters') WHERE name='{col}'";
                if ((long)(pc.ExecuteScalar() ?? 0L) == 0)
                    ExecuteOn(c, $"ALTER TABLE Parameters ADD COLUMN {col} TEXT");
            }

            // Add RevitYear column to Families if missing
            using var yearCheck = c.CreateCommand();
            yearCheck.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Families') WHERE name='RevitYear'";
            if ((long)(yearCheck.ExecuteScalar() ?? 0L) == 0)
                ExecuteOn(c, "ALTER TABLE Families ADD COLUMN RevitYear INTEGER NOT NULL DEFAULT 0");

            using var tagsCheck = c.CreateCommand();
            tagsCheck.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Families') WHERE name='Tags'";
            if ((long)(tagsCheck.ExecuteScalar() ?? 0L) == 0)
                ExecuteOn(c, "ALTER TABLE Families ADD COLUMN Tags TEXT");

            using var favCheck = c.CreateCommand();
            favCheck.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Families') WHERE name='IsFavorite'";
            if ((long)(favCheck.ExecuteScalar() ?? 0L) == 0)
                ExecuteOn(c, "ALTER TABLE Families ADD COLUMN IsFavorite INTEGER NOT NULL DEFAULT 0");

            using var versionCheck = c.CreateCommand();
            versionCheck.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Families') WHERE name='Version'";
            if ((long)(versionCheck.ExecuteScalar() ?? 0L) == 0)
                ExecuteOn(c, "ALTER TABLE Families ADD COLUMN Version TEXT");

            // Owned by the deep scan (IndexRepository) but probed by _hasVersionColumns, so the
            // browser must be able to create it on a DB no deep scan has touched yet.
            using var paramsExtractedCheck = c.CreateCommand();
            paramsExtractedCheck.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Families') WHERE name='ParametersExtracted'";
            if ((long)(paramsExtractedCheck.ExecuteScalar() ?? 0L) == 0)
                ExecuteOn(c, "ALTER TABLE Families ADD COLUMN ParametersExtracted INTEGER NOT NULL DEFAULT 0");
        }

        // Returns all families with thumbnail resolved (CustomThumbnail ?? OLE Thumbnail)
        public List<FamilyBrowserItem> GetAllFamilies() => WithRead(() =>
        {
            var result = new List<FamilyBrowserItem>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $@"
                SELECT f.Id, f.FileName, f.RelativePath, f.Category, f.ModifiedDate,
                       t.PngData  AS OlePng,
                       ct.PngData AS CustomPng,
                       ct.OleSynced,
                       f.RevitYear,
                       f.Tags,
                       f.IsFavorite,
                       {(_hasVersionColumns ? "f.Version" : "NULL")},
                       {(_hasParameterInfo
                           ? $"EXISTS(SELECT 1 FROM Parameters p WHERE p.FamilyId = f.Id AND p.ParameterName = '{FamilyVersionCheck.ParameterName}' COLLATE NOCASE AND p.IsInstance = 1)"
                           : "0")}
                FROM Families f
                LEFT JOIN Thumbnail t ON t.FamilyId = f.Id
                LEFT JOIN CustomThumbnail ct ON ct.FamilyId = f.Id
                ORDER BY f.FileName";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var hasCustom = !reader.IsDBNull(6);
                result.Add(new FamilyBrowserItem
                {
                    Id               = reader.GetInt64(0),
                    FileName         = reader.GetString(1),
                    RelativePath     = reader.GetString(2),
                    Category         = reader.IsDBNull(3) ? null : reader.GetString(3),
                    ModifiedDate     = DbConvert.ParseUtc(reader.GetString(4)),
                    ThumbnailPng     = hasCustom ? (byte[])reader[6] : (reader.IsDBNull(5) ? null : (byte[])reader[5]),
                    HasCustomThumbnail = hasCustom,
                    OleSynced        = !hasCustom || reader.GetInt32(7) == 1,
                    RevitYear        = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                    Tags             = reader.IsDBNull(9) ? null : reader.GetString(9),
                    IsFavorite       = !reader.IsDBNull(10) && reader.GetInt32(10) == 1,
                    Version          = reader.IsDBNull(11) ? null : reader.GetString(11),
                    VersionIsInstance = !reader.IsDBNull(12) && reader.GetInt32(12) == 1,
                });
            }
            return result;
        });

        public List<string?> GetCategories() => WithRead(() =>
        {
            var cats = new List<string?> { null }; // null = "All"
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT Category FROM Families WHERE Category IS NOT NULL ORDER BY Category";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                cats.Add(reader.GetString(0));
            return cats;
        });

        public string? GetInstructionsXaml(long familyId) => WithRead(() =>
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT InstructionsXaml FROM Families WHERE Id = @id";
            AddParam(cmd, "@id", familyId);
            var result = cmd.ExecuteScalar();
            return result == DBNull.Value || result == null ? null : (string)result;
        });

        public List<ParameterModel> GetParameters(long familyId) => WithRead(() =>
        {
            var result = new List<ParameterModel>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Id, ParameterName, DataType, IsInstance, ParamGroup, Kind, Guid, Formula " +
                              "FROM Parameters WHERE FamilyId = @id ORDER BY ParamGroup, ParameterName";
            AddParam(cmd, "@id", familyId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(new ParameterModel
                {
                    Id            = reader.GetInt64(0),
                    FamilyId      = familyId,
                    ParameterName = reader.GetString(1),
                    DataType      = reader.GetString(2),
                    IsInstance    = reader.GetInt32(3) == 1,
                    ParamGroup    = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Kind          = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Guid          = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Formula       = reader.IsDBNull(7) ? null : reader.GetString(7),
                });
            return result;
        });

        public (byte[]? Png, bool OleSynced) GetCustomThumbnail(long familyId) => WithRead(() =>
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT PngData, OleSynced FROM CustomThumbnail WHERE FamilyId = @id";
            AddParam(cmd, "@id", familyId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return ((byte[]?)null, true);
            return ((byte[]?)reader[0], reader.GetInt32(1) == 1);
        });

        public byte[]? GetOleThumbnail(long familyId) => WithRead(() =>
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT PngData FROM Thumbnail WHERE FamilyId = @id";
            AddParam(cmd, "@id", familyId);
            var result = cmd.ExecuteScalar();
            return result == DBNull.Value || result == null ? null : (byte[])result;
        });

        // Same resolution as GetAllFamilies: custom thumbnail wins over the OLE one.
        // Used to refresh a single family's preview after a one-off rescan.
        public byte[]? GetResolvedThumbnail(long familyId)
            => GetCustomThumbnail(familyId).Png ?? GetOleThumbnail(familyId);

        public string? GetTags(long familyId) => WithRead(() =>
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Tags FROM Families WHERE Id = @id";
            AddParam(cmd, "@id", familyId);
            var result = cmd.ExecuteScalar();
            return result == DBNull.Value || result == null ? null : (string)result;
        });

        public void SaveTags(long familyId, string? tags)
        {
            WithWrite(c =>
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "UPDATE Families SET Tags = @tags WHERE Id = @id";
                AddParam(cmd, "@tags", (object?)tags ?? DBNull.Value);
                AddParam(cmd, "@id", familyId);
                cmd.ExecuteNonQuery();
            });
        }

        public void SetFavorite(long familyId, bool isFavorite)
        {
            WithWrite(c =>
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "UPDATE Families SET IsFavorite = @fav WHERE Id = @id";
                AddParam(cmd, "@fav", isFavorite ? 1 : 0);
                AddParam(cmd, "@id", familyId);
                cmd.ExecuteNonQuery();
            });
        }

        public void SaveInstructionsXaml(long familyId, string? xaml)
        {
            WithWrite(c =>
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "UPDATE Families SET InstructionsXaml = @xaml WHERE Id = @id";
                AddParam(cmd, "@xaml", (object?)xaml ?? DBNull.Value);
                AddParam(cmd, "@id", familyId);
                cmd.ExecuteNonQuery();
            });
        }

        public void SaveCustomThumbnail(long familyId, byte[] pngData, bool oleSynced)
        {
            WithWrite(c =>
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = @"INSERT INTO CustomThumbnail (FamilyId, PngData, OleSynced)
                                VALUES (@fid, @png, @sync)
                                ON CONFLICT(FamilyId) DO UPDATE SET PngData=@png, OleSynced=@sync";
                AddParam(cmd, "@fid", familyId);
                AddParam(cmd, "@png", pngData);
                AddParam(cmd, "@sync", oleSynced ? 1 : 0);
                cmd.ExecuteNonQuery();
            });
        }

        public void DeleteCustomThumbnail(long familyId)
        {
            WithWrite(c =>
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "DELETE FROM CustomThumbnail WHERE FamilyId = @id";
                AddParam(cmd, "@id", familyId);
                cmd.ExecuteNonQuery();
            });
        }

        public void SetOleSynced(long familyId, bool synced)
        {
            WithWrite(c =>
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "UPDATE CustomThumbnail SET OleSynced = @sync WHERE FamilyId = @id";
                AddParam(cmd, "@sync", synced ? 1 : 0);
                AddParam(cmd, "@id", familyId);
                cmd.ExecuteNonQuery();
            });
        }

        private static void AddParam(IDbCommand cmd, string name, object? value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }

        public void Dispose() => _connection.Dispose();
    }
}
