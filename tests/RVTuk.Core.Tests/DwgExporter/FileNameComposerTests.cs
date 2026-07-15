using RVTuk.Core.DwgExporter;
using Xunit;

namespace RVTuk.Core.Tests.DwgExporter;

public class FileNameComposerTests
{
    private static NamingRulePart Part(string value, string prefix = "", string suffix = "", string separator = "")
        => new NamingRulePart { Prefix = prefix, Value = value, Suffix = suffix, Separator = separator };

    [Fact]
    public void Compose_JoinsPrefixValueSuffix_AndSeparatorsBetweenParts()
    {
        // <Project Number>-A-BLD_<Building Number>-<Sheet Number>
        var name = FileNameComposer.Compose(new[]
        {
            Part("1234", separator: "-A-BLD_"),
            Part("2", separator: "-"),
            Part("A-101", separator: "---last separator must be ignored---"),
        });

        Assert.Equal("1234-A-BLD_2-A-101", name);
    }

    [Fact]
    public void Compose_SinglePart_NoSeparator()
    {
        Assert.Equal("pre-VAL-suf", FileNameComposer.Compose(new[] { Part("VAL", "pre-", "-suf", "|") }));
    }

    [Fact]
    public void Compose_EmptyValues_RenderAsEmpty_LikeNativePdfExport()
    {
        var name = FileNameComposer.Compose(new[]
        {
            Part("", separator: "-"),
            Part("A-101"),
        });

        Assert.Equal("-A-101", name);
    }

    [Fact]
    public void Compose_PreservesHebrew()
    {
        var name = FileNameComposer.Compose(new[] { Part("תכנית קומה", separator: " "), Part("א-101") });

        Assert.Equal("תכנית קומה א-101", name);
    }

    [Theory]
    [InlineData("A/101", "A-101")]                    // '/' is common in Israeli sheet numbers
    [InlineData("a\\b:c*d?e\"f<g>h|i", "a-b-c-d-e-f-g-h-i")]
    [InlineData("  name. ", "name")]                  // trailing dots/spaces are illegal on Windows
    public void Sanitize_ReplacesIllegalCharacters(string raw, string expected)
    {
        Assert.Equal(expected, FileNameComposer.Sanitize(raw));
    }

    [Fact]
    public void Sanitize_EmptyResult_FallsBackToSheet()
    {
        Assert.Equal("Sheet", FileNameComposer.Sanitize("  .. "));
        Assert.Equal("Sheet", FileNameComposer.Sanitize(""));
    }

    [Fact]
    public void FindDuplicates_IsCaseInsensitive_AndReturnsEachNameOnce()
    {
        var dupes = FileNameComposer.FindDuplicates(new[] { "A-101", "a-101", "A-102", "A-101", "B-1" });

        Assert.Equal(new[] { "A-101" }, dupes);
    }

    [Fact]
    public void FindDuplicates_Empty_WhenAllUnique()
    {
        Assert.Empty(FileNameComposer.FindDuplicates(new[] { "A-101", "A-102" }));
    }
}
