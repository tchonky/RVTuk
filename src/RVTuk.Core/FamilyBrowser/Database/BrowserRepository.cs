using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
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
                    IndexRepository.EnsureSchema(init);
                }
                catch { /* read-only share or locked */ }
            }

            _connection = OpenRead();

            // A throw below (locked/corrupt DB) never reaches Dispose: close here.
            try
            {
                ExecuteOn(_connection, "PRAGMA busy_timeout=5000;");
                ExecuteOn(_connection, "PRAGMA foreign_keys=ON;");

                // Migrate only when the schema is actually behind. The everyday open of a current
                // DB must never take a write handle on the shared file — one user's Revit holding
                // write locks is what blocked the whole office (backlog "Read Only DB").
                if (!IndexRepository.SchemaIsCurrent(_connection))
                {
                    try
                    {
                        using var init = OpenWrite();
                        IndexRepository.EnsureSchema(init);
                    }
                    catch { /* read-only share or locked; reads degrade via _hasVersionColumns */ }
                }

                _hasVersionColumns = IndexRepository.ColumnExists(_connection, "Families", "Version")
                                  && IndexRepository.ColumnExists(_connection, "Families", "ParametersExtracted");
                _hasParameterInfo  = IndexRepository.ColumnExists(_connection, "Parameters", "IsInstance");
            }
            catch
            {
                _connection.Dispose();
                throw;
            }
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
            try
            {
                c.Open();
                ExecuteOn(c, "PRAGMA busy_timeout=5000;");
                ExecuteOn(c, "PRAGMA journal_mode=DELETE;");
                ExecuteOn(c, "PRAGMA synchronous=NORMAL;");
                ExecuteOn(c, "PRAGMA foreign_keys=ON;");
                return c;
            }
            catch
            {
                c.Dispose(); // a locked DB must not leave the file open until GC
                throw;
            }
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

        // Returns all families with thumbnail resolved (CustomThumbnail ?? OLE Thumbnail)
        public List<FamilyBrowserItem> GetAllFamilies() => ReadFamilies(null);

        // One family, read exactly like GetAllFamilies; null when the id doesn't exist.
        public FamilyBrowserItem? GetFamily(long familyId) => ReadFamilies(familyId).FirstOrDefault();

        private List<FamilyBrowserItem> ReadFamilies(long? familyId) => WithRead(() =>
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
                {(familyId == null ? "" : "WHERE f.Id = @id")}
                ORDER BY f.FileName";
            if (familyId != null) AddParam(cmd, "@id", familyId.Value);
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

        // Under the gate: a pool-thread read may still be running on this connection.
        public void Dispose() { lock (_readGate) _connection.Dispose(); }
    }
}
