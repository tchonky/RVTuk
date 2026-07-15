using RVTuk.Core.FamilyBrowser.Util;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser;

public class ImageCropGeometryTests
{
    [Fact]
    public void DisplayEqualsSource_FullCrop_ReturnsFullRect()
    {
        var r = ImageCropGeometry.ToPixelRect(800, 600, 800, 600, 0, 0, 800, 600);
        Assert.Equal(0, r.X);
        Assert.Equal(0, r.Y);
        Assert.Equal(800, r.Width);
        Assert.Equal(600, r.Height);
    }

    [Fact]
    public void ScaledDownDisplay_MapsCropBackToSourcePixels()
    {
        // Source 800x600 shown at 400x300 (2x). Crop centre 200x150 at (100,75) display coords.
        var r = ImageCropGeometry.ToPixelRect(800, 600, 400, 300, 100, 75, 200, 150);
        Assert.Equal(200, r.X);
        Assert.Equal(150, r.Y);
        Assert.Equal(400, r.Width);
        Assert.Equal(300, r.Height);
    }

    [Fact]
    public void CropPastRightBottomEdge_ClampsWidthHeightToBounds()
    {
        var r = ImageCropGeometry.ToPixelRect(100, 100, 100, 100, 90, 90, 50, 50);
        Assert.Equal(90, r.X);
        Assert.Equal(90, r.Y);
        Assert.Equal(10, r.Width);
        Assert.Equal(10, r.Height);
    }

    [Fact]
    public void DegenerateCrop_ClampsToMinimumOnePixel()
    {
        var r = ImageCropGeometry.ToPixelRect(100, 100, 100, 100, 0, 0, 0, 0);
        Assert.Equal(1, r.Width);
        Assert.Equal(1, r.Height);
    }

    [Fact]
    public void NonPositiveDisplay_ReturnsFullSourceRect()
    {
        var r = ImageCropGeometry.ToPixelRect(100, 80, 0, 0, 10, 10, 20, 20);
        Assert.Equal(0, r.X);
        Assert.Equal(0, r.Y);
        Assert.Equal(100, r.Width);
        Assert.Equal(80, r.Height);
    }
}
