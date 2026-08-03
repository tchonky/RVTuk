using System.Collections.Generic;
using System.Linq;
using RVTuk.Core.AutoDimensions;
using RVTuk.Core.Shared.Geometry;
using Xunit;

namespace RVTuk.Core.Tests.AutoDimensions;

public class RefLineMatcherTests
{
    // An outer string along y=0 and an inner string along y=3, both running x=0..10.
    private static readonly IReadOnlyList<ReferenceLine> TwoStrings = new[]
    {
        new ReferenceLine(new XyPoint(0, 0), new XyPoint(10, 0), DimensionRing.Outer),
        new ReferenceLine(new XyPoint(0, 3), new XyPoint(10, 3), DimensionRing.Inner),
    };

    /// <summary>Feet, matching what the Revit side passes: about 3 mm of slack.</summary>
    private const double Tolerance = 0.01;

    [Fact]
    public void AnEndpointTouchingAStringMatchesItAndTheOtherEndIsTheTarget()
    {
        // Drawn from a wall end at (5,6) down to the inner string, stopping on it.
        var match = RefLineMatcher.Match(
            new XyPoint(5, 6), new XyPoint(5, 3), TwoStrings, Tolerance);

        Assert.NotNull(match);
        Assert.Equal(new[] { 1 }, match!.Hits.Select(h => h.LineIndex));
        Assert.Equal(0.5, match.Hits[0].T, 6);
        Assert.Equal(new XyPoint(5, 6), match.TargetEnd);
    }

    [Fact]
    public void ALineCrossingBothStringsMatchesBoth()
    {
        // Run out through both and both get the mark — how far you draw is the control.
        var match = RefLineMatcher.Match(
            new XyPoint(5, 6), new XyPoint(5, -1), TwoStrings, Tolerance);

        Assert.NotNull(match);
        Assert.Equal(new[] { 0, 1 }, match!.Hits.Select(h => h.LineIndex).OrderBy(i => i));
        Assert.All(match.Hits, hit => Assert.Equal(0.5, hit.T, 6));
        Assert.Equal(new XyPoint(5, 6), match.TargetEnd);
    }

    [Fact]
    public void ALineCrossingAStringAndRunningPastItStillTargetsTheFarEnd()
    {
        // Only the outer string here, crossed mid-span with the line carrying on beyond it.
        var strings = new[] { TwoStrings[0] };

        var match = RefLineMatcher.Match(
            new XyPoint(5, 8), new XyPoint(5, -2), strings, Tolerance);

        Assert.NotNull(match);
        Assert.Equal(new XyPoint(5, 8), match!.TargetEnd);
    }

    [Fact]
    public void ALineTouchingNoStringMatchesNothing()
    {
        // Stops short at y=4, above both strings.
        Assert.Null(RefLineMatcher.Match(
            new XyPoint(5, 6), new XyPoint(5, 4), TwoStrings, Tolerance));
    }

    [Fact]
    public void ALineWithBothEndsOnStringsIsRejected()
    {
        // Spans string to string, so neither end points at anything to reference.
        Assert.Null(RefLineMatcher.Match(
            new XyPoint(5, 3), new XyPoint(5, 0), TwoStrings, Tolerance));
    }

    [Fact]
    public void ALineMeetingAStringPastItsEndIsNotAMatch()
    {
        // x=15 is off the end of both strings, which stop at x=10.
        Assert.Null(RefLineMatcher.Match(
            new XyPoint(15, 6), new XyPoint(15, -1), TwoStrings, Tolerance));
    }

    [Fact]
    public void ALineRunningAlongAStringDoesNotMeetIt()
    {
        // Parallel: there is no point at which it meets, only an overlap.
        var strings = new[] { TwoStrings[0] };

        Assert.Null(RefLineMatcher.Match(
            new XyPoint(2, 0), new XyPoint(8, 0), strings, Tolerance));
    }

    [Fact]
    public void ADegenerateLineIsRejected()
    {
        Assert.Null(RefLineMatcher.Match(
            new XyPoint(5, 3), new XyPoint(5, 3), TwoStrings, Tolerance));
    }
}
