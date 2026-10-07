using System;
using System.Collections.Generic;
using System.IO;
using RVTuk.Core.FamilyBrowser.Database;
using RVTuk.Core.FamilyBrowser.Models;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser;

// The deep scan writes the _Version value through IndexRepository; the Family Browser reads
// the same DB file through BrowserRepository. These tests exercise that cross-repository
// handshake on one database file, like production.
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
        // The seeded family has no parameters at all — nothing to flag.
        Assert.False(item.VersionIsInstance);
    }

    // The office standard wants _Version as a *type* parameter: an instance parameter can't
    // be read off the loaded family's symbols in a project, so the browser paints the version
    // red to say "fix this family". The flag derives from the Parameters rows the deep scan
    // already stores; the extractor matches the name case-insensitively, so the flag must too.
    [Theory]
    [InlineData("_Version", true, true)]
    [InlineData("_version", true, true)] // name match is case-insensitive, like the extractor's
    [InlineData("_Version", false, false)] // type parameter — the standard, nothing to flag
    [InlineData("Width", true, false)] // some other instance parameter is irrelevant
    public void GetAllFamilies_FlagsInstanceVersionParameter(string paramName, bool isInstance, bool expected)
    {
        using (var index = new IndexRepository(_dbPath))
        {
            long id = index.InsertFamily("Doors/B.rfa", "B.rfa");
            index.UpdateFamilyMetadata(id, "Doors",
                new List<ParameterModel>
                {
                    new() { ParameterName = paramName, DataType = "Text", IsInstance = isInstance },
                },
                null, revitYear: 0, modifiedDate: _modified, fileSize: 10, familyVersion: "3");
        }

        using var repo = new BrowserRepository(_dbPath);
        var item = Assert.Single(repo.GetAllFamilies(), i => i.FileName == "B.rfa");
        Assert.Equal(expected, item.VersionIsInstance);
    }

    // A custom thumbnail wins over the OLE one until it is deleted (design §2).
    [Fact]
    public void GetAllFamilies_ResolvesCustomThumbnailOverOle()
    {
        long id;
        using (var index = new IndexRepository(_dbPath))
        {
            id = index.InsertFamily("Doors/B.rfa", "B.rfa");
            index.UpdateFamilyMetadata(id, "Doors", new List<ParameterModel>(), thumbnailPng: new byte[] { 1 },
                revitYear: 0, modifiedDate: _modified, fileSize: 10);
        }
        using var repo = new BrowserRepository(_dbPath);

        repo.SaveCustomThumbnail(id, new byte[] { 2 }, oleSynced: true);
        repo.SaveCustomThumbnail(id, new byte[] { 3 }, oleSynced: false); // upsert, not a second row
        var item = Assert.Single(repo.GetAllFamilies(), i => i.Id == id);
        Assert.Equal(new byte[] { 3 }, item.ThumbnailPng);
        Assert.True(item.HasCustomThumbnail);
        Assert.False(item.OleSynced);

        repo.DeleteCustomThumbnail(id);
        item = Assert.Single(repo.GetAllFamilies(), i => i.Id == id);
        Assert.Equal(new byte[] { 1 }, item.ThumbnailPng);
        Assert.False(item.HasCustomThumbnail);
        Assert.True(item.OleSynced);
    }

    [Fact]
    public void GetFamily_ReturnsThatRow_OrNullForUnknownId()
    {
        long id;
        using (var index = new IndexRepository(_dbPath))
        {
            id = index.InsertFamily("Windows/B.rfa", "B.rfa");
            index.UpdateFamilyMetadata(id, "Windows", new List<ParameterModel>(), null,
                revitYear: 0, modifiedDate: _modified, fileSize: 10, familyVersion: "5");
        }
        using var repo = new BrowserRepository(_dbPath);

        var item = repo.GetFamily(id)!; // A.rfa sorts first, so a missing WHERE would return it
        Assert.Equal("B.rfa", item.FileName);
        Assert.Equal("Windows", item.Category);
        Assert.Equal("5", item.Version);
        Assert.Null(repo.GetFamily(id + 1000));
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
            // This schema predates the Parameters table too — the instance-version flag
            // must degrade to false, not crash the query with "no such table".
            Assert.False(item.VersionIsInstance);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.SetAttributes(oldDbPath, FileAttributes.Normal);
        }
    }
}
