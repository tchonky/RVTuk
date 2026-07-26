using RVTuk.Core.AutoDimensions;
using Xunit;

namespace RVTuk.Core.Tests.AutoDimensions;

public class ReferenceAlignmentTests
{
    // Reference line: horizontal, left to right.
    private static readonly XyPoint LineStart = new(0, 0);
    private static readonly XyPoint LineEnd = new(10, 0);

    // A wall running vertically — the line crosses it square on.
    private static readonly WallCandidate CrossedWall =
        new(new XyPoint(5, -5), new XyPoint(5, 5));

    // A wall running along the line.
    private static readonly WallCandidate ParallelWall =
        new(new XyPoint(0, 2), new XyPoint(10, 2));

    [Fact]
    public void WallSideFacesAreDimensionableByALineCrossingTheWall()
    {
        // Side-face normals run across the wall, so a line crossing it measures thickness.
        Assert.True(ReferenceAlignment.CanDimension(
            LineStart, LineEnd, CrossedWall, ReferenceNormal.AcrossSegment));
    }

    [Fact]
    public void OpeningJambsAreNotDimensionableByALineCrossingTheirWall()
    {
        // The whole bug: jamb normals run ALONG the wall, so a line crossing the wall lies
        // parallel to those planes. Revit throws on the whole dimension, not just the door.
        Assert.False(ReferenceAlignment.CanDimension(
            LineStart, LineEnd, CrossedWall, ReferenceNormal.AlongSegment));
    }

    [Fact]
    public void OpeningJambsAreDimensionableByALineRunningAlongTheirWall()
    {
        Assert.True(ReferenceAlignment.CanDimension(
            LineStart, LineEnd, ParallelWall, ReferenceNormal.AlongSegment));
    }

    [Fact]
    public void WallSideFacesAreNotDimensionableByALineRunningAlongTheWall()
    {
        Assert.False(ReferenceAlignment.CanDimension(
            LineStart, LineEnd, ParallelWall, ReferenceNormal.AcrossSegment));
    }

    [Fact]
    public void AnObliqueCrossingStillWorksForBothOrientations()
    {
        // 45 degrees: neither set of planes is anywhere near parallel to the line, so both
        // measure a (projected) distance Revit accepts.
        var oblique = new WallCandidate(new XyPoint(3, -3), new XyPoint(7, 3));

        Assert.True(ReferenceAlignment.CanDimension(
            LineStart, LineEnd, oblique, ReferenceNormal.AcrossSegment));
        Assert.True(ReferenceAlignment.CanDimension(
            LineStart, LineEnd, oblique, ReferenceNormal.AlongSegment));
    }

    [Fact]
    public void TheToleranceMatchesTheCrossingFindersParallelRule()
    {
        // A wall within 5 degrees of perpendicular is exactly the case WallCrossingFinder
        // admits and jamb planes cannot serve — the two rules must meet, not overlap.
        var almostPerpendicular = new WallCandidate(new XyPoint(5, -5), new XyPoint(5.3, 5));

        Assert.True(ReferenceAlignment.CanDimension(
            LineStart, LineEnd, almostPerpendicular, ReferenceNormal.AcrossSegment));
        Assert.False(ReferenceAlignment.CanDimension(
            LineStart, LineEnd, almostPerpendicular, ReferenceNormal.AlongSegment));
    }

    [Fact]
    public void ADegenerateSegmentIsNeverDimensionable()
    {
        var degenerate = new WallCandidate(new XyPoint(5, 5), new XyPoint(5, 5));

        Assert.False(ReferenceAlignment.CanDimension(
            LineStart, LineEnd, degenerate, ReferenceNormal.AcrossSegment));
        Assert.False(ReferenceAlignment.CanDimension(
            LineStart, LineEnd, degenerate, ReferenceNormal.AlongSegment));
    }
}
