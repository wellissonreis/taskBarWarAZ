using SideWar.Runtime.Geometry;
using Xunit;

namespace SideWar.Runtime.Tests.Geometry;

public sealed class HotspotGeometryTests
{
    private static PixelRect Scene(uint dpi) => new(0, 1000, (int)(1440 * dpi / 96), 1000 + (int)dpi);

    [Fact]
    public void Banner_100Percent_Is48DipTallWithSixDipMarginAboveTheStrip() // RF-22.8, CA-22.5
    {
        var p = HotspotGeometry.Banner(Scene(96), 96, 300);
        Assert.Equal(new PixelRect(564, 994, 876, 1042), p.Window); // 312 × 48, 6 above the strip, centered
        Assert.Equal(new PixelRect(6, 6, 306, 42), p.Control);      // 300 × 36 with 6 on every side
    }

    [Fact]
    public void Banner_150Percent_ScalesAndStaysCentered()
    {
        var p = HotspotGeometry.Banner(Scene(144), 144, 450);
        Assert.Equal(72, p.Window.Height);                           // 48 × 1.5
        Assert.Equal(9, p.Control.Left);                             // ring margin 6 × 1.5
        Assert.Equal(54, p.Control.Height);                          // 36 × 1.5
        Assert.Equal(1000 - 9, p.Window.Top);
        Assert.Equal((2160 - 468) / 2, p.Window.Left);
    }

    [Fact]
    public void SupplyBox_BaseOnStripBase_AndClampedInsideTheStrip()
    {
        var p = HotspotGeometry.SupplyBox(Scene(96), 96, 1060);
        Assert.Equal(new PixelRect(1060, 1052, 1112, 1096), p.Window); // 52 × 44, top 52, base y = 96
        Assert.Equal(new PixelRect(0, 0, 52, 44), p.Control);

        Assert.Equal(1440 - 52, HotspotGeometry.SupplyBox(Scene(96), 96, 5000).Window.Left);
        Assert.Equal(0, HotspotGeometry.SupplyBox(Scene(96), 96, -30).Window.Left);

        var hi = HotspotGeometry.SupplyBox(Scene(192), 192, 100);
        Assert.Equal(new PixelRect(200, 1000 + 192 - 88, 304, 1192), hi.Window); // 104 × 88 at 200%
    }

    [Fact]
    public void SupplySprite_CenteredAndBottomAligned()
    {
        var s = HotspotGeometry.SupplySprite(new PixelRect(0, 0, 52, 44), 45, 24);
        Assert.Equal(new PixelRect(3, 20, 48, 44), s);
    }

    [Fact]
    public void Seal_CenteredAndFourDipBelowTheTop()
    {
        var r = HotspotGeometry.Seal(Scene(96), 96, 500, 30);
        Assert.Equal(new PixelRect(470, 1004, 970, 1034), r);
        Assert.Equal(1000 + 6, HotspotGeometry.Seal(Scene(144), 144, 500, 30).Top);
    }

    [Fact]
    public void HitTest_BannerIncludesRing_BoxOnlyTheControl()
    {
        var banner = HotspotGeometry.Banner(Scene(96), 96, 300);
        Assert.True(HotspotGeometry.HitTest(HotspotKind.Banner, banner, 0, 0));
        Assert.True(HotspotGeometry.HitTest(HotspotKind.Banner, banner, 311, 47));
        Assert.False(HotspotGeometry.HitTest(HotspotKind.Banner, banner, 312, 20));
        Assert.False(HotspotGeometry.HitTest(HotspotKind.Banner, banner, -1, 20));

        var box = HotspotGeometry.SupplyBox(Scene(96), 96, 100);
        Assert.True(HotspotGeometry.HitTest(HotspotKind.SupplyBox, box, 0, 0));
        Assert.True(HotspotGeometry.HitTest(HotspotKind.SupplyBox, box, 51, 43));
        Assert.False(HotspotGeometry.HitTest(HotspotKind.SupplyBox, box, 52, 10));
        Assert.False(HotspotGeometry.HitTest(HotspotKind.SupplyBox, box, 10, 44));
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HotspotGeometry.Banner(Scene(96), 0, 300));
        Assert.Throws<ArgumentOutOfRangeException>(() => HotspotGeometry.Banner(Scene(96), 96, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => HotspotGeometry.SupplyBox(Scene(96), 96, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => HotspotGeometry.Seal(Scene(96), 96, 0, 10));
    }
}
