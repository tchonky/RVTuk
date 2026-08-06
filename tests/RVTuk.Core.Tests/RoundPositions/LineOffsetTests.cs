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
        // This is the whole point of translating rather than reshaping: lengths cannot change,
        // so two walls sharing a corner cannot pull it apart.
        var result = For(new GridPoint(10.3, 5.2, 0), new GridPoint(410.9, 5.2, 0));
        Assert.Equal(0.0, result.DeltaX, Epsilon);
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
        var result = For(new GridPoint(0, 0, 0), new GridPoint(301.2, 400.1, 0));

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
