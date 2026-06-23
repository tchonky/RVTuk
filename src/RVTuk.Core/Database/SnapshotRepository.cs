using System;
using System.Collections.Generic;
using System.Data;
using RVTuk.Core.Models.Comparison;

#if REVIT2024
using System.Data.SQLite;
#else
using Microsoft.Data.Sqlite;
using SQLiteConnection = Microsoft.Data.Sqlite.SqliteConnection;
using SQLiteCommand   = Microsoft.Data.Sqlite.SqliteCommand;
#endif

namespace RVTuk.Core.Database
{
    /// <summary>A serialized category payload ready to store. The caller serializes the concrete
    /// snapshot (via SnapshotJson) so this layer stays category-agnostic.</summary>
    public class CategoryPayload
    {
        public string CategoryId { get; set; } = string.Empty;
        public string PayloadJson { get; set; } = string.Empty;
        public int ItemCount { get; set; }
    }

    /// <summary>SQLite store for standards snapshots (and the Standard). Schema is stable across
    /// categories — each category's data is a JSON payload. Same conventions as IndexRepository:
    /// rollback journal (UNC-safe), busy_timeout, dates UTC ISO-8601 via DbConvert.</summary>
    public class SnapshotRepository : IDisposable
    {
        private readonly SQLiteConnection _connection;

        public SnapshotRepository(string databasePath)
        {
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

            Execute("PRAGMA busy_timeout=5000;");
            Execute("PRAGMA journal_mode=DELETE;");
            Execute("PRAGMA synchronous=NORMAL;");
            Execute("PRAGMA foreign_keys=ON;");
            EnsureSchema();
        }

        public void EnsureSchema()
        {
            Execute(@"
                CREATE TABLE IF NOT EXISTS Snapshot (
                    Id            INTEGER PRIMARY KEY,
                    SourceKind    TEXT NOT NULL,
                    SourceName    TEXT NOT NULL,
                    SourcePath    TEXT,
                    RevitYear     INTEGER NOT NULL,
                    CapturedUtc   TEXT NOT NULL,
                    SchemaVersion INTEGER NOT NULL,
                    IsMutable     INTEGER NOT NULL DEFAULT 0,
                    Revision      INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS SnapshotCategory (
                    Id          INTEGER PRIMARY KEY,
                    SnapshotId  INTEGER NOT NULL,
                    CategoryId  TEXT NOT NULL,
                    PayloadJson TEXT NOT NULL,
                    ItemCount   INTEGER NOT NULL,
                    FOREIGN KEY (SnapshotId) REFERENCES Snapshot(Id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS StandardChangeLog (
                    Id              INTEGER PRIMARY KEY,
                    SnapshotId      INTEGER NOT NULL,
                    CategoryId      TEXT NOT NULL,
                    ItemKey         TEXT NOT NULL,
                    Action          TEXT NOT NULL,
                    SourceSnapshotId INTEGER,
                    ProvenanceJson  TEXT,
                    AppliedUtc      TEXT NOT NULL
                );");
        }

        public long SaveSnapshot(SnapshotMeta meta, IEnumerable<CategoryPayload> categories)
        {
            using var tx = _connection.BeginTransaction();
            try
            {
                using (var ins = CreateCommand(@"
                    INSERT INTO Snapshot (SourceKind, SourceName, SourcePath, RevitYear, CapturedUtc, SchemaVersion, IsMutable, Revision)
                    VALUES (@k, @n, @p, @y, @c, @s, @m, @r);", tx))
                {
                    AddParam(ins, "@k", meta.SourceKind);
                    AddParam(ins, "@n", meta.SourceName);
                    AddParam(ins, "@p", (object?)meta.SourcePath ?? DBNull.Value);
                    AddParam(ins, "@y", meta.RevitYear);
                    AddParam(ins, "@c", meta.CapturedUtc);
                    AddParam(ins, "@s", meta.SchemaVersion);
                    AddParam(ins, "@m", meta.IsMutable ? 1 : 0);
                    AddParam(ins, "@r", meta.Revision);
                    ins.ExecuteNonQuery();
                }

                long id;
                using (var sel = CreateCommand("SELECT last_insert_rowid();", tx))
                    id = (long)(sel.ExecuteScalar() ?? 0L);

                foreach (var cat in categories)
                {
                    using var c = CreateCommand(@"
                        INSERT INTO SnapshotCategory (SnapshotId, CategoryId, PayloadJson, ItemCount)
                        VALUES (@sid, @cid, @pl, @ic);", tx);
                    AddParam(c, "@sid", id);
                    AddParam(c, "@cid", cat.CategoryId);
                    AddParam(c, "@pl", cat.PayloadJson);
                    AddParam(c, "@ic", cat.ItemCount);
                    c.ExecuteNonQuery();
                }

                tx.Commit();
                return id;
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public SnapshotMeta? GetMeta(long id)
        {
            using var cmd = CreateCommand(
                "SELECT Id, SourceKind, SourceName, SourcePath, RevitYear, CapturedUtc, SchemaVersion, IsMutable, Revision FROM Snapshot WHERE Id=@id");
            AddParam(cmd, "@id", id);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadMeta(r) : null;
        }

        public List<SnapshotMeta> ListSnapshots()
        {
            var list = new List<SnapshotMeta>();
            using var cmd = CreateCommand(
                "SELECT Id, SourceKind, SourceName, SourcePath, RevitYear, CapturedUtc, SchemaVersion, IsMutable, Revision FROM Snapshot ORDER BY CapturedUtc DESC");
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(ReadMeta(r));
            return list;
        }

        public List<CategorySnapshot> LoadCategories(long snapshotId, Func<string, string, CategorySnapshot> deserialize)
        {
            var list = new List<CategorySnapshot>();
            using var cmd = CreateCommand("SELECT CategoryId, PayloadJson FROM SnapshotCategory WHERE SnapshotId=@id");
            AddParam(cmd, "@id", snapshotId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(deserialize(r.GetString(0), r.GetString(1)));
            return list;
        }

        public void LogStandardChange(long standardId, string categoryId, string itemKey, string action, long? sourceSnapshotId, string? provenanceJson)
        {
            using var cmd = CreateCommand(@"
                INSERT INTO StandardChangeLog (SnapshotId, CategoryId, ItemKey, Action, SourceSnapshotId, ProvenanceJson, AppliedUtc)
                VALUES (@sid, @cid, @key, @act, @src, @prov, @now);");
            AddParam(cmd, "@sid", standardId);
            AddParam(cmd, "@cid", categoryId);
            AddParam(cmd, "@key", itemKey);
            AddParam(cmd, "@act", action);
            AddParam(cmd, "@src", (object?)sourceSnapshotId ?? DBNull.Value);
            AddParam(cmd, "@prov", (object?)provenanceJson ?? DBNull.Value);
            AddParam(cmd, "@now", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        public int CountStandardChanges(long standardId)
        {
            using var cmd = CreateCommand("SELECT COUNT(*) FROM StandardChangeLog WHERE SnapshotId=@id");
            AddParam(cmd, "@id", standardId);
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }

        private static SnapshotMeta ReadMeta(IDataReader r) => new SnapshotMeta
        {
            SourceKind    = r.GetString(1),
            SourceName    = r.GetString(2),
            SourcePath    = r.IsDBNull(3) ? null : r.GetString(3),
            RevitYear     = Convert.ToInt32(r.GetValue(4)),
            CapturedUtc   = r.GetString(5),
            SchemaVersion = Convert.ToInt32(r.GetValue(6)),
            IsMutable     = Convert.ToInt32(r.GetValue(7)) != 0,
            Revision      = Convert.ToInt32(r.GetValue(8)),
        };

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

        private void Execute(string sql)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        public void Dispose() => _connection.Dispose();
    }
}
