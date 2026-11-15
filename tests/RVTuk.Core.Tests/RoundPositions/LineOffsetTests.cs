using RVTuk.Core.RoundPositions;
using Xunit;

namespace RVTuk.Core.Tests.RoundPositions;

public class LineOffsetTests
{
    private const double Half = GridSnapper.HalfCentimetreStep;
    private const double Tol = AxisClassifier.ToleranceCentimetres;
    private const double Epsilon = 1e-9;

    private static LineOffsetResult For(GridPoint a, GridPoint b) => LineOffset.For(a, b, Half, Tol);

    [Fact]
    public void AHorizontalLineMovesInYOnly()
    {
        // The spec's worked example: (10.3, 5.2) → (410.9, 5.2) becomes y = 5.0, x untouched.
        var result = For(new GridPoint(10.3, 5.2, 0), new GridPoint(410.9, 5.2, 0));

        Assert.Equal(LineOffsetOutcome.Offset, result.Outcome);
        Assert.Equal(0.0, result.DeltaX, Epsilon);
        Assert.Equal(-0.2, result.DeltaY, Epsilon);
    }

    [Fact]
    public void AVerticalLineMovesInXOnly()
    {
        var result = For(new GridPoint(5.2, 10.3, 0), new GridPoint(5.2, 410.9, 0));

        Assert.Equal(LineOffsetOutcome.Offset, result.Outcome);
        Assert.Equal(-0.2, result.DeltaX, Epsilon);
        Assert.Equal(0.0, result.DeltaY, Epsilon);
    }

    [Fact]
    public void TheAlongAxisCoordinatesAreNeverTouched()
    {
        // This is the whole point of translating rather than reshaping: one delta carries both
        // endpoints, so the line's length and angle survive the move.
        var result = For(new GridPoint(10.3, 5.2, 0), new GridPoint(410.9, 5.2, 0));
        Assert.Equal(0.0, result.DeltaX, Epsilon);
    }

    [Fact]
    public void TheStartPointAnchorsTheTranslation()
    {
        // The two ends may differ across the line by up to the orthogonality tolerance and still
        // count as horizontal — here by 0.0099 cm. That is not a rounding whisker: Snap is a step
        // function, and these two straddle a midpoint, so anchoring on end instead would land the
        // wall a full 0.5 cm step away, on the next grid line over (+0.2451 rather than -0.2450).
        var result = For(new GridPoint(0.0, 5.2450, 0), new GridPoint(400.0, 5.2549, 0));

        Assert.Equal(LineOffsetOutcome.Offset, result.Outcome);
        Assert.Equal(-0.2450, result.DeltaY, Epsilon);
    }

    [Fact]
    public void ALineAlreadyOnTheGridReportsNoMovement()
    {
        var result = For(new GridPoint(10.3, 5.0, 0), new GridPoint(410.9, 5.0, 0));

        Assert.Equal(LineOffsetOutcome.AlreadyOnGrid, result.Outcome);
        Assert.Equal(0.0, result.DeltaX, Epsilon);
        Assert.Equal(0.0, result.DeltaY, Epsilon);
    }

    [Fact]
    public void ANonOrthogonalLineIsRejectedWithNoDelta()
    {
        // Both coordinates of the start are off-grid, so a delta leaked out of the rejection path
        // would be ±0.2 rather than the zero an on-grid start would hand back either way.
        var result = For(new GridPoint(10.3, 5.2, 0), new GridPoint(311.5, 405.3, 0));

        Assert.Equal(LineOffsetOutcome.NotOrthogonal, result.Outcome);
        Assert.Equal(0.0, result.DeltaX, Epsilon);
        Assert.Equal(0.0, result.DeltaY, Epsilon);
    }

    [Fact]
    public void TheWholeCentimetreGridUsesTheWholeCentimetreStep()
    {
        var result = LineOffset.For(
            new GridPoint(10.3, 5.4, 0), new GridPoint(410.9, 5.4, 0),
            GridSnapper.WholeCentimetreStep, Tol);

        Assert.Equal(LineOffsetOutcome.Offset, result.Outcome);
        Assert.Equal(-0.4, result.DeltaY, Epsilon);
    }

    [Fact]
    public void ZIsNeverPartOfTheOffset()
    {
        // A wall's height comes from its level and base offset, which other passes own.
        var result = For(new GridPoint(10.0, 5.0, 3.7), new GridPoint(410.0, 5.0, 3.7));
        Assert.Equal(LineOffsetOutcome.AlreadyOnGrid, result.Outcome);
    }
}
