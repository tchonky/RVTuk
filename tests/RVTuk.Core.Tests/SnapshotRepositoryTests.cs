using System;
using System.IO;
using System.Linq;
using RVTuk.Core.Database;
using RVTuk.Core.Models.Comparison;
using RVTuk.Core.Serialization;
using Xunit;

namespace RVTuk.Core.Tests;

public class SnapshotRepositoryTests : IDisposable
{
    private readonly string _dbPath;

    public SnapshotRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "rvtuk_test_" + Guid.NewGuid().ToString("N") + ".db");
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private static CategoryPayload Payload(ViewTemplatesSnapshot snap) => new CategoryPayload
    {
        CategoryId = snap.CategoryId,
        PayloadJson = SnapshotJson.Serialize(snap),
        ItemCount = snap.Templates.Count,
    };

    [Fact]
    public void SaveAndList_RoundTripsMetaAndPayload()
    {
        var snap = new ViewTemplatesSnapshot();
        snap.Templates.Add(new ViewTemplateDto { Name = "FP", ViewType = "FloorPlan" });
        var meta = new SnapshotMeta { SourceKind = "Project", SourceName = "Alpha", RevitYear = 2025, CapturedUtc = DateTime.UtcNow.ToString("o") };

        long id;
        using (var repo = new SnapshotRepository(_dbPath))
        {
            id = repo.SaveSnapshot(meta, new[] { Payload(snap) });
        }

        using (var repo = new SnapshotRepository(_dbPath))
        {
            var list = repo.ListSnapshots();
            Assert.Single(list);
            Assert.Equal("Alpha", list[0].SourceName);

            var got = repo.GetMeta(id);
            Assert.NotNull(got);
            Assert.Equal(2025, got!.RevitYear);

            var cats = repo.LoadCategories(id, (catId, json) =>
                SnapshotJson.Deserialize<ViewTemplatesSnapshot>(json));
            var vt = Assert.IsType<ViewTemplatesSnapshot>(Assert.Single(cats));
            Assert.Equal("FP", vt.Templates.Single().Name);
        }
    }

    [Fact]
    public void EnsureSchema_IsIdempotent()
    {
        using var repo = new SnapshotRepository(_dbPath);
        repo.EnsureSchema();
        repo.EnsureSchema();
        Assert.Empty(repo.ListSnapshots());
    }

    [Fact]
    public void LogStandardChange_IsRecorded()
    {
        using var repo = new SnapshotRepository(_dbPath);
        var id = repo.SaveSnapshot(
            new SnapshotMeta { SourceKind = "Standard", SourceName = "The Standard", CapturedUtc = DateTime.UtcNow.ToString("o"), IsMutable = true },
            Enumerable.Empty<CategoryPayload>());

        repo.LogStandardChange(id, "ViewTemplates", "FloorPlan|FP", "Accept", null, null);

        Assert.Equal(1, repo.CountStandardChanges(id));
    }

    [Fact]
    public void Snapshot_IsMutableFlag_RoundTrips()
    {
        using var repo = new SnapshotRepository(_dbPath);
        var id = repo.SaveSnapshot(
            new SnapshotMeta { SourceKind = "Standard", SourceName = "S", CapturedUtc = "2026-01-01T00:00:00.0000000Z", IsMutable = true, Revision = 3 },
            Enumerable.Empty<CategoryPayload>());
        var meta = repo.GetMeta(id)!;
        Assert.True(meta.IsMutable);
        Assert.Equal(3, meta.Revision);
    }
}
