using System.Collections.Generic;
using System.Globalization;
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class OutlineCornerRecordCodecTests
{
    [Fact]
    public void RoundTripsValuesAndTheByHandFlag()
    {
        var original = new[]
        {
            new CornerRecord(1.5, -2.25, 40.125, 41.5, false),
            new CornerRecord(-0.0, 1e-17, 12345.678901234, -7.1, true),
        };

        var decoded = OutlineCornerRecordCodec.Decode(OutlineCornerRecordCodec.Encode(original));

        Assert.Equal(original.Length, decoded.Count);
        for (var i = 0; i < original.Length; i++) Assert.Equal(original[i], decoded[i]);
        Assert.Equal(1e-17, decoded[1].Y);
        Assert.Equal(12345.678901234, decoded[1].OwnZ);
        Assert.True(decoded[1].ByHand);
        Assert.False(decoded[0].ByHand);
    }

    [Fact]
    public void TextFormatIsEntriesJoinedBySemicolonWithFlagZeroOrHand()
    {
        var text = OutlineCornerRecordCodec.Encode(new[]
        {
            new CornerRecord(1.5, 2, 3, 4.25, false),
            new CornerRecord(5, 6, 7, 8, true),
        });
        Assert.Equal("1.5,2,3,4.25,0;5,6,7,8,hand", text);
    }

    [Fact]
    public void EmptyListEncodesToEmptyString()
    {
        Assert.Equal("", OutlineCornerRecordCodec.Encode(new List<CornerRecord>()));
        Assert.Empty(OutlineCornerRecordCodec.Decode(""));
    }

    [Fact]
    public void NullDecodesToNothing()
    {
        Assert.Empty(OutlineCornerRecordCodec.Decode(null));
    }

    [Theory]
    [InlineData("1,2,3")]
    [InlineData("a,b,c,d,0")]
    [InlineData("1,2,3,4,maybe")]
    [InlineData("1,2,3,4,0,9")]
    [InlineData("1,2,NaN,4,0")]
    [InlineData("Infinity,2,3,4,0")]
    [InlineData("1,-Infinity,3,4,0")]
    [InlineData("1,2,3,1e999,0")]
    [InlineData("")]
    public void MalformedEntriesAreSkippedAndTheWellFormedRestSurvives(string bad)
    {
        var decoded = OutlineCornerRecordCodec.Decode(bad + ";1,2,3,4,0;" + bad + ";5,6,7,8,hand");

        Assert.Equal(2, decoded.Count);
        Assert.Equal(new CornerRecord(1, 2, 3, 4, false), decoded[0]);
        Assert.Equal(new CornerRecord(5, 6, 7, 8, true), decoded[1]);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("he-IL")]
    public void EncodingIsCultureInvariant(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var original = new[] { new CornerRecord(1.5, -2.25, 3.125, 4.5, true) };
            var text = OutlineCornerRecordCodec.Encode(original);
            Assert.Equal("1.5,-2.25,3.125,4.5,hand", text);
            Assert.Equal(original[0], OutlineCornerRecordCodec.Decode(text)[0]);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
