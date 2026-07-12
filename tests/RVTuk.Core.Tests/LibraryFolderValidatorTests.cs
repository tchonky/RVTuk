using System;
using System.IO;
using RVTuk.Core.Config;
using Xunit;

namespace RVTuk.Core.Tests;

public class LibraryFolderValidatorTests : IDisposable
{
    private readonly string _root;

    public LibraryFolderValidatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rvtuk_lib_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    [Fact]
    public void EmptyPath_IsRejected()
    {
        Assert.NotNull(LibraryFolderValidator.Validate(""));
    }

    [Fact]
    public void NonexistentFolder_IsRejected()
    {
        Assert.NotNull(LibraryFolderValidator.Validate(Path.Combine(_root, "does-not-exist")));
    }

    [Fact]
    public void WritableFolderWithoutDatabase_IsAccepted()
    {
        Directory.CreateDirectory(_root);
        Assert.Null(LibraryFolderValidator.Validate(_root));
    }

    [Fact]
    public void FolderWithExistingDatabase_IsAccepted()
    {
        var dbDir = Path.Combine(_root, AppConfig.DbFolderName);
        Directory.CreateDirectory(dbDir);
        File.WriteAllText(Path.Combine(dbDir, "RVTuk.db"), "");

        Assert.Null(LibraryFolderValidator.Validate(_root));
    }

    [Fact]
    public void FolderWithDatabaseInLegacySetupFolder_IsAccepted()
    {
        // Pre-rename libraries still have their DB in ".Setup"; it is migrated on first use,
        // so the validator must treat it as an existing database.
        var legacy = Path.Combine(_root, AppConfig.LegacyDbFolderName);
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "RVTuk.db"), "");

        Assert.Null(LibraryFolderValidator.Validate(_root));
    }

    [Fact]
    public void Validate_DoesNotLeaveProbeArtifacts()
    {
        Directory.CreateDirectory(_root);
        LibraryFolderValidator.Validate(_root);

        Assert.Empty(Directory.GetFiles(_root));
    }
}
