using System;
using System.IO;
using RVTuk.Core.FamilyBrowser.Database;
using RVTuk.Core.Shared.Config;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser;

// View-only users browse a local copy of the library database, which sits on a cloud-sync
// folder (Desktop Connector). The copy must follow the source, and an unreadable or damaged
// source must never replace a good copy.
public class LibraryCacheTests : IDisposable
{
    private readonly string _root;
    private readonly string _source;
    private readonly string _cacheDir;

    public LibraryCacheTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rvtuk_cache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "lib", ".DB"));
        _source = Path.Combine(_root, "lib", ".DB", "RVTuk.db");
        _cacheDir = Path.Combine(_root, "cache");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_source)) File.SetAttributes(_source, FileAttributes.Normal);
            Directory.Delete(_root, true);
        }
        catch { /* best effort */ }
    }

    private void WriteSource(params string[] families)
    {
        using var repo = new IndexRepository(_source);
        foreach (var f in families) repo.InsertFamily("Doors/" + f, f);
    }

    private static int FamilyCount(string db)
    {
        using var repo = new BrowserRepository(db);
        return repo.GetAllFamilies().Count;
    }

    [Fact]
    public void FirstRefresh_CopiesTheSource()
    {
        WriteSource("A.rfa", "B.rfa");

        var r = LibraryCache.Refresh(_source, _cacheDir);

        Assert.Equal(CacheRefreshStatus.Updated, r.Status);
        Assert.Null(r.Error);
        Assert.Equal(2, FamilyCount(r.DatabasePath!));
        Assert.Equal(new FileInfo(_source).LastWriteTimeUtc, r.CopyOfUtc);
    }

    [Fact]
    public void UnchangedSource_IsNotCopiedAgain()
    {
        WriteSource("A.rfa");
        var first = LibraryCache.Refresh(_source, _cacheDir);

        var second = LibraryCache.Refresh(_source, _cacheDir);

        Assert.Equal(CacheRefreshStatus.UpToDate, second.Status);
        Assert.Equal(first.DatabasePath, second.DatabasePath);
    }

    [Fact]
    public void ChangedSource_GivesANewCopy_AndRemovesTheOldOne()
    {
        WriteSource("A.rfa");
        var first = LibraryCache.Refresh(_source, _cacheDir);
        WriteSource("B.rfa");
        File.SetLastWriteTimeUtc(_source, DateTime.UtcNow.AddMinutes(1)); // coarse clocks

        var second = LibraryCache.Refresh(_source, _cacheDir);

        Assert.Equal(CacheRefreshStatus.Updated, second.Status);
        Assert.NotEqual(first.DatabasePath, second.DatabasePath);
        Assert.Equal(2, FamilyCount(second.DatabasePath!));
        Assert.False(File.Exists(first.DatabasePath));
        Assert.Equal(second.DatabasePath, LibraryCache.Current(_cacheDir).DatabasePath);
    }

    [Fact]
    public void ACopyStillOpen_DoesNotBlockANewerOne()
    {
        WriteSource("A.rfa");
        var first = LibraryCache.Refresh(_source, _cacheDir);
        using var openInAnotherRevit = new BrowserRepository(first.DatabasePath!);
        WriteSource("B.rfa");
        File.SetLastWriteTimeUtc(_source, DateTime.UtcNow.AddMinutes(1));

        var second = LibraryCache.Refresh(_source, _cacheDir);

        Assert.Equal(CacheRefreshStatus.Updated, second.Status);
        Assert.Equal(2, FamilyCount(second.DatabasePath!));
    }

    [Fact]
    public void MissingSource_KeepsTheLastCopy()
    {
        WriteSource("A.rfa");
        var first = LibraryCache.Refresh(_source, _cacheDir);
        File.Delete(_source);

        var r = LibraryCache.Refresh(_source, _cacheDir);

        Assert.Equal(CacheRefreshStatus.Stale, r.Status);
        Assert.Equal(first.DatabasePath, r.DatabasePath);
        Assert.NotNull(r.Error);
        Assert.Equal(1, FamilyCount(r.DatabasePath!));
    }

    [Fact]
    public void DamagedSource_NeverReplacesAGoodCopy()
    {
        WriteSource("A.rfa");
        var first = LibraryCache.Refresh(_source, _cacheDir);
        // A half-synced file: the right header, the rest missing.
        var bytes = File.ReadAllBytes(_source);
        File.WriteAllBytes(_source, bytes[..(bytes.Length / 3)]);
        File.SetLastWriteTimeUtc(_source, DateTime.UtcNow.AddMinutes(1));

        var r = LibraryCache.Refresh(_source, _cacheDir);

        Assert.Equal(CacheRefreshStatus.Stale, r.Status);
        Assert.Equal(first.DatabasePath, r.DatabasePath);
        Assert.Equal(1, FamilyCount(r.DatabasePath!));
    }

    [Fact]
    public void NoSourceAndNoCopy_IsUnavailable()
    {
        var r = LibraryCache.Refresh(_source, _cacheDir);

        Assert.Equal(CacheRefreshStatus.Unavailable, r.Status);
        Assert.Null(r.DatabasePath);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void SourceOpenElsewhere_CanStillBeCopied()
    {
        WriteSource("A.rfa");
        using var heldBySyncClient = new FileStream(_source, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var r = LibraryCache.Refresh(_source, _cacheDir);

        Assert.Equal(CacheRefreshStatus.Updated, r.Status);
    }

    [Fact]
    public void CanWrite_FalseForAReadOnlyDatabase()
    {
        WriteSource("A.rfa");
        Assert.True(LibraryCache.CanWrite(_source));

        File.SetAttributes(_source, FileAttributes.ReadOnly);

        Assert.False(LibraryCache.CanWrite(_source));
    }

    [Fact]
    public void CanWrite_NewLibraryWithoutDbFolder_ProbesTheLibraryFolder()
    {
        var lib = Path.Combine(_root, "newlib");
        Directory.CreateDirectory(lib);

        Assert.True(LibraryCache.CanWrite(Path.Combine(lib, ".DB", "RVTuk.db")));
        Assert.Empty(Directory.GetFiles(lib)); // the probe leaves nothing behind
    }

    [Fact]
    public void LocalLibraryFolder_IsStablePerLibrary_AndIgnoresCaseAndTrailingSlash()
    {
        var a = AppConfig.GetLocalLibraryFolder("base", @"C:\Lib\Families");
        var b = AppConfig.GetLocalLibraryFolder("base", @"c:\lib\families\");
        var c = AppConfig.GetLocalLibraryFolder("base", @"C:\Lib\Other");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
