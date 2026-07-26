using RVTuk.Core.AutoDimensions;
using Xunit;

namespace RVTuk.Core.Tests.AutoDimensions;

public class CategoryMaskTests
{
    [Fact]
    public void DefaultIsWallsDoorsWindows()
    {
        Assert.Equal(
            DimensionCategories.Walls | DimensionCategories.Doors | DimensionCategories.Windows,
            CategoryMask.Default);
    }

    [Fact]
    public void RoundTripsEverySupportedCombination()
    {
        var categories = DimensionCategories.Walls | DimensionCategories.Windows;

        var restored = CategoryMask.FromMask(CategoryMask.ToMask(categories));

        Assert.Equal(categories, restored);
    }

    [Fact]
    public void ClampsV2CategoriesOff()
    {
        // A stored mask that somehow carries Ceilings/Floors must never switch them on:
        // no v1 reference resolution exists for host objects.
        var stored = CategoryMask.ToMask(
            DimensionCategories.Walls | DimensionCategories.Ceilings | DimensionCategories.Floors);

        var restored = CategoryMask.FromMask(stored);

        Assert.Equal(DimensionCategories.Walls, restored);
    }

    [Fact]
    public void ClampsUnknownBitsOff()
    {
        var restored = CategoryMask.FromMask(0x7FFF_FFFF);

        Assert.Equal(CategoryMask.Supported, restored);
    }

    [Fact]
    public void ZeroMaskDecodesToNone()
    {
        Assert.Equal(DimensionCategories.None, CategoryMask.FromMask(0));
    }
}
