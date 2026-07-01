using System;
using System.Collections.Generic;
using System.IO;
using RVTuk.Core.Database;
using RVTuk.Core.Models;
using Xunit;

namespace RVTuk.Core.Tests;

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
}
