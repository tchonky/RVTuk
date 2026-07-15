using System;
using System.IO;
using RVTuk.Core.Shared.Config;
using Xunit;

namespace RVTuk.Core.Tests.Shared;

public class AppConfigTests : IDisposable
{
    private readonly string _root;

    public AppConfigTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rvtuk_cfg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    [Fact]
    public void DerivedPaths_UseDbFolder()
    {
        var config = new AppConfig { LibraryFolderPath = _root };

        Assert.Equal(Path.Combine(_root, ".DB", "RVTuk.db"), config.DatabasePath);
    }

    [Fact]
    public void MigrateLegacyDbFolder_RenamesSetupToDb_WithContents()
    {
        // The sidecar file stands in for anything else living beside the DB in old installs
        // (e.g. the standards DB that pre-split comparator builds created) — the whole folder
        // moves, not just RVTuk.db.
        var legacy = Path.Combine(_root, AppConfig.LegacyDbFolderName);
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "RVTuk.db"), "families");
        File.WriteAllText(Path.Combine(legacy, "RVTuk.Standards.db"), "standards");

        AppConfig.MigrateLegacyDbFolder(_root);

        var dbDir = Path.Combine(_root, AppConfig.DbFolderName);
        Assert.False(Directory.Exists(legacy));
        Assert.Equal("families", File.ReadAllText(Path.Combine(dbDir, "RVTuk.db")));
        Assert.Equal("standards", File.ReadAllText(Path.Combine(dbDir, "RVTuk.Standards.db")));
    }

    [Fact]
    public void MigrateLegacyDbFolder_NoOp_WhenDbFolderAlreadyExists()
    {
        // Both folders present (e.g. a fresh .DB was created before the legacy one was noticed):
        // never overwrite or merge — leave both untouched.
        var legacy = Path.Combine(_root, AppConfig.LegacyDbFolderName);
        var dbDir = Path.Combine(_root, AppConfig.DbFolderName);
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(dbDir);
        File.WriteAllText(Path.Combine(legacy, "RVTuk.db"), "old");
        File.WriteAllText(Path.Combine(dbDir, "RVTuk.db"), "new");

        AppConfig.MigrateLegacyDbFolder(_root);

        Assert.Equal("old", File.ReadAllText(Path.Combine(legacy, "RVTuk.db")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(dbDir, "RVTuk.db")));
    }

    [Fact]
    public void MigrateLegacyDbFolder_NoOp_WhenNothingToMigrate()
    {
        AppConfig.MigrateLegacyDbFolder(_root);
        Assert.False(Directory.Exists(Path.Combine(_root, AppConfig.DbFolderName)));

        // Bad inputs must never throw — callers run this best-effort on every entry point.
        AppConfig.MigrateLegacyDbFolder("");
        AppConfig.MigrateLegacyDbFolder(null!);
        AppConfig.MigrateLegacyDbFolder(Path.Combine(_root, "does-not-exist"));
    }

    [Fact]
    public void IgnoredFilePatterns_DefaultsToRevitBackupPattern()
    {
        Assert.Contains(@".*\.\d{4}\.rfa", new AppConfig().IgnoredFilePatterns);
    }

    [Fact]
    public void DwgExportSettings_DefaultEmpty_AndRoundTripThroughJson()
    {
        var fresh = new AppConfig();
        Assert.Equal("", fresh.DwgExportFolder);
        Assert.Equal("", fresh.DwgExportPdfSetupName);
        Assert.Equal("", fresh.DwgExportDwgSetupName);
        Assert.Equal("", fresh.DwgExportSheetSetName);
        Assert.False(fresh.DwgExportUseCurrentWindow);

        // The test project runs on net8, so this exercises the same serializer branch
        // ConfigManager uses there; net48's DataContractJsonSerializer handles plain
        // get/set string/bool properties identically.
        var config = new AppConfig
        {
            DwgExportFolder = @"D:\out",
            DwgExportPdfSetupName = "KKarc - Sheets (No Revision)",
            DwgExportDwgSetupName = "KKarc standard DWG",
            DwgExportSheetSetName = "Sheets for Publish",
            DwgExportUseCurrentWindow = true,
        };
        var json = System.Text.Json.JsonSerializer.Serialize(config);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json)!;

        Assert.Equal(@"D:\out", loaded.DwgExportFolder);
        Assert.Equal("KKarc - Sheets (No Revision)", loaded.DwgExportPdfSetupName);
        Assert.Equal("KKarc standard DWG", loaded.DwgExportDwgSetupName);
        Assert.Equal("Sheets for Publish", loaded.DwgExportSheetSetName);
        Assert.True(loaded.DwgExportUseCurrentWindow);
    }
}
