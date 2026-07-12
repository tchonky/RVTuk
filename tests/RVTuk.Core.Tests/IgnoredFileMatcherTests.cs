using System.Collections.Generic;
using RVTuk.Core.Config;
using RVTuk.Core.Util;
using Xunit;

namespace RVTuk.Core.Tests;

public class IgnoredFileMatcherTests
{
    // The pattern AppConfig seeds by default — the Revit backup-file convention.
    private static IgnoredFileMatcher DefaultMatcher() => new(new AppConfig().IgnoredFilePatterns);

    [Theory]
    [InlineData("Door.0001.rfa")]
    [InlineData("Door.0123.rfa")]
    [InlineData("My Family.0002.RFA")]   // case-insensitive
    public void DefaultPattern_MatchesRevitBackupFiles(string fileName)
    {
        Assert.True(DefaultMatcher().IsIgnored(fileName));
    }

    [Theory]
    [InlineData("Door.rfa")]
    [InlineData("Door.001.rfa")]     // three digits — not the backup convention
    [InlineData("Door.00001.rfa")]   // five digits — .0001. never appears as a segment
    [InlineData("Type 0001 Door.rfa")]
    public void DefaultPattern_KeepsRegularFamilies(string fileName)
    {
        Assert.False(DefaultMatcher().IsIgnored(fileName));
    }

    [Fact]
    public void NullOrEmptyPatterns_IgnoreNothing()
    {
        Assert.False(new IgnoredFileMatcher(null).IsIgnored("Door.0001.rfa"));
        Assert.False(new IgnoredFileMatcher(new List<string>()).IsIgnored("Door.0001.rfa"));
        Assert.False(new IgnoredFileMatcher(new List<string> { "  ", "" }).IsIgnored("Door.0001.rfa"));
        Assert.False(new IgnoredFileMatcher(new List<string>()).HasPatterns);
    }

    [Fact]
    public void InvalidPattern_IsSkipped_ValidOnesStillApply()
    {
        var matcher = new IgnoredFileMatcher(new List<string> { "([unclosed", "^old_" });

        Assert.True(matcher.IsIgnored("old_door.rfa"));    // the valid pattern still works
        Assert.False(matcher.IsIgnored("Door.rfa"));
    }
}
