using RVTuk.Core.AreaSubmission;
using Xunit;

namespace RVTuk.Core.Tests.AreaSubmission;

public class UsageCodeParserTests
{
    [Theory]
    [InlineData("103", 103)]
    [InlineData(" 103 ", 103)]
    [InlineData("103.0", 103)]
    [InlineData("103.00", 103)]
    [InlineData("‏103", 103)]           // RLM prefix (Revit text params carry these)
    [InlineData("‎115‎", 115)]     // LRM wrapped
    [InlineData(" 301 ", 301)]     // non-breaking spaces
    public void Resolve_NumericText_ParsesDespiteJunk(string text, int expected)
    {
        Assert.Equal(expected, UsageCodeParser.Resolve(text, requireCatalog: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("‏")]      // only a bidi mark
    [InlineData("0")]           // Revit's never-filled default — unset, not code 0
    [InlineData("0.0")]
    [InlineData("103.4")]       // non-integral decimal is garbage, not a code
    [InlineData("1,103")]       // thousands separators rejected (merge-style junk)
    [InlineData("115-103")]     // composite text must not silently become a code
    public void Resolve_JunkOrEmpty_IsNull(string? text)
    {
        Assert.Null(UsageCodeParser.Resolve(text, requireCatalog: false));
        Assert.Null(UsageCodeParser.Resolve(text, requireCatalog: true));
    }

    [Theory]
    [InlineData("מעלית", 104)]
    [InlineData("  מעלית  ", 104)]
    [InlineData("‏מעלית", 104)]
    [InlineData("הריסה ופירוק", 301)]
    public void Resolve_HebrewName_ResolvesThroughCatalog(string text, int expected)
    {
        Assert.Equal(expected, UsageCodeParser.Resolve(text, requireCatalog: false));
    }

    [Fact]
    public void Resolve_HebrewName_ToleratesDashAndSpacingVariants()
    {
        // Catalog: "מרחב מוגן דירתי – שטח רצפה" (en dash). Users type a plain hyphen
        // and stray double spaces.
        Assert.Equal(101, UsageCodeParser.Resolve("מרחב מוגן דירתי - שטח רצפה", requireCatalog: false));
        Assert.Equal(101, UsageCodeParser.Resolve("מרחב  מוגן דירתי –  שטח רצפה", requireCatalog: false));
    }

    [Fact]
    public void Resolve_RequireCatalog_RejectsUnknownNumbers()
    {
        // e.g. a *Name parameter carrying a number derived from the area number.
        Assert.Null(UsageCodeParser.Resolve("1151", requireCatalog: true));
        Assert.Equal(1151, UsageCodeParser.Resolve("1151", requireCatalog: false));
        Assert.Equal(115, UsageCodeParser.Resolve("115", requireCatalog: true));
    }

    [Fact]
    public void Resolve_AmbiguousHebrewName_ReturnsFirstCatalogEntry()
    {
        // Several names repeat across kinds (e.g. מרפסת = 30 primary / 110 service / …).
        // Resolution is by catalog order — the code parameter, not the name, is the source
        // of truth; this is only the fallback.
        Assert.Equal(30, UsageCodeParser.Resolve("מרפסת", requireCatalog: false));
    }
}
