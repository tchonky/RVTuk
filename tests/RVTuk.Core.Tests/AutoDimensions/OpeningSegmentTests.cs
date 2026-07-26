using RVTuk.Core.AutoDimensions;
using Xunit;

namespace RVTuk.Core.Tests.AutoDimensions;

public class OpeningSegmentTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void CentersTheSegmentOnTheOpeningAlongAUnitDirection()
    {
        var segment = OpeningSegment.FromCenter(new XyPoint(5, 0), new XyPoint(1, 0), 4);

        Assert.Equal(3, segment.Start.X, Tolerance);
        Assert.Equal(0, segment.Start.Y, Tolerance);
        Assert.Equal(7, segment.End.X, Tolerance);
        Assert.Equal(0, segment.End.Y, Tolerance);
    }

    [Fact]
    public void NormalisesANonUnitDirection()
    {
        // (3,4) has length 5; a width of 10 puts each end 5 away along (0.6, 0.8).
        var segment = OpeningSegment.FromCenter(new XyPoint(0, 0), new XyPoint(3, 4), 10);

        Assert.Equal(-3, segment.Start.X, Tolerance);
        Assert.Equal(-4, segment.Start.Y, Tolerance);
        Assert.Equal(3, segment.End.X, Tolerance);
        Assert.Equal(4, segment.End.Y, Tolerance);
    }

    [Fact]
    public void HandlesAVerticalDirection()
    {
        var segment = OpeningSegment.FromCenter(new XyPoint(2, 2), new XyPoint(0, 1), 2);

        Assert.Equal(2, segment.Start.X, Tolerance);
        Assert.Equal(1, segment.Start.Y, Tolerance);
        Assert.Equal(2, segment.End.X, Tolerance);
        Assert.Equal(3, segment.End.Y, Tolerance);
    }

    [Fact]
    public void DegenerateDirectionCollapsesToThePoint()
    {
        // Nothing sane to build; WallCrossingFinder rejects zero-length candidates anyway.
        var segment = OpeningSegment.FromCenter(new XyPoint(1, 1), new XyPoint(0, 0), 3);

        Assert.Equal(segment.Start, segment.End);
    }
}
