using RVTuk.Core.TopoTools;
using Xunit;

namespace RVTuk.Core.Tests.TopoTools;

public class PlaneHeightTests
{
    [Fact]
    public void AHorizontalPlaneHasItsOwnHeightEverywhere()
    {
        var z = PlaneHeight.At(new XyzPoint(3, 4, 12.5), new XyzPoint(0, 0, 1), -20, 55);

        Assert.Equal(12.5, z!.Value);
    }

    [Fact]
    public void ASlopeRisesAlongItsFall()
    {
        // 1 in 10 up along +X. The normal leans back against the rise and is deliberately not
        // unit length: the formula must not care.
        var z = PlaneHeight.At(new XyzPoint(0, 0, 5), new XyzPoint(-0.1, 0, 1), 20, 7);

        Assert.Equal(7.0, z!.Value, 9);
    }

    [Fact]
    public void AVerticalPlaneHasNoHeight()
    {
        Assert.Null(PlaneHeight.At(new XyzPoint(0, 0, 0), new XyzPoint(1, 0, 0), 5, 5));
    }
}
