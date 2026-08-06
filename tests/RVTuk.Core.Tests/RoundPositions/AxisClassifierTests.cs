using RVTuk.Core.RoundPositions;
using Xunit;

namespace RVTuk.Core.Tests.RoundPositions;

public class AxisClassifierTests
{
    private const double Tol = AxisClassifier.ToleranceCentimetres;

    [Fact]
    public void AHorizontalLineRunsAlongX()
    {
        Assert.Equal(LineAxis.AlongX, AxisClassifier.Classify(400.0, 0.0, Tol));
    }

    [Fact]
    public void AHorizontalLineDrawnRightToLeftAlsoRunsAlongX()
    {
        // Which end Revit calls "start" is arbitrary; the test must be undirected.
        Assert.Equal(LineAxis.AlongX, AxisClassifier.Classify(-400.0, 0.0, Tol));
    }

    [Fact]
    public void AVerticalLineRunsAlongY()
    {
        Assert.Equal(LineAxis.AlongY, AxisClassifier.Classify(0.0, 400.0, Tol));
        Assert.Equal(LineAxis.AlongY, AxisClassifier.Classify(0.0, -400.0, Tol));
    }

    [Fact]
    public void FloatDustInTheStoredGeometryStillCountsAsOrthogonal()
    {
        Assert.Equal(LineAxis.AlongX, AxisClassifier.Classify(400.0, 1e-9, Tol));
    }

    [Fact]
    public void ANearOrthogonalWallIsNotOrthogonal()
    {
        // 89.97° over 4 m is ~0.2 cm of drift. v1 skips it rather than sliding a crooked wall
        // sideways; straightening is v2.
        Assert.Equal(LineAxis.NotOrthogonal, AxisClassifier.Classify(0.2, 400.0, Tol));
    }

    [Fact]
    public void ADiagonalIsNotOrthogonal()
    {
        Assert.Equal(LineAxis.NotOrthogonal, AxisClassifier.Classify(300.0, 400.0, Tol));
        Assert.Equal(LineAxis.NotOrthogonal, AxisClassifier.Classify(400.0, 400.0, Tol));
    }

    [Fact]
    public void ADegenerateLineIsNotOrthogonal()
    {
        Assert.Equal(LineAxis.NotOrthogonal, AxisClassifier.Classify(0.0, 0.0, Tol));
        Assert.Equal(LineAxis.NotOrthogonal, AxisClassifier.Classify(1e-9, 1e-9, Tol));
    }

    [Fact]
    public void ToleranceIsADisplacementSoItDoesNotScaleWithLength()
    {
        // The same 0.5 cm of drift is a rounding error on a 40 m wall and a real angle on a
        // 40 cm one. An angle test would call the first orthogonal and the second not; the
        // displacement test calls both crooked, which is the honest answer.
        Assert.Equal(LineAxis.NotOrthogonal, AxisClassifier.Classify(4000.0, 0.5, Tol));
        Assert.Equal(LineAxis.NotOrthogonal, AxisClassifier.Classify(40.0, 0.5, Tol));
    }
}
