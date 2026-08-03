using System.Collections.Generic;
using System.Linq;
using RVTuk.Core.AutoDimensions;
using RVTuk.Core.Shared.Geometry;
using Xunit;

namespace RVTuk.Core.Tests.AutoDimensions;

public class CandidateMatcherTests
{
    // Reference line: horizontal, from (0,0) to (10,0). Distances are in the same unit.
    private static readonly XyPoint LineStart = new(0, 0);
    private static readonly XyPoint LineEnd = new(10, 0);

    /// <summary>Nothing standing in the way — the default for cases not about occlusion.</summary>
    private static readonly IReadOnlyList<WallCandidate> NoOccluders = new WallCandidate[0];

    [Fact]
    public void CrossingCandidatesBehaveAsBefore()
    {
        var segments = new List<WallCandidate>
        {
            new(new XyPoint(8, -5), new XyPoint(8, 5)),
            new(new XyPoint(2, -5), new XyPoint(2, 5)),
        };
        var modes = new[] { CandidateMatch.Crossing, CandidateMatch.Crossing };

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, NoOccluders);

        Assert.Equal(new[] { 1, 0 }, result);
    }

    [Fact]
    public void AnOpeningInAParallelWallIsMatched()
    {
        // A door 2 away from the line, in a wall running along it.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 2), new XyPoint(5, 2)) };
        var modes = new[] { CandidateMatch.Alongside };

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, NoOccluders);

        Assert.Equal(new[] { 0 }, result);
    }

    [Fact]
    public void AnOpeningIsMatchedHoweverFarFromTheLine()
    {
        // The case the old reach test rejected: with a clear line of sight, distance is not the
        // question. An exterior string sits far outside the facade it dimensions.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 40), new XyPoint(5, 40)) };
        var modes = new[] { CandidateMatch.Alongside };

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, NoOccluders);

        Assert.Equal(new[] { 0 }, result);
    }

    [Fact]
    public void OpeningsAreMatchedOnBothSidesOfTheLine()
    {
        var segments = new List<WallCandidate> { new(new XyPoint(4, -2), new XyPoint(5, -2)) };
        var modes = new[] { CandidateMatch.Alongside };

        Assert.Single(CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, NoOccluders));
    }

    [Fact]
    public void AnOpeningInAPerpendicularWallIsNotMatched()
    {
        // The old rule's only case, and the one whose jambs lie parallel to the line.
        var segments = new List<WallCandidate> { new(new XyPoint(5, 1.5), new XyPoint(5, 2.5)) };
        var modes = new[] { CandidateMatch.Alongside };

        Assert.Empty(CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, NoOccluders));
    }

    [Fact]
    public void AnOpeningPastTheEndOfTheLineIsNotMatched()
    {
        // Close to the line's infinite extension, but off the end of the drawn segment.
        var segments = new List<WallCandidate> { new(new XyPoint(12, 1), new XyPoint(13, 1)) };
        var modes = new[] { CandidateMatch.Alongside };

        Assert.Empty(CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, NoOccluders));
    }

    [Fact]
    public void CrossingsAndOpeningsInterleaveInOrderAlongTheLine()
    {
        // What an elevation dimension string actually reads like: a cross wall, two openings
        // in the facade, then another cross wall — sorted by position, not by category.
        var segments = new List<WallCandidate>
        {
            new(new XyPoint(9, -5), new XyPoint(9, 5)),      // 0: cross wall at 9
            new(new XyPoint(2.5, 1), new XyPoint(3.5, 1)),   // 1: opening centred at 3
            new(new XyPoint(1, -5), new XyPoint(1, 5)),      // 2: cross wall at 1
            new(new XyPoint(5.5, 1), new XyPoint(6.5, 1)),   // 3: opening centred at 6
        };
        var modes = new[]
        {
            CandidateMatch.Crossing,
            CandidateMatch.Alongside,
            CandidateMatch.Crossing,
            CandidateMatch.Alongside,
        };

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, NoOccluders);

        Assert.Equal(new[] { 2, 1, 3, 0 }, result);
    }

    // ── What stands in the way ────────────────────────────────────────────────
    // An opening belongs to a line when nothing parallel comes between them. This replaces the
    // reach setting: a facade string reaches its whole facade and stops at the first wall behind.

    [Fact]
    public void AParallelWallBetweenTheOpeningAndTheLineBlocksIt()
    {
        // Door 4 away, with a parallel wall at 2 running right past it. The line dimensions the
        // facade in front, never the interior wall behind it.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 4), new XyPoint(5, 4)) };
        var modes = new[] { CandidateMatch.Alongside };
        var occluders = new List<WallCandidate> { new(new XyPoint(0, 2), new XyPoint(10, 2)) };

        Assert.Empty(CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, occluders));
    }

    [Fact]
    public void AParallelWallOnTheOtherSideOfTheLineDoesNotBlock()
    {
        var segments = new List<WallCandidate> { new(new XyPoint(4, 4), new XyPoint(5, 4)) };
        var modes = new[] { CandidateMatch.Alongside };
        var occluders = new List<WallCandidate> { new(new XyPoint(0, -2), new XyPoint(10, -2)) };

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, occluders);

        Assert.Equal(new[] { 0 }, result);
    }

    [Fact]
    public void AParallelWallBeyondTheOpeningDoesNotBlock()
    {
        // Behind the door, not in front of it.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 2), new XyPoint(5, 2)) };
        var modes = new[] { CandidateMatch.Alongside };
        var occluders = new List<WallCandidate> { new(new XyPoint(0, 4), new XyPoint(10, 4)) };

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, occluders);

        Assert.Equal(new[] { 0 }, result);
    }

    [Fact]
    public void AParallelWallThatStopsShortOfTheOpeningDoesNotBlock()
    {
        // The wall covers x 0–3; the door is centred at 4.5. A line sees through a gap in a facade.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 4), new XyPoint(5, 4)) };
        var modes = new[] { CandidateMatch.Alongside };
        var occluders = new List<WallCandidate> { new(new XyPoint(0, 2), new XyPoint(3, 2)) };

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, occluders);

        Assert.Equal(new[] { 0 }, result);
    }

    [Fact]
    public void AWallTheLineCrossesNeverBlocks()
    {
        // Perpendicular, and passing between the line and the door — but a wall the line crosses
        // is something it dimensions, not something in its way.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 4), new XyPoint(5, 4)) };
        var modes = new[] { CandidateMatch.Alongside };
        var occluders = new List<WallCandidate> { new(new XyPoint(4.5, -5), new XyPoint(4.5, 5)) };

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, occluders);

        Assert.Equal(new[] { 0 }, result);
    }

    [Fact]
    public void AnOpeningsOwnHostWallDoesNotBlockIt()
    {
        // The host is in the occluder list like every other wall. A door sits ON its host's
        // location curve, so "strictly nearer" excludes it with no special case.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 2), new XyPoint(5, 2)) };
        var modes = new[] { CandidateMatch.Alongside };
        var occluders = new List<WallCandidate> { new(new XyPoint(0, 2), new XyPoint(10, 2)) };

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, occluders);

        Assert.Equal(new[] { 0 }, result);
    }

    // ── One opening, one owner ────────────────────────────────────────────────
    // Facades carry stacked dimension strings. Every one of them qualifies for the same door,
    // and dimensioning it from all of them is noise — the nearest string owns it.

    private static readonly ReferenceLine NearLine = new(new XyPoint(0, 0), new XyPoint(10, 0));
    private static readonly ReferenceLine FarLine = new(new XyPoint(0, -2), new XyPoint(10, -2));

    [Fact]
    public void AnOpeningGoesToTheNearestLineOnly()
    {
        // Door centred at (4.5, 1): 1 from the near line, 3 from the far one. Both qualify.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 1), new XyPoint(5, 1)) };
        var modes = new[] { CandidateMatch.Alongside };

        var result = CandidateMatcher.FindMatchIndicesForLines(
            new[] { NearLine, FarLine }, segments, modes, NoOccluders);

        Assert.Equal(new[] { 0 }, result[0]);
        Assert.Empty(result[1]);
    }

    [Fact]
    public void TheLosingLineStillGetsItsOwnWallCrossings()
    {
        // Losing a door must not cost a line the walls it crosses — only openings are owned.
        var segments = new List<WallCandidate>
        {
            new(new XyPoint(4, 1), new XyPoint(5, 1)),    // 0: door, nearest to the near line
            new(new XyPoint(6, -5), new XyPoint(6, 5)),   // 1: wall crossing both lines
        };
        var modes = new[] { CandidateMatch.Alongside, CandidateMatch.Crossing };

        var result = CandidateMatcher.FindMatchIndicesForLines(
            new[] { NearLine, FarLine }, segments, modes, NoOccluders);

        Assert.Equal(new[] { 0, 1 }, result[0]);
        Assert.Equal(new[] { 1 }, result[1]);
    }

    [Fact]
    public void AnOpeningBlockedFromEveryLineGoesToNobody()
    {
        // Door at 8, with a parallel wall at 6 standing between it and both lines (at 0 and -2).
        var segments = new List<WallCandidate> { new(new XyPoint(4, 8), new XyPoint(5, 8)) };
        var modes = new[] { CandidateMatch.Alongside };
        var occluders = new List<WallCandidate> { new(new XyPoint(0, 6), new XyPoint(10, 6)) };

        var result = CandidateMatcher.FindMatchIndicesForLines(
            new[] { NearLine, FarLine }, segments, modes, occluders);

        Assert.Empty(result[0]);
        Assert.Empty(result[1]);
    }

    [Fact]
    public void ALineBlockedFromTheOpeningDoesNotCompeteForIt()
    {
        // Door at -4.5. The near line at 0 is closer, but a parallel wall at -1 stands between
        // them; the far line at -2 has that wall on its other side and takes the door. Ownership
        // is decided among the lines that QUALIFY, not by distance alone.
        var segments = new List<WallCandidate> { new(new XyPoint(4, -4.5), new XyPoint(5, -4.5)) };
        var modes = new[] { CandidateMatch.Alongside };
        var occluders = new List<WallCandidate> { new(new XyPoint(0, -1), new XyPoint(10, -1)) };

        var result = CandidateMatcher.FindMatchIndicesForLines(
            new[] { NearLine, FarLine }, segments, modes, occluders);

        Assert.Empty(result[0]);
        Assert.Equal(new[] { 0 }, result[1]);
    }

    [Fact]
    public void AnOpeningFallsToAFartherLineWhenTheNearestDoesNotSpanIt()
    {
        // The short line is nearer (1 away) but stops at x=3, so the door at x=4.5 projects off
        // its end. The far line is 3 away and spans it.
        var shortNearLine = new ReferenceLine(new XyPoint(0, 1.9), new XyPoint(3, 1.9));
        var segments = new List<WallCandidate> { new(new XyPoint(4, 1), new XyPoint(5, 1)) };
        var modes = new[] { CandidateMatch.Alongside };

        var result = CandidateMatcher.FindMatchIndicesForLines(
            new[] { shortNearLine, FarLine }, segments, modes, NoOccluders);

        Assert.Empty(result[0]);
        Assert.Equal(new[] { 0 }, result[1]);
    }

    [Fact]
    public void TwoOpeningsCanBelongToDifferentLines()
    {
        var segments = new List<WallCandidate>
        {
            new(new XyPoint(2, 1), new XyPoint(3, 1)),      // 0: 1 from near, 3 from far
            new(new XyPoint(6, -3), new XyPoint(7, -3)),    // 1: 3 from near, 1 from far
        };
        var modes = new[] { CandidateMatch.Alongside, CandidateMatch.Alongside };

        var result = CandidateMatcher.FindMatchIndicesForLines(
            new[] { NearLine, FarLine }, segments, modes, NoOccluders);

        Assert.Equal(new[] { 0 }, result[0]);
        Assert.Equal(new[] { 1 }, result[1]);
    }

    [Fact]
    public void AnEquidistantOpeningGoesToTheFirstLineDeterministically()
    {
        // Centred at (4.5, -1): exactly 1 from both lines. Either answer is defensible; the
        // same answer every run is not optional, or re-running would shuffle dimensions
        // between strings.
        var segments = new List<WallCandidate> { new(new XyPoint(4, -1), new XyPoint(5, -1)) };
        var modes = new[] { CandidateMatch.Alongside };

        var result = CandidateMatcher.FindMatchIndicesForLines(
            new[] { NearLine, FarLine }, segments, modes, NoOccluders);

        Assert.Equal(new[] { 0 }, result[0]);
        Assert.Empty(result[1]);
    }

    [Fact]
    public void OpeningsStillOrderAlongTheLineThatOwnsThem()
    {
        var segments = new List<WallCandidate>
        {
            new(new XyPoint(7.5, 1), new XyPoint(8.5, 1)),  // 0: centred at 8
            new(new XyPoint(1.5, 1), new XyPoint(2.5, 1)),  // 1: centred at 2
            new(new XyPoint(4.5, 1), new XyPoint(5.5, 1)),  // 2: centred at 5
        };
        var modes = new[]
        {
            CandidateMatch.Alongside, CandidateMatch.Alongside, CandidateMatch.Alongside,
        };

        var result = CandidateMatcher.FindMatchIndicesForLines(
            new[] { NearLine }, segments, modes, NoOccluders);

        Assert.Equal(new[] { 1, 2, 0 }, result[0]);
    }

    [Fact]
    public void NoLinesYieldsNoResults()
    {
        var segments = new List<WallCandidate> { new(new XyPoint(4, 1), new XyPoint(5, 1)) };
        var modes = new[] { CandidateMatch.Alongside };

        Assert.Empty(CandidateMatcher.FindMatchIndicesForLines(
            new ReferenceLine[0], segments, modes, NoOccluders));
    }

    [Fact]
    public void AnOpeningVisibleToBothRingsGoesToTheOuterLine()
    {
        // A facade window at y=10. The inner string stands 2 away and the outer 10 away, and
        // the outer takes it anyway — that is the whole point of the ring. The inner line is
        // listed first so this cannot pass merely by preferring the earlier line.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 10), new XyPoint(5, 10)) };
        var modes = new[] { CandidateMatch.Alongside };
        var lines = new[]
        {
            new ReferenceLine(new XyPoint(0, 8), new XyPoint(10, 8), DimensionRing.Inner),
            new ReferenceLine(new XyPoint(0, 0), new XyPoint(10, 0), DimensionRing.Outer),
        };

        var result = CandidateMatcher.FindMatchIndicesForLines(lines, segments, modes, NoOccluders);

        Assert.Empty(result[0]);
        Assert.Equal(new[] { 0 }, result[1]);
    }

    [Fact]
    public void AnOpeningWalledOffFromEveryOuterLineFallsToTheInner()
    {
        // The wall at y=5 stands between the opening and the outer string, but not between it
        // and the inner one — so the outer cannot claim it and the inner does.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 10), new XyPoint(5, 10)) };
        var modes = new[] { CandidateMatch.Alongside };
        var occluders = new List<WallCandidate> { new(new XyPoint(0, 5), new XyPoint(10, 5)) };
        var lines = new[]
        {
            new ReferenceLine(new XyPoint(0, 0), new XyPoint(10, 0), DimensionRing.Outer),
            new ReferenceLine(new XyPoint(0, 8), new XyPoint(10, 8), DimensionRing.Inner),
        };

        var result = CandidateMatcher.FindMatchIndicesForLines(lines, segments, modes, occluders);

        Assert.Empty(result[0]);
        Assert.Equal(new[] { 0 }, result[1]);
    }

    [Fact]
    public void NearestStillWinsWithinARing()
    {
        var segments = new List<WallCandidate> { new(new XyPoint(4, 10), new XyPoint(5, 10)) };
        var modes = new[] { CandidateMatch.Alongside };
        var lines = new[]
        {
            new ReferenceLine(new XyPoint(0, 0), new XyPoint(10, 0), DimensionRing.Outer),
            new ReferenceLine(new XyPoint(0, 6), new XyPoint(10, 6), DimensionRing.Outer),
        };

        var result = CandidateMatcher.FindMatchIndicesForLines(lines, segments, modes, NoOccluders);

        Assert.Empty(result[0]);
        Assert.Equal(new[] { 0 }, result[1]);
    }

    [Fact]
    public void ATieWithinARingKeepsTheEarlierLine()
    {
        // Equidistant on either side, so re-running must not shuffle the opening between them.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 0), new XyPoint(5, 0)) };
        var modes = new[] { CandidateMatch.Alongside };
        var lines = new[]
        {
            new ReferenceLine(new XyPoint(0, -3), new XyPoint(10, -3), DimensionRing.Outer),
            new ReferenceLine(new XyPoint(0, 3), new XyPoint(10, 3), DimensionRing.Outer),
        };

        var result = CandidateMatcher.FindMatchIndicesForLines(lines, segments, modes, NoOccluders);

        Assert.Equal(new[] { 0 }, result[0]);
        Assert.Empty(result[1]);
    }

    [Fact]
    public void AWallCrossedByBothRingsIsMeasuredByBoth()
    {
        // Only openings are owned. A wall crossed by two strings is measured by both, which is
        // what a chained string is — the ring must not change that.
        var segments = new List<WallCandidate> { new(new XyPoint(5, -20), new XyPoint(5, 20)) };
        var modes = new[] { CandidateMatch.Crossing };
        var lines = new[]
        {
            new ReferenceLine(new XyPoint(0, 0), new XyPoint(10, 0), DimensionRing.Outer),
            new ReferenceLine(new XyPoint(0, 8), new XyPoint(10, 8), DimensionRing.Inner),
        };

        var result = CandidateMatcher.FindMatchIndicesForLines(lines, segments, modes, NoOccluders);

        Assert.Equal(new[] { 0 }, result[0]);
        Assert.Equal(new[] { 0 }, result[1]);
    }

    [Fact]
    public void FindMatchesForLinesReportsTheStationAlongTheLine()
    {
        // The runner needs T to slot ref-line marks into the right place in the string.
        var segments = new List<WallCandidate>
        {
            new(new XyPoint(8, -5), new XyPoint(8, 5)),
            new(new XyPoint(2, -5), new XyPoint(2, 5)),
        };
        var modes = new[] { CandidateMatch.Crossing, CandidateMatch.Crossing };
        var lines = new[] { new ReferenceLine(LineStart, LineEnd) };

        var result = CandidateMatcher.FindMatchesForLines(lines, segments, modes, NoOccluders);

        Assert.Equal(new[] { 1, 0 }, result[0].Select(m => m.Index));
        Assert.Equal(0.2, result[0][0].T, 6);
        Assert.Equal(0.8, result[0][1].T, 6);
    }
}
