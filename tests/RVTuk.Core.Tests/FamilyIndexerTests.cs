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
    // swallows non-OLE files and returns (null, 0), so the indexer treats them as valid families
    // whose thumbnail extraction always "fails" — every thumbnail-facet test below is written
    // around that constraint (successful thumbnail commits are covered directly in
    // IndexRepositoryTests instead).
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

        var first = indexer.Scan(NoProgress, default, includeParameters: true);
        var item = Assert.Single(first);      // A is new -> needs parameter extraction

        // A scan no longer marks a family current on its own: extraction must succeed first
        // (so a cancelled scan leaves the row stale and re-scannable). Simulate that success.
        repo.UpdateFamilyMetadata(item.FamilyId, "Doors", new List<ParameterModel>(), null,
            revitYear: 0, modifiedDate: item.ModifiedDate, fileSize: item.FileSize);

        var second = indexer.Scan(NoProgress, default, includeParameters: true);
        Assert.Empty(second);                 // A unchanged & extracted -> skipped
    }

    [Fact]
    public void Scan_Incremental_ReextractsModifiedFile()
    {
        var a = WriteRfa("Doors/A.rfa", "one");
        using var repo = new IndexRepository(_dbPath);
        var indexer = new FamilyIndexer(repo, _root);
        Assert.Single(indexer.Scan(NoProgress, default, includeParameters: true));   // initial index

        // Release the leaked OpenMcdf handle (fake .rfa), then change the file size.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        File.WriteAllText(a, "two-different-and-longer");

        var second = indexer.Scan(NoProgress, default, includeParameters: true);     // incremental
        Assert.Single(second);                     // A changed -> re-extracted
    }

    [Fact]
    public void Scan_NeitherFlag_SyncsFilenamesOnly_NoExtraction()
    {
        WriteRfa("Doors/A.rfa");
        WriteRfa("Windows/B.rfa");
        using var repo = new IndexRepository(_dbPath);
        var indexer = new FamilyIndexer(repo, _root);

        var work = indexer.Scan(NoProgress);      // both flags default to false

        Assert.Empty(work);
        Assert.Equal(2, repo.GetAllRelativePaths().Count);
        Assert.Empty(repo.GetFamilyIdsWithThumbnail());
        Assert.Empty(repo.GetFamilyIdsWithParametersExtracted());

        // Real size/date written directly (no InsertFamily sentinel).
        var rel = repo.GetAllRelativePaths().First(p => p.EndsWith("A.rfa", StringComparison.OrdinalIgnoreCase));
        var family = repo.GetFamilyByPath(rel)!;
        Assert.NotEqual(0, family.FileSize);
    }

    [Fact]
    public void Scan_IncludeThumbnailsOnly_NeverQueuesWorkItem()
    {
        WriteRfa("Doors/A.rfa");
        using var repo = new IndexRepository(_dbPath);
        var indexer = new FamilyIndexer(repo, _root);

        var work = indexer.Scan(NoProgress, default, includeThumbnails: true);

        Assert.Empty(work);                            // thumbnails-only never reaches Phase 2
        Assert.Single(repo.GetAllRelativePaths());      // file info still synced
    }

    [Fact]
    public void Scan_IncludeParametersOnly_NewFile_QueuesWorkItem_WithNullThumbnail()
    {
        WriteRfa("Doors/A.rfa");
        using var repo = new IndexRepository(_dbPath);
        var indexer = new FamilyIndexer(repo, _root);

        var work = indexer.Scan(NoProgress, default, includeParameters: true);

        var item = Assert.Single(work);
        Assert.Null(item.ThumbnailPng);    // includeThumbnails was false -> never attempted
    }

    [Fact]
    public void Scan_BothFlags_NewFile_QueuesWorkItem()
    {
        WriteRfa("Doors/A.rfa");
        using var repo = new IndexRepository(_dbPath);
        var indexer = new FamilyIndexer(repo, _root);

        var work = indexer.Scan(NoProgress, default, includeThumbnails: true, includeParameters: true);

        Assert.Single(work);   // needs parameters -> queued regardless of thumbnail outcome
    }

    [Fact]
    public void Scan_MissingParameters_PickedUpDespiteUnchangedFile_SyncBugFix()
    {
        // Reproduces the fast-Sync-then-deep-scan bug: a family whose real size/date are already
        // recorded (e.g. by a filenames-only sync) but that has never had its parameters
        // extracted must still be picked up by a parameters scan, even though nothing changed.
        var full = WriteRfa("Doors/A.rfa");
        using var repo = new IndexRepository(_dbPath);
        var info = new FileInfo(full);
        repo.UpsertFamilyFileInfo("Doors\\A.rfa", "A.rfa", info.Length, info.LastWriteTimeUtc);

        var indexer = new FamilyIndexer(repo, _root);
        var work = indexer.Scan(NoProgress, default, includeParameters: true);

        Assert.Single(work);   // still picked up despite matching size/date
    }

    [Fact]
    public void Scan_ZeroParameterFamily_NotReExtractedForever()
    {
        WriteRfa("Doors/A.rfa");
        using var repo = new IndexRepository(_dbPath);
        var indexer = new FamilyIndexer(repo, _root);

        var first = indexer.Scan(NoProgress, default, includeParameters: true);
        var item = Assert.Single(first);

        // Extraction succeeds but genuinely finds zero parameters.
        repo.UpdateFamilyMetadata(item.FamilyId, "Doors", new List<ParameterModel>(), null,
            revitYear: 0, modifiedDate: item.ModifiedDate, fileSize: item.FileSize);

        var second = indexer.Scan(NoProgress, default, includeParameters: true);
        Assert.Empty(second);   // ParametersExtracted=1 regardless of row count -> not re-queued
    }

    [Fact]
    public void Scan_DoesNotWalkIgnoredSubfolders()
    {
        WriteRfa("Doors/A.rfa");
        WriteRfa("Archive/Old.rfa");
        using var repo = new IndexRepository(_dbPath);
        var indexer = new FamilyIndexer(repo, _root, new List<string> { "Archive" });

        var work = indexer.Scan(NoProgress, default, includeParameters: true);

        Assert.Single(work);                                       // only Doors/A; Archive not walked
        Assert.Null(repo.GetFamilyByPath("Archive\\Old.rfa"));     // never indexed
        Assert.Equal(0, indexer.SkippedIgnored);                   // nothing was previously indexed under it
    }

    [Fact]
    public void Scan_PreservesPreviouslyIndexedFamilyWhenItsFolderBecomesIgnored()
    {
        WriteRfa("Doors/A.rfa");
        WriteRfa("Archive/Old.rfa");
        using var repo = new IndexRepository(_dbPath);

        // First scan with nothing ignored: both families get indexed (rows created; A's
        // extraction is left uncommitted so it still "needs parameters" on the next scan below —
        // mirrors Scan_NewFamilyScannedButNotExtracted_IsReturnedAgainNextScan).
        new FamilyIndexer(repo, _root).Scan(NoProgress, default, includeParameters: true);
        Assert.Equal(2, repo.GetAllRelativePaths().Count);

        // Now ignore Archive and rescan: the Archive row must be kept (browser just hides it),
        // not pruned as stale, and not re-extracted. Doors/A still needs extraction (never
        // committed above), so it must appear again.
        var indexer = new FamilyIndexer(repo, _root, new List<string> { "Archive" });
        var work = indexer.Scan(NoProgress, default, includeParameters: true);

        Assert.Equal(2, repo.GetAllRelativePaths().Count);        // Archive row preserved
        Assert.Single(work);                                      // only Doors/A re-extracted
        Assert.Equal(1, indexer.SkippedIgnored);                  // Archive/Old.rfa protected
        Assert.DoesNotContain(work, w =>
            w.RelativePath.IndexOf("Old", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void Scan_CaseOnlyRename_KeepsRowAndReKeysToDiskCasing()
    {
        WriteRfa("Doors/A.rfa");
        using var repo = new IndexRepository(_dbPath);

        var first = new FamilyIndexer(repo, _root).Scan(NoProgress, default, includeParameters: true);
        var item = Assert.Single(first);
        repo.UpdateFamilyMetadata(item.FamilyId, "Doors", new List<ParameterModel>(), null,
            revitYear: 0, modifiedDate: item.ModifiedDate, fileSize: item.FileSize);

        // Simulate a case-only rename of the folder ("Doors" -> "doors"). On Windows this is the
        // same file; the DB row (and its curated data) must survive under the same Id, re-keyed
        // to the new casing — not be pruned as stale and re-indexed as a new family.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var oldDir = Path.Combine(_root, "Doors");
        var newDir = Path.Combine(_root, "doors");
        var tmpDir = Path.Combine(_root, "doors_tmp");
        Directory.Move(oldDir, tmpDir);   // two-step: a direct case-only Move throws on Windows
        Directory.Move(tmpDir, newDir);

        var second = new FamilyIndexer(repo, _root).Scan(NoProgress, default, includeParameters: true);

        Assert.Empty(second);                                          // unchanged content -> no re-extraction
        var path = Assert.Single(repo.GetAllRelativePaths());          // no duplicate, not pruned
        Assert.StartsWith("doors", path);                              // re-keyed to on-disk casing
        var reFound = repo.GetFamilyByPath(path);
        Assert.NotNull(reFound);
        Assert.Equal(item.FamilyId, reFound!.Id);                      // same row -> curated data kept
    }

    [Fact]
    public void Scan_PrunesFamiliesWhoseFileIsGone()
    {
        var a = WriteRfa("Doors/A.rfa");
        WriteRfa("Windows/B.rfa");
        using var repo = new IndexRepository(_dbPath);
        var indexer = new FamilyIndexer(repo, _root);
        indexer.Scan(NoProgress);      // filenames-only sync is enough to add both rows
        Assert.Equal(2, repo.GetAllRelativePaths().Count);

        // The fake .rfa is not a valid OLE file, so OpenMcdf throws while opening it and leaks
        // the file handle until finalization. Force collection so we can delete it.
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
        WriteRfa("Doors/A.rfa");
        using var repo = new IndexRepository(_dbPath);
        var indexer = new FamilyIndexer(repo, _root);

        var first = indexer.Scan(NoProgress, default, includeParameters: true);
        Assert.Single(first);                 // new -> needs extraction

        // No UpdateFamilyMetadata call (the Revit ExternalEvent never ran / was cancelled).
        var second = indexer.Scan(NoProgress, default, includeParameters: true);
        Assert.Single(second);                // still needs extraction next time
    }

    [Fact]
    public void Scan_ExistingChangedFamilyScannedButNotExtracted_IsReturnedAgainNextScan()
    {
        var a = WriteRfa("Doors/A.rfa", "one");
        using var repo = new IndexRepository(_dbPath);
        var indexer = new FamilyIndexer(repo, _root);

        // Fully index the family once (write its real size/date so it's "current").
        var first = indexer.Scan(NoProgress, default, includeParameters: true);
        var item = Assert.Single(first);
        repo.UpdateFamilyMetadata(item.FamilyId, "Doors", new List<ParameterModel>(), null,
            revitYear: 0, modifiedDate: item.ModifiedDate, fileSize: item.FileSize);
        Assert.Empty(indexer.Scan(NoProgress, default, includeParameters: true)); // confirm skipped

        // Change the file. Release the leaked OpenMcdf handle first.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        File.WriteAllText(a, "two-different-and-longer");

        // Scan sees the change but extraction is cancelled (no UpdateFamilyMetadata).
        var second = indexer.Scan(NoProgress, default, includeParameters: true);
        Assert.Single(second);                // changed -> needs extraction

        // InsertFamily must NOT have overwritten the stored size/date on conflict,
        // so the family still looks stale and is returned again.
        var third = indexer.Scan(NoProgress, default, includeParameters: true);
        Assert.Single(third);
    }

    [Fact]
    public void Scan_KeepsStableRowId_OnReindex()
    {
        // The non-destructive "rebuild" guarantee: re-indexing an existing family updates the
        // row in place (ON CONFLICT DO UPDATE) and keeps its Id, so all curated data keyed by
        // that Id — instructions, tags, favourites, custom thumbnails, gallery — survives.
        var a = WriteRfa("Doors/A.rfa", "one");
        using var repo = new IndexRepository(_dbPath);
        var indexer = new FamilyIndexer(repo, _root);
        var first = indexer.Scan(NoProgress, default, includeParameters: true);
        var item = Assert.Single(first);
        repo.UpdateFamilyMetadata(item.FamilyId, "Doors", new List<ParameterModel>(), null,
            revitYear: 0, modifiedDate: item.ModifiedDate, fileSize: item.FileSize);

        var rel = repo.GetAllRelativePaths().Single();
        long id1 = repo.GetFamilyByPath(rel)!.Id;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        File.WriteAllText(a, "two-different-and-longer");
        indexer.Scan(NoProgress, default, includeParameters: true);

        long id2 = repo.GetFamilyByPath(rel)!.Id;
        Assert.Equal(id1, id2);
    }
}
