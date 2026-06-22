using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Reflection;
using ReviTchucky.Core.Models;

#if REVIT2024
using System.Data.SQLite;
using System.Runtime.InteropServices;
#else
using Microsoft.Data.Sqlite;
using SQLiteConnection = Microsoft.Data.Sqlite.SqliteConnection;
using SQLiteCommand   = Microsoft.Data.Sqlite.SqliteCommand;
#endif

namespace ReviTchucky.Core.Database
{
    public class BrowserRepository : IDisposable
    {
        private readonly SQLiteConnection _connection;

#if REVIT2024
        // System.Data.SQLite searches for SQLite.Interop.dll relative to Revit.exe, not our add-in folder.
        // Pre-load it by full path so Windows' module cache satisfies the subsequent name-only lookup.
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string dllPath);

        static BrowserRepository()
        {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (dir != null)
            {
                var interop = Path.Combine(dir, "SQLite.Interop.dll");
                if (File.Exists(interop))
                    LoadLibrary(interop);
            }
        }
#endif

        public BrowserRepository(string databasePath)
        {
#if REVIT2024
            _connection = new SQLiteConnection($"Data Source={databasePath};Version=3;");
#else
            _connection = new SQLiteConnection(
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate
                }.ToString());
#endif
            _connection.Open();
            Execute("PRAGMA journal_mode=WAL;");
            Execute("PRAGMA synchronous=NORMAL;");
            Execute("PRAGMA foreign_keys=ON;");
            EnsureSchema();
        }

        private void EnsureSchema()
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
                );
                CREATE TABLE IF NOT EXISTS CustomThumbnail (
                    Id       INTEGER PRIMARY KEY,
                    FamilyId INTEGER UNIQUE NOT NULL,
                    PngData  BLOB    NOT NULL,
                    OleSynced INTEGER NOT NULL DEFAULT 1,
                    FOREIGN KEY (FamilyId) REFERENCES Families(Id) ON DELETE CASCADE
                );");

            // Add InstructionsXaml column if missing (migration for older DBs)
            using var checkCmd = _connection.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Families') WHERE name='InstructionsXaml'";
            if ((long)(checkCmd.ExecuteScalar() ?? 0L) == 0)
                Execute("ALTER TABLE Families ADD COLUMN InstructionsXaml TEXT");
        }

        private void Execute(string sql)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
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
                       ct.OleSynced
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
            cmd.CommandText = "SELECT Id, ParameterName, DataType, IsInstance FROM Parameters WHERE FamilyId = @id ORDER BY ParameterName";
            AddParam(cmd, "@id", familyId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(new ParameterModel
                {
                    Id            = reader.GetInt64(0),
                    FamilyId      = familyId,
                    ParameterName = reader.GetString(1),
                    DataType      = reader.GetString(2),
                    IsInstance    = reader.GetInt32(3) == 1
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

        public void SaveInstructionsXaml(long familyId, string? xaml)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE Families SET InstructionsXaml = @xaml WHERE Id = @id";
            AddParam(cmd, "@xaml", (object?)xaml ?? DBNull.Value);
            AddParam(cmd, "@id", familyId);
            cmd.ExecuteNonQuery();
        }

        public void SaveCustomThumbnail(long familyId, byte[] pngData, bool oleSynced)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO CustomThumbnail (FamilyId, PngData, OleSynced)
                                VALUES (@fid, @png, @sync)
                                ON CONFLICT(FamilyId) DO UPDATE SET PngData=@png, OleSynced=@sync";
            AddParam(cmd, "@fid", familyId);
            AddParam(cmd, "@png", pngData);
            AddParam(cmd, "@sync", oleSynced ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

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

        public void UpsertFamily(string relativePath, string fileName, DateTime modifiedDateUtc, long fileSize)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Families (RelativePath, FileName, ModifiedDate, FileSize)
                VALUES (@rel, @name, @modified, @size)
                ON CONFLICT(RelativePath) DO UPDATE SET
                    FileName = excluded.FileName,
                    ModifiedDate = excluded.ModifiedDate,
                    FileSize = excluded.FileSize";
            AddParam(cmd, "@rel", relativePath);
            AddParam(cmd, "@name", fileName);
            AddParam(cmd, "@modified", modifiedDateUtc.ToString("o"));
            AddParam(cmd, "@size", fileSize);
            cmd.ExecuteNonQuery();
        }

        public void DeleteStaleEntries(IEnumerable<string> staleRelativePaths)
        {
            foreach (var path in staleRelativePaths)
            {
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = "DELETE FROM Families WHERE RelativePath = @path";
                AddParam(cmd, "@path", path);
                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteCustomThumbnail(long familyId)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM CustomThumbnail WHERE FamilyId = @id";
            AddParam(cmd, "@id", familyId);
            cmd.ExecuteNonQuery();
        }

        public void SetOleSynced(long familyId, bool synced)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE CustomThumbnail SET OleSynced = @sync WHERE FamilyId = @id";
            AddParam(cmd, "@sync", synced ? 1 : 0);
            AddParam(cmd, "@id", familyId);
            cmd.ExecuteNonQuery();
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
