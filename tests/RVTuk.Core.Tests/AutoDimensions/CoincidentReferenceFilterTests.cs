using RVTuk.Core.AutoDimensions;
using Xunit;

namespace RVTuk.Core.Tests.AutoDimensions;

public class CoincidentReferenceFilterTests
{
    private const double Tolerance = 0.001;

    // A dimension over N references has N-1 segments; segmentValues[i] is the length
    // between reference i and reference i+1.

    [Fact]
    public void NoZeroSegments_KeepsAllReferences()
    {
        var result = CoincidentReferenceFilter.KeepIndices(new[] { 1.0, 2.0, 3.0 }, Tolerance);

        Assert.Equal(new[] { 0, 1, 2, 3 }, result);
    }

    [Fact]
    public void OneZeroSegment_DropsTheReferenceEndingIt()
    {
        var result = CoincidentReferenceFilter.KeepIndices(new[] { 1.0, 0.0, 3.0 }, Tolerance);

        Assert.Equal(new[] { 0, 1, 3 }, result);
    }

    [Fact]
    public void ThreeCoincidentReferences_CollapseToOne()
    {
        // refs 1, 2, 3 all sit at the same station: segments 1 and 2 are zero.
        var result = CoincidentReferenceFilter.KeepIndices(new[] { 1.0, 0.0, 0.0, 2.0 }, Tolerance);

        Assert.Equal(new[] { 0, 1, 4 }, result);
    }

    [Fact]
    public void ZeroSegmentAtStart_DropsSecondReference()
    {
        var result = CoincidentReferenceFilter.KeepIndices(new[] { 0.0, 2.0 }, Tolerance);

        Assert.Equal(new[] { 0, 2 }, result);
    }

    [Fact]
    public void AllSegmentsZero_KeepsOnlyFirstReference()
    {
        var result = CoincidentReferenceFilter.KeepIndices(new[] { 0.0, 0.0 }, Tolerance);

        Assert.Equal(new[] { 0 }, result);
    }

    [Fact]
    public void SegmentJustBelowTolerance_IsTreatedAsZero()
    {
        var result = CoincidentReferenceFilter.KeepIndices(new[] { 0.0005, 5.0 }, Tolerance);

        Assert.Equal(new[] { 0, 2 }, result);
    }
}
