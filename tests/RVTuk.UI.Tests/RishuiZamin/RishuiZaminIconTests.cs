using System.Windows.Media;
using System.Windows.Media.Imaging;
using RVTuk.UI.RishuiZamin.Views;
using Xunit;

namespace RVTuk.UI.Tests.RishuiZamin;

public class RishuiZaminIconTests
{
    private static Color Pixel(BitmapSource bmp, double fx, double fy)
    {
        var px = new byte[4];
        bmp.CopyPixels(new System.Windows.Int32Rect((int)(fx * bmp.PixelWidth), (int)(fy * bmp.PixelHeight), 1, 1), px, 4, 0);
        return Color.FromRgb(px[2], px[1], px[0]);   // Pbgra32
    }

    [Theory]
    [InlineData(32)]
    [InlineData(16)]
    public void Create_IsAFrozenTileOfTheSize(int size) => Sta.Run(() =>
    {
        var icon = RishuiZaminIcon.Create(size);
        Assert.Equal(size, icon.PixelWidth);
        Assert.Equal(size, icon.PixelHeight);
        Assert.True(icon.IsFrozen);
        Assert.Equal(Color.FromRgb(0x25, 0x25, 0x26), Pixel(icon, 0, 0));   // dark tile, like the other buttons
    });

    [Theory]
    [InlineData(32)]
    [InlineData(16)]
    public void Create_BlueShieldWithWhiteMenorahBase(int size) => Sta.Run(() =>
    {
        var icon = RishuiZaminIcon.Create(size);
        Assert.Equal(RishuiZaminIcon.ShieldBlue, Pixel(icon, 0.5, 0.86));   // shield, below the menorah
        Assert.Equal(Colors.White, Pixel(icon, 0.5, 0.72));                  // the menorah's base
    });
}
