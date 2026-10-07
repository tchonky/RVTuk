using RVTuk.Core.FamilyBrowser.Util;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser;

public class FamilyVersionCheckTests
{
    [Theory]
    // Either side missing the _Version parameter → check is skipped, never "update available".
    [InlineData(null, null, false)]
    [InlineData("2", null, false)]
    [InlineData(null, "2", false)]
    [InlineData("  ", "2", false)]
    [InlineData("2", "", false)]
    // Equal values (whitespace/case-insensitive) → up to date.
    [InlineData("2", "2", false)]
    [InlineData(" 2 ", "2", false)]
    [InlineData("v2", "V2", false)]
    // Numeric values compare numerically: only a HIGHER library version is an update.
    [InlineData("3", "2", true)]
    [InlineData("10", "9", true)]   // would be false under string comparison
    [InlineData("2", "3", false)]   // project ahead of library is not an update
    [InlineData("2.1", "2.0", true)]
    [InlineData("1.10", "1.9", true)]  // dotted parts compare as integers, not as decimals
    [InlineData("1.9", "1.10", false)]
    // Non-numeric values: any difference means the library copy differs → update.
    [InlineData("B", "A", true)]
    [InlineData("A", "B", true)]
    public void IsUpdateAvailable(string? libraryVersion, string? projectVersion, bool expected)
    {
        Assert.Equal(expected, FamilyVersionCheck.IsUpdateAvailable(libraryVersion, projectVersion));
    }
}
