using System.Collections.Generic;
using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class TopoPointLedgerCodecTests
{
    private static Dictionary<long, IReadOnlyList<XyzPoint>> Ledger(
        params (long LineId, XyzPoint[] Points)[] entries)
    {
        var ledger = new Dictionary<long, IReadOnlyList<XyzPoint>>();
        foreach (var (lineId, points) in entries) ledger[lineId] = points;
        return ledger;
    }

    [Fact]
    public void RoundTripsSeveralLinesAndPoints()
    {
        var original = Ledger(
            (412, new[] { new XyzPoint(1.5, -2.25, 40.125), new XyzPoint(3, 4, 40.125) }),
            (9007199254740993, new[] { new XyzPoint(-0.1, 0, -12.75) }));

        var decoded = TopoPointLedgerCodec.Decode(TopoPointLedgerCodec.Encode(original));

        Assert.Equal(2, decoded.Count);
        Assert.Equal(2, decoded[412].Count);
        Assert.Equal(new XyzPoint(1.5, -2.25, 40.125), decoded[412][0]);
        Assert.Equal(new XyzPoint(3, 4, 40.125), decoded[412][1]);
        Assert.Equal(new XyzPoint(-0.1, 0, -12.75), decoded[9007199254740993][0]);
    }

    [Fact]
    public void SurvivesCoordinatesThatNeedFullPrecision()
    {
        var point = new XyzPoint(1.0 / 3.0, 2.0 / 7.0, 123.456789012345);
        var original = Ledger((1, new[] { point }));

        var decoded = TopoPointLedgerCodec.Decode(TopoPointLedgerCodec.Encode(original));

        Assert.Equal(point, decoded[1][0]);
    }

    [Fact]
    public void EncodesAnEmptyLedgerAsAnEmptyString()
    {
        Assert.Equal("", TopoPointLedgerCodec.Encode(Ledger()));
    }

    [Fact]
    public void SkipsALineWithNoPoints()
    {
        var encoded = TopoPointLedgerCodec.Encode(Ledger(
            (1, new XyzPoint[0]),
            (2, new[] { new XyzPoint(0, 0, 0) })));

        var decoded = TopoPointLedgerCodec.Decode(encoded);

        Assert.Single(decoded);
        Assert.True(decoded.ContainsKey(2));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a ledger")]
    [InlineData("12=1,2")]
    [InlineData("abc=1,2,3")]
    public void DecodesAnythingMalformedAsEmpty(string? encoded)
    {
        Assert.Empty(TopoPointLedgerCodec.Decode(encoded));
    }

    [Fact]
    public void KeepsTheGoodEntriesWhenOneIsCorrupt()
    {
        var decoded = TopoPointLedgerCodec.Decode("5=1,2,3;broken;7=4,5,6");

        Assert.Equal(2, decoded.Count);
        Assert.Equal(new XyzPoint(1, 2, 3), decoded[5][0]);
        Assert.Equal(new XyzPoint(4, 5, 6), decoded[7][0]);
    }
}
