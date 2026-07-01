using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using RVTuk.Core.Models;
using Microsoft.Data.Sqlite;
using SQLiteConnection = Microsoft.Data.Sqlite.SqliteConnection;
using SQLiteCommand   = Microsoft.Data.Sqlite.SqliteCommand;

namespace RVTuk.Core.Database
{
    public class BrowserRepository : IDisposable
    {
        private readonly string _databasePath;
        private readonly SQLiteConnection _connection; // persistent READ-ONLY connection

        public BrowserRepository(string databasePath)
        {
            SqliteNative.EnsureLoaded();
            _databasePath = databasePath;

            // One-time: ensure the file + schema exist and the journal is migrated off WAL.
            // Best-effort — if the share is read-only for this user, assume the admin already
            // created/migrated the DB and fall through to the read-only connection.
            try
            {
                using var init = OpenWrite();
                EnsureSchema(init);
            }
            catch { /* read-only share or locked; admin DB assumed ready */ }

            _connection = OpenRead();
            ExecuteOn(_connection, "PRAGMA busy_timeout=5000;");
            ExecuteOn(_connection, "PRAGMA foreign_keys=ON;");
        }

        // Persistent read-only connection for all Get* methods.
        private SQLiteConnection OpenRead()
        {
            var c = new SQLiteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString());
            c.Open();
            return c;
        }

        // Short-lived read-write connection for the occasional admin/edit write.
        private SQLiteConnection OpenWrite()
        {
            var c = new SQLiteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate
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
                );
                CREATE TABLE IF NOT EXISTS FamilyImage (
                    Id        INTEGER PRIMARY KEY,
                    FamilyId  INTEGER NOT NULL,
                    FileName  TEXT    NOT NULL,
                    Caption   TEXT,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
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
        }

        // Returns all families with thumbnail resolved (CustomThumbnail ?? OLE Thumbnail)
        public List<FamilyBrowserItem> GetAllFamilies()
        {
            var result = new List<FamilyBrowserItem>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                SELECT f.Id, f.FileName, f.RelativePath, f.Category, f.ModifiedDate,
                       t.PngData  AS OlePng,
                       ct.PngData AS CustomPng,
                       ct.OleSynced,
                       f.RevitYear,
                       f.Tags,
                       f.IsFavorite
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
                });
            }
            return result;
        }

        public List<string?> GetCategories()
        {
            var cats = new List<string?> { null }; // null = "All"
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT DISTINCT Category FROM Families WHERE Category IS NOT NULL ORDER BY Category";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                cats.Add(reader.GetString(0));
            return cats;
        }

        public string? GetInstructionsXaml(long familyId)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT InstructionsXaml FROM Families WHERE Id = @id";
            AddParam(cmd, "@id", familyId);
            var result = cmd.ExecuteScalar();
            return result == DBNull.Value || result == null ? null : (string)result;
        }

        public List<ParameterModel> GetParameters(long familyId)
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
        }

        public (byte[]? Png, bool OleSynced) GetCustomThumbnail(long familyId)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT PngData, OleSynced FROM CustomThumbnail WHERE FamilyId = @id";
            AddParam(cmd, "@id", familyId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return (null, true);
            return ((byte[])reader[0], reader.GetInt32(1) == 1);
        }

        public byte[]? GetOleThumbnail(long familyId)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT PngData FROM Thumbnail WHERE FamilyId = @id";
            AddParam(cmd, "@id", familyId);
            var result = cmd.ExecuteScalar();
            return result == DBNull.Value || result == null ? null : (byte[])result;
        }

        // Same resolution as GetAllFamilies: custom thumbnail wins over the OLE one.
        // Used to refresh a single family's preview after a one-off rescan.
        public byte[]? GetResolvedThumbnail(long familyId)
            => GetCustomThumbnail(familyId).Png ?? GetOleThumbnail(familyId);

        public List<string> GetAllRelativePaths()
        {
            var result = new List<string>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT RelativePath FROM Families";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(reader.GetString(0));
            return result;
        }

        public string? GetTags(long familyId)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Tags FROM Families WHERE Id = @id";
            AddParam(cmd, "@id", familyId);
            var result = cmd.ExecuteScalar();
            return result == DBNull.Value || result == null ? null : (string)result;
        }

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

        // NOTE: The fast Sync path intentionally writes the file's real size/date with no extracted
        // metadata. Consequently a Sync-marked family then "looks up to date" to the deep scan
        // (its stored size/date matches the file) and is skipped — so its parameters/category are
        // never extracted. That is a separate, known issue (whether Sync should mark rows "needs
        // deep scan") tracked in docs/BACKLOG.md and is OUT OF SCOPE for the resumable-deep-scan
        // change. Deliberately left unchanged here.
        public void UpsertFamily(string relativePath, string fileName, DateTime modifiedDateUtc, long fileSize)
        {
            WithWrite(c =>
            {
                // Resolve the row case-insensitively (preferring an exact-case match): Windows
                // paths are case-insensitive but the RelativePath key is not, so a case-only
                // rename on disk must update the existing row — a case-sensitive upsert would
                // insert a duplicate that then lingers next to the old row forever. Updating by
                // Id also re-keys the row to the current on-disk casing.
                long id = 0;
                using (var lookup = c.CreateCommand())
                {
                    lookup.CommandText =
                        "SELECT Id FROM Families WHERE RelativePath = @rel COLLATE NOCASE " +
                        "ORDER BY (CASE WHEN RelativePath = @rel THEN 0 ELSE 1 END) LIMIT 1";
                    AddParam(lookup, "@rel", relativePath);
                    var scalar = lookup.ExecuteScalar();
                    if (scalar != null && scalar != DBNull.Value)
                        id = (long)scalar;
                }

                using var cmd = c.CreateCommand();
                if (id != 0)
                {
                    cmd.CommandText = @"
                        UPDATE Families SET
                            RelativePath = @rel,
                            FileName = @name,
                            ModifiedDate = @modified,
                            FileSize = @size
                        WHERE Id = @id";
                    AddParam(cmd, "@id", id);
                }
                else
                {
                    // ON CONFLICT kept for safety under concurrent writers on the shared DB.
                    cmd.CommandText = @"
                        INSERT INTO Families (RelativePath, FileName, ModifiedDate, FileSize)
                        VALUES (@rel, @name, @modified, @size)
                        ON CONFLICT(RelativePath) DO UPDATE SET
                            FileName = excluded.FileName,
                            ModifiedDate = excluded.ModifiedDate,
                            FileSize = excluded.FileSize";
                }
                AddParam(cmd, "@rel", relativePath);
                AddParam(cmd, "@name", fileName);
                AddParam(cmd, "@modified", modifiedDateUtc.ToString("o"));
                AddParam(cmd, "@size", fileSize);
                cmd.ExecuteNonQuery();
            });
        }

        public void DeleteStaleEntries(IEnumerable<string> staleRelativePaths)
        {
            foreach (var path in staleRelativePaths)
            {
                // Look up the family Id (RO connection) before deleting the row so we can
                // remove its gallery folder even after the row is gone.
                long id = 0;
                using (var lookup = _connection.CreateCommand())
                {
                    lookup.CommandText = "SELECT Id FROM Families WHERE RelativePath = @path";
                    AddParam(lookup, "@path", path);
                    var scalar = lookup.ExecuteScalar();
                    if (scalar != null && scalar != DBNull.Value)
                        id = (long)scalar;
                }

                WithWrite(c =>
                {
                    using var cmd = c.CreateCommand();
                    cmd.CommandText = "DELETE FROM Families WHERE RelativePath = @path";
                    AddParam(cmd, "@path", path);
                    cmd.ExecuteNonQuery();
                });

                if (id != 0)
                {
                    try
                    {
                        var folder = GalleryRoot(id);
                        if (Directory.Exists(folder))
                            Directory.Delete(folder, true);
                    }
                    catch { /* best-effort; never let a file-delete failure propagate */ }
                }
            }
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

        // ── Gallery ──────────────────────────────────────────────────────────

        private string GalleryRoot(long familyId)
        {
            var setupDir = System.IO.Path.GetDirectoryName(_databasePath)!; // the .Setup folder
            return System.IO.Path.Combine(setupDir, "Gallery", familyId.ToString());
        }

        public string GetGalleryPath(long familyId, string fileName)
            => System.IO.Path.Combine(GalleryRoot(familyId), fileName);

        public List<FamilyImage> GetImages(long familyId)
        {
            var result = new List<FamilyImage>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT Id, FileName, Caption, SortOrder FROM FamilyImage " +
                              "WHERE FamilyId=@id ORDER BY SortOrder, Id";
            AddParam(cmd, "@id", familyId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(new FamilyImage
                {
                    Id        = reader.GetInt64(0),
                    FamilyId  = familyId,
                    FileName  = reader.GetString(1),
                    Caption   = reader.IsDBNull(2) ? null : reader.GetString(2),
                    SortOrder = reader.GetInt32(3),
                });
            return result;
        }

        public FamilyImage AddImage(long familyId, byte[] pngData, string? caption)
        {
            var dir = GalleryRoot(familyId);
            System.IO.Directory.CreateDirectory(dir);
            var fileName = $"{Guid.NewGuid():N}.png";
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, fileName), pngData);

            long newId = 0;
            int sort = 0;
            WithWrite(c =>
            {
                using (var max = c.CreateCommand())
                {
                    max.CommandText = "SELECT COALESCE(MAX(SortOrder)+1, 0) FROM FamilyImage WHERE FamilyId=@id";
                    AddParam(max, "@id", familyId);
                    sort = Convert.ToInt32(max.ExecuteScalar() ?? 0);
                }
                using (var ins = c.CreateCommand())
                {
                    ins.CommandText = "INSERT INTO FamilyImage (FamilyId, FileName, Caption, SortOrder) " +
                                      "VALUES (@fid, @fn, @cap, @sort)";
                    AddParam(ins, "@fid", familyId);
                    AddParam(ins, "@fn", fileName);
                    AddParam(ins, "@cap", (object?)caption ?? DBNull.Value);
                    AddParam(ins, "@sort", sort);
                    ins.ExecuteNonQuery();
                }
                using (var sel = c.CreateCommand())
                {
                    sel.CommandText = "SELECT Id FROM FamilyImage WHERE FamilyId=@fid AND FileName=@fn";
                    AddParam(sel, "@fid", familyId);
                    AddParam(sel, "@fn", fileName);
                    newId = (long)(sel.ExecuteScalar() ?? 0L);
                }
            });
            return new FamilyImage { Id = newId, FamilyId = familyId, FileName = fileName, Caption = caption, SortOrder = sort };
        }

        public void UpdateCaption(long imageId, string? caption)
        {
            WithWrite(c =>
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "UPDATE FamilyImage SET Caption=@cap WHERE Id=@id";
                AddParam(cmd, "@cap", (object?)caption ?? DBNull.Value);
                AddParam(cmd, "@id", imageId);
                cmd.ExecuteNonQuery();
            });
        }

        public void DeleteImage(long imageId)
        {
            // Look up file (RO connection) before deleting the row.
            long familyId = 0; string? fileName = null;
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "SELECT FamilyId, FileName FROM FamilyImage WHERE Id=@id";
                AddParam(cmd, "@id", imageId);
                using var r = cmd.ExecuteReader();
                if (r.Read()) { familyId = r.GetInt64(0); fileName = r.GetString(1); }
            }
            WithWrite(c =>
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "DELETE FROM FamilyImage WHERE Id=@id";
                AddParam(cmd, "@id", imageId);
                cmd.ExecuteNonQuery();
            });
            if (fileName != null)
            {
                try { System.IO.File.Delete(System.IO.Path.Combine(GalleryRoot(familyId), fileName)); } catch { }
            }
        }

        public void ReorderImages(long familyId, IReadOnlyList<long> orderedImageIds)
        {
            WithWrite(c =>
            {
                for (int i = 0; i < orderedImageIds.Count; i++)
                {
                    using var cmd = c.CreateCommand();
                    cmd.CommandText = "UPDATE FamilyImage SET SortOrder=@s WHERE Id=@id AND FamilyId=@fid";
                    AddParam(cmd, "@s", i);
                    AddParam(cmd, "@id", orderedImageIds[i]);
                    AddParam(cmd, "@fid", familyId);
                    cmd.ExecuteNonQuery();
                }
            });
        }

        public void Dispose() => _connection.Dispose();
    }
}
