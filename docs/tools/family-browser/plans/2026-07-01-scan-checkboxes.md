# Scan Checkboxes (Thumbnails / Parameters) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Config window's two deep-scan buttons ("Scan New & Changed" / "Re-scan All Families") with one **Scan** button plus two independent checkboxes — **Update thumbnails** and **Update parameters** — and make each facet's "needs refresh" detection independent of the other (missing data, not just a changed file, triggers re-extraction of that facet).

**Architecture:** `FamilyIndexer.Scan` gains two bool params (`includeThumbnails`, `includeParameters`) replacing `forceReextractAll`, and per-file "needs" detection splits into two independent booleans backed by two preloaded `HashSet<long>` (family Ids that already have a `Thumbnail` row / already have `Families.ParametersExtracted = 1`). Families needing only a thumbnail refresh are committed directly from the background thread (no Revit call); families needing parameters still queue a Phase-2 work item for the Revit-engine `ExternalEvent` ping-pong, exactly as today. The UI/Revit layers are updated to match the new two-flag signature.

**Tech Stack:** C# (.NET Framework 4.8 / .NET 8), WPF, `Microsoft.Data.Sqlite`, xUnit.

## Global Constraints

- Both build configs must compile: `Release2024` (net48) and `Release2025` (net8.0-windows).
- No schema change beyond the one new column specified (`Families.ParametersExtracted`); use the existing `ALTER TABLE ... ADD COLUMN` migration pattern in `IndexRepository.MigrateSchema()`.
- Curated data (`InstructionsXaml`, `Tags`, `IsFavorite`, `CustomThumbnail`, `FamilyImage`) must never be touched by any of these changes.
- `RVTuk.Core` must not reference Revit or WPF types; `RVTuk.UI` must not reference Revit types. Only `RVTuk.Revit` touches the Revit API.
- Dates are UTC ISO-8601 (`DateTime.ToString("o")`), read back via `DbConvert.ParseUtc`.
- Full spec: [`docs/superpowers/specs/2026-07-01-scan-checkboxes-design.md`](../specs/2026-07-01-scan-checkboxes-design.md).

---

### Task 1: `IndexRepository` — new column + facet-detection methods

**Files:**
- Modify: `src/RVTuk.Core/Database/IndexRepository.cs`
- Test: `tests/RVTuk.Core.Tests/IndexRepositoryTests.cs` (new)

**Interfaces:**
- Consumes: existing `IndexRepository` ctor, `InsertFamily(string, string)`, `GetFamilyByPath(string)`, `UpdateFamilyMetadata(...)` (all unchanged in shape).
- Produces (used by Task 2):
  - `HashSet<long> GetFamilyIdsWithThumbnail()`
  - `HashSet<long> GetFamilyIdsWithParametersExtracted()`
  - `void UpsertFamilyFileInfo(string relativePath, string fileName, long fileSize, DateTime modifiedDateUtc)`
  - `void UpdateThumbnailOnly(long familyId, byte[] thumbnailPng, int revitYear, DateTime modifiedDate, long fileSize)`
  - `UpdateFamilyMetadata(...)` now also sets `Families.ParametersExtracted = 1` (no signature change).

- [ ] **Step 1: Write the failing tests**

Create `tests/RVTuk.Core.Tests/IndexRepositoryTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter IndexRepositoryTests`
Expected: build error — `GetFamilyIdsWithThumbnail`, `GetFamilyIdsWithParametersExtracted`, `UpsertFamilyFileInfo`, `UpdateThumbnailOnly` don't exist yet.

- [ ] **Step 3: Add the `ParametersExtracted` migration**

In `src/RVTuk.Core/Database/IndexRepository.cs`, inside `MigrateSchema()`, immediately after the existing `IsFavorite` column check (the last block in that method), add:

```csharp
            using var paramsExtractedCheck = _connection.CreateCommand();
            paramsExtractedCheck.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Families') WHERE name='ParametersExtracted'";
            if ((long)(paramsExtractedCheck.ExecuteScalar() ?? 0L) == 0)
                Execute("ALTER TABLE Families ADD COLUMN ParametersExtracted INTEGER NOT NULL DEFAULT 0");
```

- [ ] **Step 4: Add the two preload query methods**

Immediately after `GetAllRelativePaths()` (around line 148), add:

```csharp
        public HashSet<long> GetFamilyIdsWithThumbnail()
        {
            var ids = new HashSet<long>();
            using var cmd = CreateCommand("SELECT DISTINCT FamilyId FROM Thumbnail");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                ids.Add(reader.GetInt64(0));
            return ids;
        }

        public HashSet<long> GetFamilyIdsWithParametersExtracted()
        {
            var ids = new HashSet<long>();
            using var cmd = CreateCommand("SELECT Id FROM Families WHERE ParametersExtracted = 1");
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                ids.Add(reader.GetInt64(0));
            return ids;
        }
```

Add `using System.Collections.Generic;` at the top of the file if not already present (it already is, per the existing `List<string>` usage in `GetAllRelativePaths`).

- [ ] **Step 5: Add `UpsertFamilyFileInfo`**

Immediately after `InsertFamily(...)` (after its closing brace, before `UpdateFamilyMetadata`), add:

```csharp
        public void UpsertFamilyFileInfo(string relativePath, string fileName, long fileSize, DateTime modifiedDateUtc)
        {
            using var cmd = CreateCommand(@"
                INSERT INTO Families (RelativePath, FileName, ModifiedDate, FileSize)
                VALUES (@path, @name, @modified, @size)
                ON CONFLICT(RelativePath) DO UPDATE SET
                    FileName = excluded.FileName,
                    ModifiedDate = excluded.ModifiedDate,
                    FileSize = excluded.FileSize;");
            AddParam(cmd, "@path", relativePath);
            AddParam(cmd, "@name", fileName);
            AddParam(cmd, "@modified", modifiedDateUtc.ToString("o"));
            AddParam(cmd, "@size", fileSize);
            cmd.ExecuteNonQuery();
        }
```

- [ ] **Step 6: Add `UpdateThumbnailOnly` and update `UpdateFamilyMetadata`**

Immediately after `UpdateFamilyMetadata(...)`'s closing brace, add:

```csharp
        public void UpdateThumbnailOnly(long familyId, byte[] thumbnailPng, int revitYear, DateTime modifiedDate, long fileSize)
        {
            using var transaction = _connection.BeginTransaction();
            try
            {
                using var thumbCmd = CreateCommand(
                    "INSERT OR REPLACE INTO Thumbnail (FamilyId, PngData) VALUES (@fid, @png)", transaction);
                AddParam(thumbCmd, "@fid", familyId);
                AddParam(thumbCmd, "@png", thumbnailPng);
                thumbCmd.ExecuteNonQuery();

                using var famCmd = CreateCommand(
                    "UPDATE Families SET RevitYear=@year, ModifiedDate=@modified, FileSize=@size, IndexedDate=@now WHERE Id=@id",
                    transaction);
                AddParam(famCmd, "@year", revitYear);
                AddParam(famCmd, "@modified", modifiedDate.ToString("o"));
                AddParam(famCmd, "@size", fileSize);
                AddParam(famCmd, "@now", DateTime.UtcNow.ToString("o"));
                AddParam(famCmd, "@id", familyId);
                famCmd.ExecuteNonQuery();

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
```

Then inside `UpdateFamilyMetadata`, change the `catCmd` line from:

```csharp
                using var catCmd = CreateCommand("UPDATE Families SET Category=@cat, IndexedDate=@now, RevitYear=@year, ModifiedDate=@modified, FileSize=@size WHERE Id=@id", transaction);
```

to:

```csharp
                using var catCmd = CreateCommand("UPDATE Families SET Category=@cat, IndexedDate=@now, RevitYear=@year, ModifiedDate=@modified, FileSize=@size, ParametersExtracted=1 WHERE Id=@id", transaction);
```

(No new parameter needed — `1` is a literal.)

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter IndexRepositoryTests`
Expected: PASS (5/5).

- [ ] **Step 8: Commit**

```bash
git add src/RVTuk.Core/Database/IndexRepository.cs tests/RVTuk.Core.Tests/IndexRepositoryTests.cs
git commit -m "feat(index): add per-facet extraction-status queries to IndexRepository"
```

---

### Task 2: `FamilyIndexer.Scan` — split thumbnail/parameter detection

**Files:**
- Modify: `src/RVTuk.Core/Extraction/FamilyIndexer.cs`
- Test: `tests/RVTuk.Core.Tests/FamilyIndexerTests.cs` (rewrite)

**Interfaces:**
- Consumes: `IndexRepository.GetFamilyIdsWithThumbnail()`, `GetFamilyIdsWithParametersExtracted()`, `UpsertFamilyFileInfo(...)`, `UpdateThumbnailOnly(...)` (Task 1), plus existing `InsertFamily`, `GetFamilyByPath`, `GetAllRelativePaths`, `DeleteStaleEntries`, `ThumbnailExtractor.ExtractFromRfa(string)`.
- Produces (used by Task 3):
  - `IReadOnlyList<ExtractionWorkItem> Scan(Action<string,int,int> progressCallback, CancellationToken cancellationToken = default, bool includeThumbnails = false, bool includeParameters = false)`
  - `int ThumbnailOnlyCount { get; }` (alongside existing `SkippedLongPath`, `SkippedIgnored`)

- [ ] **Step 1: Replace the test file with the new signature and scenarios**

Replace the full contents of `tests/RVTuk.Core.Tests/FamilyIndexerTests.cs`:

```csharp
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
        Assert.Equal(1, repo.GetAllRelativePaths().Count);   // file info still synced
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter FamilyIndexerTests`
Expected: build error — `Scan` has no overload taking `includeThumbnails`/`includeParameters`; `ThumbnailOnlyCount` doesn't exist.

- [ ] **Step 3: Rewrite `FamilyIndexer.Scan`**

Replace the full contents of `src/RVTuk.Core/Extraction/FamilyIndexer.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using RVTuk.Core.Database;
using RVTuk.Core.Models;
using RVTuk.Core.Util;

namespace RVTuk.Core.Extraction
{
    public class FamilyIndexer
    {
        private readonly IndexRepository _repository;
        private readonly string _libraryRoot;
        private readonly IReadOnlyList<string> _ignoredSubfolders;

        /// <summary>Families skipped because their full path exceeds Windows MAX_PATH (set by the last Scan).</summary>
        public int SkippedLongPath { get; private set; }

        /// <summary>Families skipped because they live under an ignored subfolder (set by the last Scan).</summary>
        public int SkippedIgnored { get; private set; }

        /// <summary>Families whose thumbnail was committed directly without queuing Revit parameter extraction (set by the last Scan).</summary>
        public int ThumbnailOnlyCount { get; private set; }

        public FamilyIndexer(IndexRepository repository, string libraryRootPath,
            IReadOnlyList<string> ignoredSubfolders = null)
        {
            _repository = repository;
            _libraryRoot = libraryRootPath;
            _ignoredSubfolders = ignoredSubfolders ?? new List<string>();
        }

        /// <summary>
        /// Scans the library folder. <paramref name="includeThumbnails"/> and
        /// <paramref name="includeParameters"/> are independent: a family needs a facet
        /// refreshed if its file changed, or it is simply missing that facet's data. Families
        /// needing only a thumbnail refresh are committed directly (no Revit engine involved);
        /// families needing parameters are returned as work items for Phase 2 (Revit-engine
        /// extraction), carrying an already-extracted thumbnail if that facet was also requested.
        /// Both flags false is a filenames-only sync: add new families, prune deleted ones.
        /// </summary>
        public IReadOnlyList<ExtractionWorkItem> Scan(
            Action<string, int, int> progressCallback,
            CancellationToken cancellationToken = default,
            bool includeThumbnails = false,
            bool includeParameters = false)
        {
            // Robust walk: skips over-long/inaccessible paths instead of aborting the whole scan.
            // Ignored subfolders are pruned from the walk entirely, so their (often huge) file
            // counts never inflate the progress total or slow the scan.
            var rfaFiles = new List<string>(PathUtil.SafeEnumerateFiles(_libraryRoot, "*.rfa",
                dir => PathUtil.IsUnderIgnoredFolder(PathUtil.GetRelativePath(_libraryRoot, dir), _ignoredSubfolders)));
            int total = rfaFiles.Count;
            var workItems = new List<ExtractionWorkItem>();
            var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            SkippedLongPath = 0;
            SkippedIgnored = 0;
            ThumbnailOnlyCount = 0;

            // Preloaded once per scan so the per-file loop never issues an extra query per family.
            var hasThumbnail = includeThumbnails ? _repository.GetFamilyIdsWithThumbnail() : new HashSet<long>();
            var paramsExtracted = includeParameters ? _repository.GetFamilyIdsWithParametersExtracted() : new HashSet<long>();

            // Protect families that were indexed before their folder was ignored: keep their rows
            // (the browser just hides them) instead of pruning them as stale. We don't walk the
            // ignored folders on disk, so we read these straight from the DB. Skipped entirely when
            // nothing is ignored, to avoid an extra full-table read on every scan.
            if (_ignoredSubfolders.Count > 0)
            {
                foreach (var dbPath in _repository.GetAllRelativePaths())
                {
                    if (PathUtil.IsUnderIgnoredFolder(dbPath, _ignoredSubfolders))
                    {
                        scannedPaths.Add(dbPath);
                        SkippedIgnored++;
                    }
                }
            }

            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string fullPath = rfaFiles[i];

                // A family whose full path exceeds Windows MAX_PATH cannot be opened by Revit on
                // .NET Framework; skip it so it neither aborts the scan nor triggers Revit's
                // "path too long" dialog during metadata extraction.
                if (fullPath.Length >= 260)
                {
                    SkippedLongPath++;
                    progressCallback(Path.GetFileName(fullPath), i + 1, total);
                    continue;
                }

                string relativePath = PathUtil.GetRelativePath(_libraryRoot, fullPath);
                string fileName = Path.GetFileName(fullPath);

                progressCallback(fileName, i + 1, total);

                scannedPaths.Add(relativePath);

                var info = new FileInfo(fullPath);
                long fileSize = info.Length;
                DateTime modifiedDate = info.LastWriteTimeUtc;

                var existing = _repository.GetFamilyByPath(relativePath);

                bool fileChanged = existing == null
                    || existing.FileSize != fileSize
                    || Math.Abs((existing.ModifiedDate - modifiedDate).TotalSeconds) > 1;

                bool needsThumbnail = includeThumbnails
                    && (fileChanged || existing == null || !hasThumbnail.Contains(existing.Id));
                bool needsParameters = includeParameters
                    && (fileChanged || existing == null || !paramsExtracted.Contains(existing.Id));

                if (!needsThumbnail && !needsParameters)
                {
                    // Filenames-only sync (also the "neither checkbox" case): keep the row's
                    // name/size/date current with no extraction. Real values written directly —
                    // there is nothing to extract afterward, so no sentinel/resumability dance.
                    _repository.UpsertFamilyFileInfo(relativePath, fileName, fileSize, modifiedDate);
                    continue;
                }

                if (needsParameters)
                {
                    // Create/keep the row (preserving its Id and curated data) but do NOT store the
                    // real size/date yet — InsertFamily writes a sentinel for new rows and leaves an
                    // existing row's old size/date untouched. The real values ride on the work item
                    // and are committed only once extraction succeeds (UpdateFamilyMetadata). That
                    // way a cancelled family still looks stale and is re-scanned next time.
                    long familyId = _repository.InsertFamily(relativePath, fileName);

                    byte[] thumbnail = null;
                    int revitYear = 0;
                    if (needsThumbnail)
                        (thumbnail, revitYear) = ThumbnailExtractor.ExtractFromRfa(fullPath);

                    workItems.Add(new ExtractionWorkItem
                    {
                        FamilyId = familyId,
                        FullPath = fullPath,
                        RelativePath = relativePath,
                        ThumbnailPng = thumbnail,
                        FileRevitYear = revitYear,
                        ModifiedDate = modifiedDate,
                        FileSize = fileSize
                    });
                }
                else
                {
                    // Needs only a thumbnail refresh: a plain file read, no Revit engine involved,
                    // so commit it directly instead of queuing a Phase-2 work item.
                    var (thumbnail, revitYear) = ThumbnailExtractor.ExtractFromRfa(fullPath);
                    long familyId = existing?.Id ?? _repository.InsertFamily(relativePath, fileName);

                    if (thumbnail != null)
                    {
                        _repository.UpdateThumbnailOnly(familyId, thumbnail, revitYear, modifiedDate, fileSize);
                        ThumbnailOnlyCount++;
                    }
                    else
                    {
                        // Extraction failed (e.g. no embedded preview) — keep name/size/date
                        // current; leave the Thumbnail table untouched so this family is retried
                        // on the next thumbnails-enabled scan.
                        _repository.UpsertFamilyFileInfo(relativePath, fileName, fileSize, modifiedDate);
                    }
                }
            }

            _repository.DeleteStaleEntries(scannedPaths);
            return workItems;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj --filter "FamilyIndexerTests|IndexRepositoryTests"`
Expected: PASS (all).

- [ ] **Step 5: Run the full Core test suite to check for regressions**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj`
Expected: PASS (no regressions in `ComparisonEngineTests`, `SnapshotRepositoryTests`, etc. — none of them touch `FamilyIndexer`/`IndexRepository`).

- [ ] **Step 6: Commit**

```bash
git add src/RVTuk.Core/Extraction/FamilyIndexer.cs tests/RVTuk.Core.Tests/FamilyIndexerTests.cs
git commit -m "feat(index): decouple thumbnail and parameter extraction in FamilyIndexer.Scan"
```

---

### Task 3: Revit + UI command plumbing — rename `RunDeepScan`, single scan delegate, unify `ConfigViewModel`

These three files are mutually dependent and cannot compile individually — `OpenConfigCommand`
calls both `IndexLibraryCommand.RunScan` and `ConfigViewModel`'s constructor with its new shape —
so they land together as one task with a single build-verify + commit at the end.

**Files:**
- Modify: `src/RVTuk.Revit/Commands/IndexLibraryCommand.cs`
- Modify: `src/RVTuk.Revit/Commands/OpenConfigCommand.cs`
- Modify: `src/LibraryBrowser/RVTuk.UI/ViewModels/ConfigViewModel.cs`

**Interfaces:**
- Consumes: `FamilyIndexer.Scan(progressCallback, cancellationToken, includeThumbnails, includeParameters)`, `FamilyIndexer.ThumbnailOnlyCount` (Task 2).
- Produces (used by Task 4): `ConfigViewModel.ScanThumbnails`/`ScanParameters` (bool properties), `ConfigViewModel.ScanCommand`, `ConfigViewModel.BrowseLibraryCommand` (unchanged), `ConfigViewModel.IsConfigured` (unchanged), `ConfigViewModel.IgnoredSubfoldersText` (unchanged).

Not unit-testable (references the Revit API / WPF windows) — verified by building the solution,
per this project's existing convention (`docs/superpowers/specs/2026-06-30-config-hub-deep-scan-design.md`:
"Core logic is unit-testable; UI/Revit wiring is verified in-Revit").

- [ ] **Step 1: Replace `IndexLibraryCommand.cs`**

Replace the full contents of `src/RVTuk.Revit/Commands/IndexLibraryCommand.cs`:

```csharp
using System;
using System.IO;
using System.Text;
using System.Threading;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.Config;
using RVTuk.Core.Database;
using RVTuk.Core.Extraction;
using RVTuk.Revit.Extraction;
using RVTuk.UI.Views;

namespace RVTuk.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class IndexLibraryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var config = ConfigManager.LoadConfig();
            if (!ConfigManager.IsConfigured(config))
            {
                new SettingsWindow().ShowDialog();
                config = ConfigManager.LoadConfig();
                if (!ConfigManager.IsConfigured(config))
                    return Result.Cancelled;
            }
            RunScan(commandData.Application, config, includeThumbnails: true, includeParameters: true);
            return Result.Succeeded;
        }

        /// <param name="includeThumbnails">
        /// Re-extract thumbnails for families that are new/changed or simply missing one. A plain
        /// file read — never touches Revit's main thread.
        /// </param>
        /// <param name="includeParameters">
        /// Re-extract category/parameters (via the Revit engine — the slow path) for families
        /// that are new/changed or simply missing them.
        /// </param>
        /// <remarks>
        /// Both false is a filenames-only sync: add new families, prune deleted ones, no
        /// extraction. Non-destructive either way — curated data (instructions, tags, favourites,
        /// custom thumbnails, gallery) is always preserved.
        /// </remarks>
        public static void RunScan(UIApplication uiApp, AppConfig config, bool includeThumbnails, bool includeParameters)
        {
            var progressWindow = new IndexProgressWindow();
            var vm = progressWindow.ViewModel;
            var handler = Application.IndexingHandler;
            var externalEvent = Application.IndexingEvent;
            var extractor = new FamilyMetadataExtractor(uiApp.Application);

            progressWindow.Show();
            var cancellationToken = vm.Start();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                int updated = 0;
                int thumbnailOnly = 0;
                int skippedLong = 0;
                int skippedIgnored = 0;
                try
                {
                    using var repo = new IndexRepository(config.DatabasePath);
                    var indexer = new FamilyIndexer(repo, config.LibraryFolderPath, config.IgnoredSubfolders);

                    var workItems = indexer.Scan(
                        (fileName, current, total) => vm.UpdateProgress(fileName, current, total),
                        cancellationToken,
                        includeThumbnails,
                        includeParameters);

                    updated = workItems.Count;
                    thumbnailOnly = indexer.ThumbnailOnlyCount;
                    skippedLong = indexer.SkippedLongPath;
                    skippedIgnored = indexer.SkippedIgnored;

                    // Phase 2 — pull category/parameters from each family via the Revit engine.
                    // Only families needing parameters ever reach here; thumbnails-only and
                    // filenames-only families were already fully handled in Phase 1 above, so a
                    // scan with Update parameters unchecked never touches Revit's main thread.
                    for (int i = 0; i < workItems.Count; i++)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        var item = workItems[i];
                        vm.UpdateProgress(Path.GetFileName(item.FullPath), i + 1, workItems.Count);
                        handler.PrepareAndWait(item, repo, extractor);
                        externalEvent.Raise();
                        handler.WaitForCompletion();
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                        TaskDialog.Show("RVTuk – Error", ex.Message));
                }
                finally
                {
                    vm.Finish();
                    int finalUpdated = updated;
                    int finalThumbnailOnly = thumbnailOnly;
                    int finalSkippedLong = skippedLong;
                    int finalSkippedIgnored = skippedIgnored;

                    WriteScanLog(config, finalUpdated, finalThumbnailOnly, finalSkippedLong, finalSkippedIgnored);

                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        progressWindow.Close();

                        var msg = new StringBuilder();
                        msg.Append($"Indexed: {finalUpdated} families.");
                        if (finalThumbnailOnly > 0)
                            msg.Append($"\nThumbnails updated: {finalThumbnailOnly}");
                        if (finalSkippedLong > 0)
                            msg.Append($"\nSkipped (path too long): {finalSkippedLong}");
                        if (finalSkippedIgnored > 0)
                            msg.Append($"\nSkipped (ignored folder): {finalSkippedIgnored}");

                        TaskDialog.Show("RVTuk – Scan Complete", msg.ToString());
                    });
                }
            });
        }

        /// <summary>
        /// Writes a small last-scan.log next to the database so the admin can see why some
        /// families were skipped. Best-effort: any failure (e.g. read-only share) is swallowed.
        /// </summary>
        private static void WriteScanLog(AppConfig config, int indexed, int thumbnailOnly, int skippedLong, int skippedIgnored)
        {
            try
            {
                string dir = Path.GetDirectoryName(config.DatabasePath);
                if (string.IsNullOrEmpty(dir)) return;

                string logPath = Path.Combine(dir, "last-scan.log");
                var sb = new StringBuilder();
                sb.AppendLine($"Scan finished: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Indexed families:          {indexed}");
                sb.AppendLine($"Thumbnails updated:        {thumbnailOnly}");
                sb.AppendLine($"Skipped (path too long):   {skippedLong}");
                sb.AppendLine($"Skipped (ignored folder):  {skippedIgnored}");
                File.WriteAllText(logPath, sb.ToString());
            }
            catch
            {
                // Logging is best-effort; never let it break the scan result dialog.
            }
        }
    }
}
```

- [ ] **Step 2: Replace `OpenConfigCommand.cs`**

Replace the full contents of `src/RVTuk.Revit/Commands/OpenConfigCommand.cs`:

```csharp
using System;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RVTuk.Core.Config;
using RVTuk.UI.ViewModels;
using RVTuk.UI.Views;

namespace RVTuk.Revit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class OpenConfigCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Revit hosts the CLR but never creates a WPF Application; create one we own that
            // never auto-shuts-down, so closing a window can't take Revit down. (Same reasoning
            // as BrowseLibraryCommand.)
            if (System.Windows.Application.Current == null)
            {
                new System.Windows.Application
                {
                    ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
                };
            }

            // Single-instance: bring an open Config window to front instead of opening another.
            if (Application.ConfigWindow != null && Application.ConfigWindow.IsLoaded)
            {
                Application.ConfigWindow.Activate();
                return Result.Succeeded;
            }

            var config = ConfigManager.LoadConfig();
            var uiApp = commandData.Application;

            // The scan needs the Revit UIApplication for metadata extraction. Reload config at
            // click time so a folder / ignored-list change made in the window is honoured.
            Action<bool, bool> scan = (includeThumbnails, includeParameters) =>
                IndexLibraryCommand.RunScan(uiApp, ConfigManager.LoadConfig(), includeThumbnails, includeParameters);

            // If the Family Browser is open, refresh it after a library-folder change so it does
            // not keep showing the old library.
            Action onLibraryFolderChanged = () =>
            {
                try { Application.BrowserWindow?.ReloadConfig(); } catch { /* refresh is best-effort */ }
            };

            var crashLogPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RVTuk", "crash.log");

            try
            {
                var vm = new ConfigViewModel(config, scan, onLibraryFolderChanged);
                var window = new ConfigWindow(vm);
                window.Closed += (s, e) => Application.ConfigWindow = null;
                Application.ConfigWindow = window; // set before Show() so re-entry finds it
                window.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(crashLogPath)!);
                    File.AppendAllText(crashLogPath,
                        $"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] OpenConfig: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}\n");
                }
                catch { /* logging must never itself crash */ }

                TaskDialog.Show("RVTuk – Config",
                    $"Failed to open Config:\n\n{ex.GetType().Name}: {ex.Message}");
                return Result.Failed;
            }
        }
    }
}
```

- [ ] **Step 3: Replace `ConfigViewModel.cs`**

Replace the full contents of `src/LibraryBrowser/RVTuk.UI/ViewModels/ConfigViewModel.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using RVTuk.Core.Config;

namespace RVTuk.UI.ViewModels
{
    /// <summary>
    /// View model for the ribbon-launched Config hub. Today it hosts the Family Library
    /// settings (library root, scan, ignored subfolders) that previously lived in an
    /// inline panel inside the Family Browser. Built as a hub so other tools' settings can be
    /// added as further tabs later.
    /// </summary>
    public class ConfigViewModel : ViewModelBase
    {
        private readonly AppConfig _config;
        private readonly Action<bool, bool> _scan;
        private readonly Action? _onLibraryFolderChanged;

        private string? _ignoredSubfoldersText;
        private bool _scanThumbnails;
        private bool _scanParameters;

        /// <param name="scan">
        /// Runs a scan. Args are (includeThumbnails, includeParameters); both false means a
        /// filenames-only sync (add new families, prune deleted ones, no extraction).
        /// </param>
        /// <param name="onLibraryFolderChanged">
        /// Invoked after the library folder is changed + saved, so the host can refresh an open
        /// Family Browser. Optional — kept as a delegate so this UI project takes no Revit/window
        /// dependency.
        /// </param>
        public ConfigViewModel(
            AppConfig config,
            Action<bool, bool> scan,
            Action? onLibraryFolderChanged = null)
        {
            _config = config;
            _scan = scan;
            _onLibraryFolderChanged = onLibraryFolderChanged;

            BrowseLibraryCommand = new RelayCommand(BrowseLibraryFolder);
            ScanCommand = new RelayCommand(() => _scan(ScanThumbnails, ScanParameters), () => IsConfigured);
        }

        public string LibraryFolderPath
        {
            get => _config.LibraryFolderPath;
            private set
            {
                if (_config.LibraryFolderPath == value) return;
                _config.LibraryFolderPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DerivedDatabasePath));
            }
        }

        public string DerivedDatabasePath =>
            string.IsNullOrWhiteSpace(_config.LibraryFolderPath)
                ? string.Empty
                : _config.DatabasePath;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_config.LibraryFolderPath);

        public bool ScanThumbnails
        {
            get => _scanThumbnails;
            set => SetProperty(ref _scanThumbnails, value);
        }

        public bool ScanParameters
        {
            get => _scanParameters;
            set => SetProperty(ref _scanParameters, value);
        }

        public string IgnoredSubfoldersText
        {
            get => _ignoredSubfoldersText ?? string.Join(Environment.NewLine, _config.IgnoredSubfolders);
            set
            {
                if (Equals(_ignoredSubfoldersText, value)) return;
                _ignoredSubfoldersText = value;
                _config.IgnoredSubfolders = (value ?? string.Empty)
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
                ConfigManager.SaveConfig(_config);
                OnPropertyChanged();
            }
        }

        public ICommand BrowseLibraryCommand { get; }
        public ICommand ScanCommand { get; }

        private void BrowseLibraryFolder()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the root folder containing your Revit families",
                SelectedPath = Directory.Exists(LibraryFolderPath) ? LibraryFolderPath : string.Empty
            };
            if (dialog.ShowDialog() != DialogResult.OK) return;

            var error = LibraryFolderValidator.Validate(dialog.SelectedPath);
            if (error != null)
            {
                System.Windows.MessageBox.Show(error, "RVTuk – Config",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LibraryFolderPath = dialog.SelectedPath;
            Directory.CreateDirectory(Path.Combine(dialog.SelectedPath, ".Setup"));
            ConfigManager.SaveConfig(_config);
            CommandManager.InvalidateRequerySuggested(); // re-enable the Scan button now a folder is set
            _onLibraryFolderChanged?.Invoke();
        }
    }
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2025`
Expected: build succeeds (`ConfigWindow.xaml` still references the old `ScanNewCommand`/`RescanAllCommand`
bindings, but WPF bindings are resolved at runtime, not compile time, so this still compiles — it
just won't be visually correct until Task 4).

- [ ] **Step 5: Commit**

```bash
git add src/RVTuk.Revit/Commands/IndexLibraryCommand.cs src/RVTuk.Revit/Commands/OpenConfigCommand.cs src/LibraryBrowser/RVTuk.UI/ViewModels/ConfigViewModel.cs
git commit -m "feat(config): rename RunDeepScan to RunScan and unify scan into one delegate + ScanCommand"
```

---

### Task 4: `ConfigWindow.xaml` — one Scan button + two checkboxes

**Files:**
- Modify: `src/LibraryBrowser/RVTuk.UI/Views/ConfigWindow.xaml`

**Interfaces:**
- Consumes: `ConfigViewModel.ScanThumbnails`, `ScanParameters`, `ScanCommand` (Task 3).

- [ ] **Step 1: Replace the DEEP SCAN section**

In `src/LibraryBrowser/RVTuk.UI/Views/ConfigWindow.xaml`, replace lines 46-68 (from `<TextBlock Text="DEEP SCAN"` through the closing `</StackPanel>` of the button row) with:

```xml
                    <TextBlock Text="SCAN" Foreground="{StaticResource Brush.TextMuted}"
                               FontSize="10" FontWeight="SemiBold" Margin="0,0,0,8"/>
                    <TextBlock FontSize="12" TextWrapping="Wrap" Margin="0,0,0,14"
                               Foreground="{StaticResource Brush.Text}">
                        Scan always adds new families and removes ones whose files are gone.
                        Check a box to also refresh that data for anything new, changed, or
                        currently missing it — nothing else is touched.
                        <LineBreak/><LineBreak/>
                        <Run FontWeight="SemiBold">Update thumbnails</Run> re-reads each family's
                        preview image — a fast, plain file read.
                        <LineBreak/>
                        <Run FontWeight="SemiBold">Update parameters</Run> re-reads category and
                        parameters using the Revit engine — slower.
                        <LineBreak/><LineBreak/>
                        Your instructions, pictures, custom thumbnails, tags, and favourites are
                        always kept.
                    </TextBlock>
                    <CheckBox Content="Update thumbnails" Margin="0,0,0,8"
                              IsChecked="{Binding ScanThumbnails}"/>
                    <CheckBox Content="Update parameters (slower)" Margin="0,0,0,14"
                              IsChecked="{Binding ScanParameters}"/>
                    <Button Command="{Binding ScanCommand}"
                            HorizontalAlignment="Left"
                            Padding="14,7" Background="{StaticResource Brush.AccentDark}"
                            BorderBrush="{StaticResource Brush.Accent}"
                            Content="Scan"/>
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/RVTuk.Revit/RVTuk.Revit.csproj -c Release2025`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/LibraryBrowser/RVTuk.UI/Views/ConfigWindow.xaml
git commit -m "feat(config): replace two scan buttons with one Scan button + two checkboxes"
```

---

### Task 5: Full solution verification + backlog update

**Files:**
- Modify: `docs/BACKLOG.md`

- [ ] **Step 1: Build both configs**

Run: `dotnet build RVTuk.sln -c Release2024`
Expected: 0 errors.

Run: `dotnet build RVTuk.sln -c Release2025`
Expected: 0 errors.

- [ ] **Step 2: Run the full Core test suite**

Run: `dotnet test tests/RVTuk.Core.Tests/RVTuk.Core.Tests.csproj`
Expected: all tests PASS.

- [ ] **Step 3: Update `docs/BACKLOG.md`**

In the "🐞 Bugs / things to fix" → "✨ Improvements" section (around line 61-72), replace:

```markdown
- [ ] **Deep scan is slow** — it opens every family in Revit to read parameters
  (which upgrades older families to the running version in memory), so a first
  full scan takes a long time. ETA now shown (`87fa3e0`); still want: make it
  resumable, or a faster path for families that don't need full metadata.
- [x] When deep scanning, on **Cancel** keep everything already extracted so the time
  isn't lost (`57c9ceb`). New/changed rows now carry a sentinel size/date; the real
  ModifiedDate+FileSize ride on `ExtractionWorkItem` and `UpdateFamilyMetadata` writes them
  only after extraction commits, so a cancelled family stays stale and is re-scanned next
  time. ⚠️ **Still open (separate):** the fast Sync's `UpsertFamily` writes real size/date
  with no metadata, so a Sync-then-DeepScan skips those families — decide whether Sync should
  mark rows "needs deep scan" too. *(Verify the cancel/resume end-to-end in Revit.)*
- [ ] Option to deep-scan **just thumbnails** (fast) and/or **just parameters** (slow).
```

with:

```markdown
- [ ] **Deep scan is slow** — it opens every family in Revit to read parameters
  (which upgrades older families to the running version in memory), so a first
  full scan takes a long time. ETA now shown (`87fa3e0`); resumable (`57c9ceb`);
  now also fixed for the "opens every family" part when only thumbnails are needed
  (see below) — still no chunked/background resumability across app restarts.
- [x] When deep scanning, on **Cancel** keep everything already extracted so the time
  isn't lost (`57c9ceb`). New/changed rows now carry a sentinel size/date; the real
  ModifiedDate+FileSize ride on `ExtractionWorkItem` and `UpdateFamilyMetadata` writes them
  only after extraction commits, so a cancelled family stays stale and is re-scanned next
  time. *(Verify the cancel/resume end-to-end in Revit.)*
- [x] Option to deep-scan **just thumbnails** (fast) and/or **just parameters** (slow), and fix
  the fast Sync's families being invisible to the deep scan forever — replaced the two Config
  scan buttons with one **Scan** button + **Update thumbnails** / **Update parameters**
  checkboxes; each facet's "needs refresh" now also fires when that family is simply missing
  the data (not just when the file changed), so a Sync-only family gets picked up on the next
  facet scan. See `docs/superpowers/specs/2026-07-01-scan-checkboxes-design.md`.
```

- [ ] **Step 4: Commit**

```bash
git add docs/BACKLOG.md
git commit -m "docs: mark scan-checkboxes backlog items done"
```

## Post-implementation (manual, in Revit — not automatable here)

- Deploy to Revit 2024 or 2025 (`.\Deploy.ps1 2024` or `2025`, elevated), open Config: confirm one
  Scan button + two checkboxes replace the old two buttons.
- Scan with neither checked on a library with new/removed files: confirm files are added/removed
  from the Family Browser list with no "processing family" Revit delay.
- Check **Update thumbnails** only: confirm previews refresh without any Revit per-family delay.
- Check **Update parameters** only: confirm category/parameters refresh (slow path) without
  affecting existing thumbnails.
- Sync a brand-new family via the Family Browser's own **Sync** button first, then run a
  Config **Scan** with both boxes checked: confirm that family's thumbnail and parameters get
  extracted (this is the regression the whole change targets).
