using System.Collections.Generic;
using RVTuk.Core.AutoDimensions;
using Xunit;

namespace RVTuk.Core.Tests.AutoDimensions;

public class CandidateMatcherTests
{
    // Reference line: horizontal, from (0,0) to (10,0). Distances are in the same unit.
    private static readonly XyPoint LineStart = new(0, 0);
    private static readonly XyPoint LineEnd = new(10, 0);
    private const double Reach = 3.0;

    [Fact]
    public void CrossingCandidatesBehaveAsBefore()
    {
        var segments = new List<WallCandidate>
        {
            new(new XyPoint(8, -5), new XyPoint(8, 5)),
            new(new XyPoint(2, -5), new XyPoint(2, 5)),
        };
        var modes = new[] { CandidateMatch.Crossing, CandidateMatch.Crossing };

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, Reach);

        Assert.Equal(new[] { 1, 0 }, result);
    }

    [Fact]
    public void AnOpeningInAParallelWallWithinReachIsMatched()
    {
        // A door 2 away from the line, in a wall running along it.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 2), new XyPoint(5, 2)) };
        var modes = new[] { CandidateMatch.Alongside };

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, Reach);

        Assert.Equal(new[] { 0 }, result);
    }

    [Fact]
    public void AnOpeningBeyondReachIsNotMatched()
    {
        // Same door, now 4 away: a parallel wall deeper in the plan is not this line's business.
        var segments = new List<WallCandidate> { new(new XyPoint(4, 4), new XyPoint(5, 4)) };
        var modes = new[] { CandidateMatch.Alongside };

        Assert.Empty(CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, Reach));
    }

    [Fact]
    public void ReachIsMeasuredOnBothSidesOfTheLine()
    {
        var segments = new List<WallCandidate> { new(new XyPoint(4, -2), new XyPoint(5, -2)) };
        var modes = new[] { CandidateMatch.Alongside };

        Assert.Single(CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, Reach));
    }

    [Fact]
    public void AnOpeningInAPerpendicularWallIsNotMatched()
    {
        // The old rule's only case, and the one whose jambs lie parallel to the line.
        var segments = new List<WallCandidate> { new(new XyPoint(5, 1.5), new XyPoint(5, 2.5)) };
        var modes = new[] { CandidateMatch.Alongside };

        Assert.Empty(CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, Reach));
    }

    [Fact]
    public void AnOpeningPastTheEndOfTheLineIsNotMatched()
    {
        // Close to the line's infinite extension, but off the end of the drawn segment.
        var segments = new List<WallCandidate> { new(new XyPoint(12, 1), new XyPoint(13, 1)) };
        var modes = new[] { CandidateMatch.Alongside };

        Assert.Empty(CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, Reach));
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

        var result = CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, Reach);

        Assert.Equal(new[] { 2, 1, 3, 0 }, result);
    }

    [Fact]
    public void ZeroReachMatchesNoOpenings()
    {
        var segments = new List<WallCandidate> { new(new XyPoint(4, 0.5), new XyPoint(5, 0.5)) };
        var modes = new[] { CandidateMatch.Alongside };

        Assert.Empty(CandidateMatcher.FindMatchIndices(LineStart, LineEnd, segments, modes, 0));
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
            new[] { NearLine, FarLine }, segments, modes, Reach);

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
            new[] { NearLine, FarLine }, segments, modes, Reach);

        Assert.Equal(new[] { 0, 1 }, result[0]);
        Assert.Equal(new[] { 1 }, result[1]);
    }

    [Fact]
    public void AnOpeningOutOfEveryLinesReachGoesToNobody()
    {
        var segments = new List<WallCandidate> { new(new XyPoint(4, 8), new XyPoint(5, 8)) };
        var modes = new[] { CandidateMatch.Alongside };

        var result = CandidateMatcher.FindMatchIndicesForLines(
            new[] { NearLine, FarLine }, segments, modes, Reach);

        Assert.Empty(result[0]);
        Assert.Empty(result[1]);
    }

    [Fact]
    public void ALineTooFarAwayDoesNotCompeteForTheOpening()
    {
        // Centred at (4.5, -4.5): 4.5 from the near line (out of reach), 2.5 from the far one.
        // Reach filtering, not ownership — the winner here is also the nearest, so this cannot
        // distinguish "nearest that qualifies" from "nearest, then check reach". That is what
        // AnOpeningFallsToAFartherLineWhenTheNearestDoesNotSpanIt exists for.
        var segments = new List<WallCandidate> { new(new XyPoint(4, -4.5), new XyPoint(5, -4.5)) };
        var modes = new[] { CandidateMatch.Alongside };

        var result = CandidateMatcher.FindMatchIndicesForLines(
            new[] { NearLine, FarLine }, segments, modes, Reach);

        Assert.Empty(result[0]);
        Assert.Equal(new[] { 0 }, result[1]);
    }

    [Fact]
    public void AnOpeningFallsToAFartherLineWhenTheNearestDoesNotSpanIt()
    {
        // The case that actually pins "ownership among lines that QUALIFY". The short line is
        // nearer (1 away) but stops at x=3, so the door at x=4.5 projects off its end. The far
        // line is 3 away and spans it. Nearest-then-check would leave the door unowned.
        var shortNearLine = new ReferenceLine(new XyPoint(0, 1.9), new XyPoint(3, 1.9));
        var segments = new List<WallCandidate> { new(new XyPoint(4, 1), new XyPoint(5, 1)) };
        var modes = new[] { CandidateMatch.Alongside };

        var result = CandidateMatcher.FindMatchIndicesForLines(
            new[] { shortNearLine, FarLine }, segments, modes, Reach);

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
            new[] { NearLine, FarLine }, segments, modes, Reach);

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
            new[] { NearLine, FarLine }, segments, modes, Reach);

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
            new[] { NearLine }, segments, modes, Reach);

        Assert.Equal(new[] { 1, 2, 0 }, result[0]);
    }

    [Fact]
    public void NoLinesYieldsNoResults()
    {
        var segments = new List<WallCandidate> { new(new XyPoint(4, 1), new XyPoint(5, 1)) };
        var modes = new[] { CandidateMatch.Alongside };

        Assert.Empty(CandidateMatcher.FindMatchIndicesForLines(
            new ReferenceLine[0], segments, modes, Reach));
    }
}
