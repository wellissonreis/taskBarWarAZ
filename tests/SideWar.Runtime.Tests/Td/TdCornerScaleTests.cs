using SideWar.Runtime.Td;
using SideWar.Runtime.Geometry;
using Xunit;

namespace SideWar.Runtime.Tests.Td;

public class TdCornerScaleTests
{
    [Theory]
    [InlineData(96, 100)]
    [InlineData(96, 125)]
    [InlineData(144, 175)]
    [InlineData(192, 200)]
    public void Scaled_hit_coordinates_track_visible_item(int dpi, int percent)
    {
        var scale = new TdCornerScale(dpi, percent);
        var button = new TdUiItem(new DipRect(20, 20, 40, 40), TdUiKind.Button, "Torre", TdUiAction.ChooseTower);
        var pop = new TdPopoverView("Torres", [button], 100, 100, 0);
        var hud = new TdHudView([button], 100, 100);
        // A 20..60 DIP button's centre stays inside after scaling and conversion back.
        float hit = scale.ToDip(scale.ToPixels(40));
        Assert.Equal(0, pop.HitTest(hit, hit));
        Assert.Equal(0, hud.HitTest(hit, hit));
        Assert.Equal(-1, pop.HitTest(scale.ToDip(scale.ToPixels(70)), hit));
        Assert.Equal((int)MathF.Round(100 * scale.PixelsPerDip), scale.ToPixels(100));
    }
}
