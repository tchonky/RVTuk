using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RVTuk.Core.Database;
using RVTuk.Core.Extraction;
using RVTuk.Core.Models;
using Xunit;

namespace RVTuk.Core.Tests;

public class FamilyIndexerTests : IDisposable
{
    private readonly string _root;
    private readonly string _dbPath;

    public FamilyIndexerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rvtuk_idx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    // Writes a fake .rfa under the library root. Content is irrelevant: ThumbnailExtractor
    // swallows non-OLE files and returns (null, 0), so the indexer treats them as valid families.
    private string WriteRfa(string relative, string content = "fake")
    {
        var full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private static void NoProgress(string f, int c, int t) { }

    [Fact]
    public void Scan_Incremental_SkipsUnchangedFiles()
    {
        WriteRfa("Doors/A.rfa");
        using var repo = new IndexRepository(_dbPath);
        var indexer = new FamilyIndexer(repo, _root);

        var first = indexer.Scan(NoProgress);
        var item = Assert.Single(first);      // A is new -> needs extraction

        // A scan no longer marks a family current on its own: extraction must succeed first
        // (so a cancelled scan leaves the row stale and re-scannable). Simulate that success.
        repo.UpdateFamilyMetadata(item.FamilyId, "Doors", new List<ParameterModel>(), null,
            revitYear: 0, modifiedDate: item.ModifiedDate, fileSize: item.FileSize);

        var second = indexer.Scan(NoProgress);
        Assert.Empty(second);                 // A unchanged & extracted -> skipped
    }

    [Fact]
    public void Scan_Incremental_ReextractsModifiedFile()
    {
        var a = WriteRfa("Doors/A.rfa", "one");
        using var repo = new IndexRepository(_dbPath);
        var indexer = new FamilyIndexer(repo, _root);
        Assert.Single(indexer.Scan(NoProgress));   // initial index

        // Release the leaked OpenMcdf handle (fake .rfa), then change the file size.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        File.WriteAllText(a, "two-different-and-longer");

        var second = indexer.Scan(NoProgress);     // incremental
        Assert.Single(second);                     // A changed -> re-extracted
    }

    [Fact]
    public void Scan_ForceReextractAll_ReturnsEveryFileEvenWhenUnchanged()
    {
        WriteRfa("Doors/A.rfa");
        WriteRfa("Windows/B.rfa");
        using var repo = new IndexRepository(_dbPath);
        var indexer = new FamilyIndexer(repo, _root);

        indexer.Scan(NoProgress);             // index both
        var forced = indexer.Scan(NoProgress, default, forceReextractAll: true);

        Assert.Equal(2, forced.Count);        // both re-extracted despite no file change
    }

    [Fact]
    public void Scan_ForceReextractAll_DoesNotWalkIgnoredSubfolders()
    {
        WriteRfa("Doors/A.rfa");
        WriteRfa("Archive/Old.rfa");
        using var repo = new IndexRepository(_dbPath);
        var indexer = new FamilyIndexer(repo, _root, new List<string> { "Archive" });

        var forced = indexer.Scan(NoProgress, default, forceReextractAll: true);

        Assert.Single(forced);                                    // only Doors/A; Archive not walked
        Assert.Null(repo.GetFamilyByPath("Archive\\Old.rfa"));    // never indexed
        Assert.Equal(0, indexer.SkippedIgnored);                  // nothing was previously indexed under it
    }

    [Fact]
    public void Scan_PreservesPreviouslyIndexedFamilyWhenItsFolderBecomesIgnored()
    {
        WriteRfa("Doors/A.rfa");
        WriteRfa("Archive/Old.rfa");
        using var repo = new IndexRepository(_dbPath);

        // First scan with nothing ignored: both families get indexed.
        new FamilyIndexer(repo, _root).Scan(NoProgress);
        Assert.Equal(2, repo.GetAllRelativePaths().Count);

        // Now ignore Archive and rescan: the Archive row must be kept (browser just hides it),
        // not pruned as stale, and not re-extracted.
        var indexer = new FamilyIndexer(repo, _root, new List<string> { "Archive" });
        var work = indexer.Scan(NoProgress, default, forceReextractAll: true);

        Assert.Equal(2, repo.GetAllRelativePaths().Count);        // Archive row preserved
        Assert.Single(work);                                      // only Doors/A re-extracted
        Assert.Equal(1, indexer.SkippedIgnored);                  // Archive/Old.rfa protected
        Assert.DoesNotContain(work, w =>
            w.RelativePath.IndexOf("Old", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void Scan_PrunesFamiliesWhoseFileIsGone()
    {
        var a = WriteRfa("Doors/A.rfa");
        WriteRfa("Windows/B.rfa");
        using var repo = new IndexRepository(_dbPath);
        var indexer = new FamilyIndexer(repo, _root);
        indexer.Scan(NoProgress);
        Assert.Equal(2, repo.GetAllRelativePaths().Count);

        // The fake .rfa is not a valid OLE file, so OpenMcdf throws while opening it and leaks
        // the file handle until finalization. Force collection so we can delete it. (Real .rfa
        // files are valid compound files and never hit this path; production never deletes mid-scan.)
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        File.Delete(a);
        indexer.Scan(NoProgress);

        var remaining = repo.GetAllRelativePaths();
        Assert.Single(remaining);
        Assert.DoesNotContain(remaining, p => p.EndsWith("A.rfa", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_NewFamilyScannedButNotExtracted_IsReturnedAgainNextScan()
    {
        // Simulates cancelling a deep scan before a new family's metadata is written.
        // The row exists (so its Id/curated data is stable) but carries a sentinel
        // size/date, so the next scan must still see it as needing extraction.
        WriteRfa("Doors/A.rfa");
        using var repo = new IndexRepository(_dbPath);
        var indexer = new FamilyIndexer(repo, _root);

        var first = indexer.Scan(NoProgress);
        Assert.Single(first);                 // new -> needs extraction

        // No UpdateFamilyMetadata call (the Revit ExternalEvent never ran / was cancelled).
        var second = indexer.Scan(NoProgress);
        Assert.Single(second);                // still needs extraction next time
    }

    [Fact]
    public void Scan_AfterUpdateFamilyMetadataWritesRealSizeDate_SkipsFamilyNextScan()
    {
        WriteRfa("Doors/A.rfa");
        using var repo = new IndexRepository(_dbPath);
        var indexer = new FamilyIndexer(repo, _root);

        var first = indexer.Scan(NoProgress);
        var item = Assert.Single(first);

        // Successful extraction marks the row current by writing the real size/date.
        repo.UpdateFamilyMetadata(item.FamilyId, "Doors", new List<ParameterModel>(), null,
            revitYear: 0, modifiedDate: item.ModifiedDate, fileSize: item.FileSize);

        var second = indexer.Scan(NoProgress);
        Assert.Empty(second);                 // now up to date -> skipped
    }

    [Fact]
    public void Scan_ExistingChangedFamilyScannedButNotExtracted_IsReturnedAgainNextScan()
    {
        var a = WriteRfa("Doors/A.rfa", "one");
        using var repo = new IndexRepository(_dbPath);
        var indexer = new FamilyIndexer(repo, _root);

        // Fully index the family once (write its real size/date so it's "current").
        var first = indexer.Scan(NoProgress);
        var item = Assert.Single(first);
        repo.UpdateFamilyMetadata(item.FamilyId, "Doors", new List<ParameterModel>(), null,
            revitYear: 0, modifiedDate: item.ModifiedDate, fileSize: item.FileSize);
        Assert.Empty(indexer.Scan(NoProgress)); // confirm it's now skipped

        // Change the file. Release the leaked OpenMcdf handle first.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        File.WriteAllText(a, "two-different-and-longer");

        // Scan sees the change but extraction is cancelled (no UpdateFamilyMetadata).
        var second = indexer.Scan(NoProgress);
        Assert.Single(second);                // changed -> needs extraction

        // InsertFamily must NOT have overwritten the stored size/date on conflict,
        // so the family still looks stale and is returned again.
        var third = indexer.Scan(NoProgress);
        Assert.Single(third);
    }

    [Fact]
    public void Scan_ForceReextractAll_KeepsStableRowId()
    {
        // The non-destructive "rebuild" guarantee: re-indexing an existing family updates the
        // row in place (ON CONFLICT DO UPDATE) and keeps its Id, so all curated data keyed by
        // that Id — instructions, tags, favourites, custom thumbnails, gallery — survives.
        WriteRfa("Doors/A.rfa");
        using var repo = new IndexRepository(_dbPath);
        var indexer = new FamilyIndexer(repo, _root);
        indexer.Scan(NoProgress);

        var rel = repo.GetAllRelativePaths().Single();
        long id1 = repo.GetFamilyByPath(rel)!.Id;

        indexer.Scan(NoProgress, default, forceReextractAll: true);

        long id2 = repo.GetFamilyByPath(rel)!.Id;
        Assert.Equal(id1, id2);
    }
}
