using System;
using System.IO;
using RVTuk.Core.FamilyBrowser.Database;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser;

// The everyday open of the browser against a current DB must be read-only (never take
// write locks on the shared file — backlog "Read Only DB"); the ctor may only fall back
// to a write open when there is real work to do: no DB yet, or a schema that's behind.
public class BrowserRepositorySchemaTests : IDisposable
{
    private readonly string _root;

    public BrowserRepositorySchemaTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rvtuk_schema_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private string DbPath(string name) => Path.Combine(_root, name);

    // Fresh install: no scan has run yet, the browser still opens (empty) and creates the DB.
    [Fact]
    public void MissingDbFile_CreatesSchema_AndOpensEmpty()
    {
        using var repo = new BrowserRepository(DbPath("new.db"));
        Assert.Empty(repo.GetAllFamilies());
    }

    // A DB from an older build (pre-Version/Tags/etc. columns) gets migrated on open when the
    // file is writable, so edits (tags, favourites) work immediately.
    [Fact]
    public void OutdatedSchema_WritableFile_MigratesOnOpen()
    {
        var dbPath = DbPath("old.db");
        CreateOldSchemaDb(dbPath);

        long id;
        using (var repo = new BrowserRepository(dbPath))
        {
            var item = Assert.Single(repo.GetAllFamilies());
            id = item.Id;
            repo.SaveTags(id, "legacy"); // requires the migrated Tags column
        }

        using (var repo = new BrowserRepository(dbPath))
            Assert.Equal("legacy", Assert.Single(repo.GetAllFamilies()).Tags);
    }

    // A current DB on a share where this user has no write access must open fully functional
    // for reading — including the Version column feeding the update check.
    [Fact]
    public void CurrentSchema_ReadOnlyFile_ReadsFullyFunctional()
    {
        var dbPath = DbPath("current.db");
        using (var index = new IndexRepository(dbPath))
        {
            long id = index.InsertFamily("Doors/A.rfa", "A.rfa");
            index.UpdateFamilyMetadata(id, "Doors",
                new System.Collections.Generic.List<RVTuk.Core.FamilyBrowser.Models.ParameterModel>(),
                null, revitYear: 0,
                modifiedDate: new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                fileSize: 10, familyVersion: "7");
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.SetAttributes(dbPath, FileAttributes.ReadOnly);
        try
        {
            using var repo = new BrowserRepository(dbPath);
            var item = Assert.Single(repo.GetAllFamilies());
            Assert.Equal("7", item.Version);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.SetAttributes(dbPath, FileAttributes.Normal);
        }
    }

    private static void CreateOldSchemaDb(string dbPath)
    {
        using var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE Families (
                Id INTEGER PRIMARY KEY,
                RelativePath TEXT UNIQUE NOT NULL,
                FileName TEXT NOT NULL,
                ModifiedDate DATETIME NOT NULL,
                FileSize INTEGER NOT NULL,
                Category TEXT,
                IndexedDate DATETIME DEFAULT CURRENT_TIMESTAMP
            );
            CREATE TABLE Parameters (
                Id INTEGER PRIMARY KEY,
                FamilyId INTEGER NOT NULL,
                ParameterName TEXT NOT NULL,
                DataType TEXT NOT NULL,
                IsInstance INTEGER NOT NULL,
                FOREIGN KEY (FamilyId) REFERENCES Families(Id) ON DELETE CASCADE
            );
            CREATE TABLE Thumbnail (Id INTEGER PRIMARY KEY, FamilyId INTEGER UNIQUE NOT NULL, PngData BLOB NOT NULL);
            INSERT INTO Families (RelativePath, FileName, ModifiedDate, FileSize)
            VALUES ('Doors/Old.rfa', 'Old.rfa', '2026-01-01T12:00:00.0000000Z', 5);";
        cmd.ExecuteNonQuery();
    }
}
