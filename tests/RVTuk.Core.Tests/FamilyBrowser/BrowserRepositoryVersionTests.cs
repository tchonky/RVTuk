using System;
using System.Collections.Generic;
using System.IO;
using RVTuk.Core.FamilyBrowser.Database;
using RVTuk.Core.FamilyBrowser.Models;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser;

// The deep scan writes the _Version value through IndexRepository; the Family Browser reads
// (and fast-Syncs) the same DB file through BrowserRepository. These tests exercise that
// cross-repository handshake on one database file, like production.
public class BrowserRepositoryVersionTests : IDisposable
{
    private readonly string _root;
    private readonly string _dbPath;
    private readonly DateTime _modified = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public BrowserRepositoryVersionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rvtuk_browserrepo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");

        using var index = new IndexRepository(_dbPath);
        long id = index.InsertFamily("Doors/A.rfa", "A.rfa");
        index.UpdateFamilyMetadata(id, "Doors", new List<ParameterModel>(), null,
            revitYear: 0, modifiedDate: _modified, fileSize: 10, familyVersion: "2");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    [Fact]
    public void GetAllFamilies_ReturnsVersionWrittenByDeepScan()
    {
        using var repo = new BrowserRepository(_dbPath);
        var item = Assert.Single(repo.GetAllFamilies());
        Assert.Equal("2", item.Version);
    }

    [Fact]
    public void UpsertFamily_UnchangedFile_KeepsVersion()
    {
        using var repo = new BrowserRepository(_dbPath);
        repo.UpsertFamily("Doors/A.rfa", "A.rfa", _modified, 10);

        Assert.Equal("2", Assert.Single(repo.GetAllFamilies()).Version);
    }

    [Fact]
    public void UpsertFamily_ChangedFile_ClearsStaleVersion()
    {
        using var repo = new BrowserRepository(_dbPath);
        repo.UpsertFamily("Doors/A.rfa", "A.rfa", _modified.AddMinutes(5), 10);

        Assert.Null(Assert.Single(repo.GetAllFamilies()).Version);
    }

    [Fact]
    public void UpsertFamily_ChangedFile_MarksParametersStale_SoDeepScanReExtracts()
    {
        using (var repo = new BrowserRepository(_dbPath))
            repo.UpsertFamily("Doors/A.rfa", "A.rfa", _modified.AddMinutes(5), 10);

        using var index = new IndexRepository(_dbPath);
        Assert.Empty(index.GetFamilyIdsWithParametersExtracted());
    }

    [Fact]
    public void UpsertFamily_UnchangedFile_KeepsParametersExtracted()
    {
        using (var repo = new BrowserRepository(_dbPath))
            repo.UpsertFamily("Doors/A.rfa", "A.rfa", _modified, 10);

        using var index = new IndexRepository(_dbPath);
        Assert.Single(index.GetFamilyIdsWithParametersExtracted());
    }

    [Fact]
    public void UpsertFamily_NewFamily_HasNoVersion()
    {
        using var repo = new BrowserRepository(_dbPath);
        repo.UpsertFamily("Doors/B.rfa", "B.rfa", _modified, 20);

        var b = repo.GetAllFamilies().Find(f => f.FileName == "B.rfa");
        Assert.NotNull(b);
        Assert.Null(b!.Version);
    }

    // The browser's schema migration is best-effort by design (read-only share, DB locked by
    // another user → silently skipped). Reads must therefore tolerate a DB that predates the
    // Version/ParametersExtracted columns instead of crashing on "no such column: f.Version".
    [Fact]
    public void GetAllFamilies_ReadOnlyDbWithoutVersionColumn_DegradesToNullVersion()
    {
        var oldDbPath = Path.Combine(_root, "old.db");
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={oldDbPath}"))
        {
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
                    IndexedDate DATETIME DEFAULT CURRENT_TIMESTAMP,
                    InstructionsXaml TEXT,
                    RevitYear INTEGER NOT NULL DEFAULT 0,
                    Tags TEXT,
                    IsFavorite INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE Thumbnail (Id INTEGER PRIMARY KEY, FamilyId INTEGER UNIQUE NOT NULL, PngData BLOB NOT NULL);
                CREATE TABLE CustomThumbnail (Id INTEGER PRIMARY KEY, FamilyId INTEGER UNIQUE NOT NULL, PngData BLOB NOT NULL, OleSynced INTEGER NOT NULL DEFAULT 1);
                INSERT INTO Families (RelativePath, FileName, ModifiedDate, FileSize)
                VALUES ('Doors/Old.rfa', 'Old.rfa', '2026-01-01T12:00:00.0000000Z', 5);";
            cmd.ExecuteNonQuery();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.SetAttributes(oldDbPath, FileAttributes.ReadOnly);
        try
        {
            using var repo = new BrowserRepository(oldDbPath);
            var item = Assert.Single(repo.GetAllFamilies());
            Assert.Equal("Old.rfa", item.FileName);
            Assert.Null(item.Version);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.SetAttributes(oldDbPath, FileAttributes.Normal);
        }
    }
}
