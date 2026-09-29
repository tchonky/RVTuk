using System.Collections.Generic;
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class ParkedParameterCodecTests
{
    private const string SharedGuid = "0b4c9f4e-2d3a-4c1b-9e8f-7a6b5c4d3e2f";

    private static IReadOnlyList<ParkedParameter> RoundTrip(params ParkedParameter[] entries) =>
        ParkedParameterCodec.Decode(ParkedParameterCodec.Encode(entries));

    [Fact]
    public void RoundTripsEveryStorageType()
    {
        var entries = new[]
        {
            ParkedParameter.OfInteger(ParkedKeyKind.BuiltIn, "-1001203", 1),
            ParkedParameter.OfDouble(ParkedKeyKind.Shared, SharedGuid, 1.0 / 3.0),
            ParkedParameter.OfString(ParkedKeyKind.Project, "Finish Code", "P-12"),
            ParkedParameter.OfElementId(ParkedKeyKind.BuiltIn, "-1012101", 9007199254740993),
        };

        var decoded = RoundTrip(entries);

        Assert.Equal(entries, decoded);
        Assert.True(decoded[0].TryGetInteger(out var flag));
        Assert.Equal(1, flag);
        Assert.True(decoded[1].TryGetDouble(out var third));
        Assert.Equal(1.0 / 3.0, third);
        Assert.True(decoded[3].TryGetElementId(out var id));
        Assert.Equal(9007199254740993, id);
    }

    [Theory]
    [InlineData("ריצוף A-12")]
    [InlineData("מטבח 2 / LIQUI HARD")]
    [InlineData("KEMIKO גמר 60x60")]
    public void MixedHebrewAndLatinTextSurvivesExactly(string text)
    {
        var decoded = RoundTrip(ParkedParameter.OfString(ParkedKeyKind.Shared, SharedGuid, text));

        Assert.Equal(text, Assert.Single(decoded).Value);
    }

    [Theory]
    [InlineData("a|b;c")]
    [InlineData("50%")]
    [InlineData("%7C and %3B and %25, literally")]
    [InlineData("")]
    public void SeparatorsPercentSignsAndEmptyTextSurvive(string text)
    {
        var decoded = RoundTrip(
            ParkedParameter.OfString(ParkedKeyKind.Project, "Note|v2;x%", text),
            ParkedParameter.OfInteger(ParkedKeyKind.BuiltIn, "-1001203", 0));

        Assert.Equal(2, decoded.Count);
        Assert.Equal("Note|v2;x%", decoded[0].Key);
        Assert.Equal(text, decoded[0].Value);
        Assert.Equal("0", decoded[1].Value);
    }

    [Fact]
    public void AnEmptyListEncodesAsAnEmptyString()
    {
        Assert.Equal("", ParkedParameterCodec.Encode(new ParkedParameter[0]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("X|k|T|v")]    // unknown key kind
    [InlineData("B|k|Q|v")]    // unknown storage
    [InlineData("B|%ZZ|T|v")]  // broken escape
    [InlineData("B||T|v")]     // empty key
    [InlineData("B|k|T|v%2")]  // truncated escape
    public void DamagedInputDecodesToNothingWithoutThrowing(string? encoded)
    {
        Assert.Empty(ParkedParameterCodec.Decode(encoded));
    }

    [Fact]
    public void ADamagedEntryIsSkippedAndTheRestKept()
    {
        var good = ParkedParameterCodec.Encode(new[]
        {
            ParkedParameter.OfString(ParkedKeyKind.Project, "Mark 2", "A1"),
        });

        var decoded = ParkedParameterCodec.Decode("B|%ZZ|T|v;" + good);

        Assert.Equal("A1", Assert.Single(decoded).Value);
    }
}
