using System.Collections.Generic;
using RVTuk.Core.AutoDimensions;
using Xunit;

namespace RVTuk.Core.Tests.AutoDimensions;

public class IdCodecTests
{
    [Fact]
    public void ListRoundTripsIncludingLargeIds()
    {
        var ids = new long[] { 1, 4_294_967_296L, 9_007_199_254_740_993L };

        var restored = IdListCodec.Decode(IdListCodec.Encode(ids));

        Assert.Equal(ids, restored);
    }

    [Fact]
    public void EmptyListRoundTripsToEmpty()
    {
        Assert.Empty(IdListCodec.Decode(IdListCodec.Encode(new long[0])));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ListDecodeOfNothingIsEmpty(string? encoded)
    {
        Assert.Empty(IdListCodec.Decode(encoded));
    }

    [Fact]
    public void ListDecodeDropsUnparseableEntries()
    {
        // A hand-edited or truncated entity must not take the whole selection down with it.
        Assert.Equal(new long[] { 7, 9 }, IdListCodec.Decode("7,,oops,9"));
    }

    [Fact]
    public void MapRoundTripsIncludingLargeIds()
    {
        var map = new Dictionary<long, long>
        {
            [1] = 2,
            [4_294_967_296L] = 9_007_199_254_740_993L,
        };

        var restored = IdMapCodec.Decode(IdMapCodec.Encode(map));

        Assert.Equal(2L, restored[1]);
        Assert.Equal(9_007_199_254_740_993L, restored[4_294_967_296L]);
        Assert.Equal(2, restored.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MapDecodeOfNothingIsEmpty(string? encoded)
    {
        Assert.Empty(IdMapCodec.Decode(encoded));
    }

    [Fact]
    public void MapDecodeDropsMalformedPairsAndKeepsTheLastValueForARepeatedKey()
    {
        var restored = IdMapCodec.Decode("5:6;bad;7:;:8;5:9");

        Assert.Equal(9L, restored[5]);
        Assert.Single(restored);
    }
}
