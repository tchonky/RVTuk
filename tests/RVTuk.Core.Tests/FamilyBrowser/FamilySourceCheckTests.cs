using System;
using RVTuk.Core.FamilyBrowser.Models;
using RVTuk.Core.FamilyBrowser.Util;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser;

// Loaded copy vs library file: _Version first, then the size and date RVTuk noted when it
// loaded the family, and "unknown" — never a false ✓ — when neither can tell.
public class FamilySourceCheckTests
{
    private static readonly DateTime Saved = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

    private static ProjectFamilyInfo Loaded(string? version = null, long? size = null, DateTime? modified = null) =>
        new() { Name = "Door", Version = version, LoadedFileSize = size, LoadedFileModifiedUtc = modified };

    [Fact]
    public void NotedLoad_SameFile_IsUpToDate() =>
        Assert.Equal(VersionStatus.UpToDate, FamilySourceCheck.Judge(null, Loaded(size: 1000, modified: Saved), 1000, Saved));

    [Fact]
    public void NotedLoad_FileChangedSince_IsAnUpdate_EvenWithoutVersion() =>
        Assert.Equal(VersionStatus.UpdateAvailable,
            FamilySourceCheck.Judge(null, Loaded(size: 1000, modified: Saved), 1000, Saved.AddHours(3)));

    [Fact]
    public void NotedLoad_SizeChanged_IsAnUpdate() =>
        Assert.Equal(VersionStatus.UpdateAvailable,
            FamilySourceCheck.Judge(null, Loaded(size: 1000, modified: Saved), 1200, Saved));

    [Fact]
    public void NotedLoad_FileChanged_ButVersionNotBumped_IsStillAnUpdate() =>
        // The case that showed a false ✓: same _Version, different file.
        Assert.Equal(VersionStatus.UpdateAvailable,
            FamilySourceCheck.Judge("1.3", Loaded("1.3", 1000, Saved), 1400, Saved.AddDays(1)));

    [Fact]
    public void NewerLibraryVersion_IsAnUpdate_EvenIfTheFileLooksTheSame() =>
        Assert.Equal(VersionStatus.UpdateAvailable,
            FamilySourceCheck.Judge("1.4", Loaded("1.3", 1000, Saved), 1000, Saved));

    [Fact]
    public void FileTimesRoundedBySync_StillMatch() =>
        Assert.Equal(VersionStatus.UpToDate,
            FamilySourceCheck.Judge(null, Loaded(size: 1000, modified: Saved), 1000, Saved.AddSeconds(1.5)));

    [Fact]
    public void LocalAndUtcTimes_AreTheSameInstant() =>
        Assert.True(FamilySourceCheck.SameFile(1000, Saved.ToLocalTime(), 1000, Saved));

    [Fact]
    public void NothingNoted_MatchingVersions_IsUpToDate() =>
        Assert.Equal(VersionStatus.UpToDate, FamilySourceCheck.Judge("1.3", Loaded("1.3"), 1000, Saved));

    [Theory]
    [InlineData(null, null)]
    [InlineData("1.3", null)]
    [InlineData(null, "1.3")]
    public void NothingNoted_NoVersionToCompare_IsUnknown(string? library, string? project) =>
        Assert.Equal(VersionStatus.Unknown, FamilySourceCheck.Judge(library, Loaded(project), 1000, Saved));

    [Fact]
    public void NotedLoad_ButLibraryFileUnreadable_FallsBackToTheVersion()
    {
        Assert.Equal(VersionStatus.UpToDate, FamilySourceCheck.Judge("1.3", Loaded("1.3", 1000, Saved), null, null));
        Assert.Equal(VersionStatus.Unknown, FamilySourceCheck.Judge(null, Loaded(null, 1000, Saved), null, null));
    }

    [Fact]
    public void UnknownCountsAsLoadedInTheProject_ForTheProjectTag() =>
        Assert.Contains(BuiltInTags.Project, FamilyFilter.TagsOf(null, VersionStatus.Unknown, false));
}
