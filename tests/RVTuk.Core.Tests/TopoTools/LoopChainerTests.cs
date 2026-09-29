using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class LoopChainerTests
{
    private const double Tolerance = 0.0005;

    private static readonly XyzPoint A = new(0, 0, 0);
    private static readonly XyzPoint B = new(10, 0, 0);
    private static readonly XyzPoint C = new(10, 10, 0);
    private static readonly XyzPoint D = new(0, 10, 0);

    [Fact]
    public void AnOrderedLoopComesBackAsItIs()
    {
        var chain = LoopChainer.Chain(
            new[] { new Segment(A, B), new Segment(B, C), new Segment(C, D), new Segment(D, A) }, Tolerance);

        Assert.Equal(
            new[] { new ChainLink(0, false), new ChainLink(1, false), new ChainLink(2, false), new ChainLink(3, false) },
            chain);
    }

    [Fact]
    public void ShuffledSegmentsAreChainedHeadToTail()
    {
        var chain = LoopChainer.Chain(
            new[] { new Segment(A, B), new Segment(C, D), new Segment(B, C), new Segment(D, A) }, Tolerance);

        Assert.Equal(
            new[] { new ChainLink(0, false), new ChainLink(2, false), new ChainLink(1, false), new ChainLink(3, false) },
            chain);
    }

    [Fact]
    public void AReversedSegmentIsFlagged()
    {
        var chain = LoopChainer.Chain(
            new[] { new Segment(A, B), new Segment(C, B), new Segment(C, D), new Segment(D, A) }, Tolerance);

        Assert.Equal(
            new[] { new ChainLink(0, false), new ChainLink(1, true), new ChainLink(2, false), new ChainLink(3, false) },
            chain);
    }

    [Fact]
    public void ACircleDrawnAsTwoArcsTheSameWayRoundCloses()
    {
        // Two half-circles both running A → C: the second has to be walked backwards.
        var chain = LoopChainer.Chain(new[] { new Segment(A, C), new Segment(A, C) }, Tolerance);

        Assert.Equal(new[] { new ChainLink(0, false), new ChainLink(1, true) }, chain);
    }

    [Fact]
    public void ASingleClosedCurveIsALoop()
    {
        Assert.Equal(new[] { new ChainLink(0, false) }, LoopChainer.Chain(new[] { new Segment(A, A) }, Tolerance));
    }

    [Fact]
    public void AnOpenChainIsRejected()
    {
        Assert.Null(LoopChainer.Chain(
            new[] { new Segment(A, B), new Segment(B, C), new Segment(C, D) }, Tolerance));
    }

    [Fact]
    public void AGapWithinToleranceCloses_ButOneBeyondDoesNot()
    {
        var nearA = new XyzPoint(0.0001, 0, 0);
        var farA = new XyzPoint(0.01, 0, 0);

        Assert.NotNull(LoopChainer.Chain(
            new[] { new Segment(A, B), new Segment(B, C), new Segment(C, D), new Segment(D, nearA) }, Tolerance));
        Assert.Null(LoopChainer.Chain(
            new[] { new Segment(A, B), new Segment(B, C), new Segment(C, D), new Segment(D, farA) }, Tolerance));
    }

    [Fact]
    public void NoSegmentsIsNotALoop()
    {
        Assert.Null(LoopChainer.Chain(new Segment[0], Tolerance));
    }
}
