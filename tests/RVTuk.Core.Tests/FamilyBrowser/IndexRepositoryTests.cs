using System;
using System.Collections.Generic;
using System.IO;
using RVTuk.Core.FamilyBrowser.Database;
using RVTuk.Core.FamilyBrowser.Models;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser;

public class IndexRepositoryTests : IDisposable
{
    private readonly string _root;
    private readonly string _dbPath;

    public IndexRepositoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rvtuk_repo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    [Fact]
    public void UpsertFamilyFileInfo_NewFamily_WritesRealSizeAndDate()
    {
        using var repo = new IndexRepository(_dbPath);
        var modified = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        repo.UpsertFamilyFileInfo("Doors/A.rfa", "A.rfa", 1234, modified);

        var family = repo.GetFamilyByPath("Doors/A.rfa");
        Assert.NotNull(family);
        Assert.Equal(1234, family!.FileSize);
        Assert.Equal(modified, family.ModifiedDate);
    }

    [Fact]
    public void UpsertFamilyFileInfo_ExistingFamily_UpdatesNameSizeDate_KeepsId()
    {
        using var repo = new IndexRepository(_dbPath);
        long id1 = repo.InsertFamily("Doors/A.rfa", "A.rfa");

        repo.UpsertFamilyFileInfo("Doors/A.rfa", "A-renamed.rfa", 999,
            new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc));

        var family = repo.GetFamilyByPath("Doors/A.rfa");
        Assert.Equal(id1, family!.Id);
        Assert.Equal("A-renamed.rfa", family.FileName);
        Assert.Equal(999, family.FileSize);
    }

    [Fact]
    public void UpdateThumbnailOnly_WritesThumbnailAndFileInfo_LeavesCategoryAlone()
    {
        using var repo = new IndexRepository(_dbPath);
        long id = repo.InsertFamily("Doors/A.rfa", "A.rfa");
        var png = new byte[] { 1, 2, 3 };
        var modified = new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc);

        repo.UpdateThumbnailOnly(id, png, revitYear: 2024, modified, fileSize: 555);

        var family = repo.GetFamilyByPath("Doors/A.rfa");
        Assert.Equal(555, family!.FileSize);
        Assert.Equal(modified, family.ModifiedDate);
        Assert.Null(family.Category);
        Assert.Contains(id, repo.GetFamilyIdsWithThumbnail());
        Assert.DoesNotContain(id, repo.GetFamilyIdsWithParametersExtracted());
    }

    [Fact]
    public void GetFamilyIdsWithParametersExtracted_SetByUpdateFamilyMetadata_EvenWithNoParameters()
    {
        using var repo = new IndexRepository(_dbPath);
        long id = repo.InsertFamily("Doors/A.rfa", "A.rfa");

        repo.UpdateFamilyMetadata(id, "Doors", new List<ParameterModel>(), null,
            revitYear: 0, modifiedDate: DateTime.UtcNow, fileSize: 10);

        Assert.Contains(id, repo.GetFamilyIdsWithParametersExtracted());
    }

    [Fact]
    public void GetFamilyIdsWithThumbnail_EmptyForFamilyWithNoThumbnailRow()
    {
        using var repo = new IndexRepository(_dbPath);
        long id = repo.InsertFamily("Doors/A.rfa", "A.rfa");

        Assert.DoesNotContain(id, repo.GetFamilyIdsWithThumbnail());
    }

    [Fact]
    public void UpdateFamilyMetadata_StoresFamilyVersion()
    {
        using var repo = new IndexRepository(_dbPath);
        long id = repo.InsertFamily("Doors/A.rfa", "A.rfa");

        repo.UpdateFamilyMetadata(id, "Doors", new List<ParameterModel>(), null,
            revitYear: 0, modifiedDate: DateTime.UtcNow, fileSize: 10, familyVersion: "3");

        Assert.Equal("3", repo.GetFamilyByPath("Doors/A.rfa")!.Version);
    }

    [Fact]
    public void UpsertFamilyFileInfo_UnchangedFile_KeepsVersion()
    {
        using var repo = new IndexRepository(_dbPath);
        long id = repo.InsertFamily("Doors/A.rfa", "A.rfa");
        var modified = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        repo.UpdateFamilyMetadata(id, "Doors", new List<ParameterModel>(), null,
            revitYear: 0, modifiedDate: modified, fileSize: 10, familyVersion: "3");

        repo.UpsertFamilyFileInfo("Doors/A.rfa", "A.rfa", 10, modified);

        Assert.Equal("3", repo.GetFamilyByPath("Doors/A.rfa")!.Version);
    }

    [Fact]
    public void UpsertFamilyFileInfo_ChangedFile_ClearsStaleVersion()
    {
        using var repo = new IndexRepository(_dbPath);
        long id = repo.InsertFamily("Doors/A.rfa", "A.rfa");
        var modified = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        repo.UpdateFamilyMetadata(id, "Doors", new List<ParameterModel>(), null,
            revitYear: 0, modifiedDate: modified, fileSize: 10, familyVersion: "3");

        repo.UpsertFamilyFileInfo("Doors/A.rfa", "A.rfa", 10, modified.AddMinutes(5));

        Assert.Null(repo.GetFamilyByPath("Doors/A.rfa")!.Version);
    }

    [Fact]
    public void UpdateThumbnailOnly_ChangedFile_ClearsStaleVersion()
    {
        using var repo = new IndexRepository(_dbPath);
        long id = repo.InsertFamily("Doors/A.rfa", "A.rfa");
        var modified = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        repo.UpdateFamilyMetadata(id, "Doors", new List<ParameterModel>(), null,
            revitYear: 0, modifiedDate: modified, fileSize: 10, familyVersion: "3");

        repo.UpdateThumbnailOnly(id, new byte[] { 1 }, revitYear: 2024, modified.AddMinutes(5), fileSize: 11);

        Assert.Null(repo.GetFamilyByPath("Doors/A.rfa")!.Version);
    }

    // Writing a CHANGED file's info without re-extracting must also drop ParametersExtracted,
    // or the next deep scan sees matching size/date + "already extracted" and skips the family
    // forever — its stored parameters (and _Version) would silently stay stale.
    [Fact]
    public void UpsertFamilyFileInfo_ChangedFile_MarksParametersStale()
    {
        using var repo = new IndexRepository(_dbPath);
        long id = repo.InsertFamily("Doors/A.rfa", "A.rfa");
        var modified = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        repo.UpdateFamilyMetadata(id, "Doors", new List<ParameterModel>(), null,
            revitYear: 0, modifiedDate: modified, fileSize: 10, familyVersion: "3");

        repo.UpsertFamilyFileInfo("Doors/A.rfa", "A.rfa", 10, modified.AddMinutes(5));

        Assert.DoesNotContain(id, repo.GetFamilyIdsWithParametersExtracted());
    }

    [Fact]
    public void UpsertFamilyFileInfo_UnchangedFile_KeepsParametersExtracted()
    {
        using var repo = new IndexRepository(_dbPath);
        long id = repo.InsertFamily("Doors/A.rfa", "A.rfa");
        var modified = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        repo.UpdateFamilyMetadata(id, "Doors", new List<ParameterModel>(), null,
            revitYear: 0, modifiedDate: modified, fileSize: 10, familyVersion: "3");

        repo.UpsertFamilyFileInfo("Doors/A.rfa", "A.rfa", 10, modified);

        Assert.Contains(id, repo.GetFamilyIdsWithParametersExtracted());
    }

    [Fact]
    public void UpdateThumbnailOnly_ChangedFile_MarksParametersStale()
    {
        using var repo = new IndexRepository(_dbPath);
        long id = repo.InsertFamily("Doors/A.rfa", "A.rfa");
        var modified = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        repo.UpdateFamilyMetadata(id, "Doors", new List<ParameterModel>(), null,
            revitYear: 0, modifiedDate: modified, fileSize: 10, familyVersion: "3");

        repo.UpdateThumbnailOnly(id, new byte[] { 1 }, revitYear: 2024, modified.AddMinutes(5), fileSize: 11);

        Assert.DoesNotContain(id, repo.GetFamilyIdsWithParametersExtracted());
    }

    // SQLite silently degrades a read-write open to read-only when the file denies write
    // access (e.g. Desktop Connector flags a cloud-synced DB ReadOnly while another user
    // holds it). Every IndexRepository caller is a writer (scan/rescan), and the silent
    // degradation used to surface only as a raw "attempt to write a readonly database"
    // from whichever statement wrote first — swallowed into "Could not rescan this family".
    [Fact]
    public void Ctor_ReadOnlyDbFile_ThrowsClearReadOnlyError()
    {
        using (var seed = new IndexRepository(_dbPath)) { }
        File.SetAttributes(_dbPath, FileAttributes.ReadOnly);
        try
        {
            var ex = Assert.Throws<IOException>(() => new IndexRepository(_dbPath));
            Assert.Contains("read-only", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(_dbPath, ex.Message);
        }
        finally
        {
            File.SetAttributes(_dbPath, FileAttributes.Normal);
        }
    }

    // A schema-behind DB on a read-only file must produce the same clear error (the auto
    // migration is what used to throw the raw SqliteException mid-ctor).
    [Fact]
    public void Ctor_ReadOnlyDbFile_SchemaBehind_ThrowsClearReadOnlyError()
    {
        using (var seed = new IndexRepository(_dbPath)) { }
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_dbPath};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "ALTER TABLE Families DROP COLUMN Version";
            cmd.ExecuteNonQuery();
        }
        File.SetAttributes(_dbPath, FileAttributes.ReadOnly);
        try
        {
            var ex = Assert.Throws<IOException>(() => new IndexRepository(_dbPath));
            Assert.Contains("read-only", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.SetAttributes(_dbPath, FileAttributes.Normal);
        }
    }
}
