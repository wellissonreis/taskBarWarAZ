using SideWar.Runtime.Geometry;
using SideWar.Runtime.Td;
using Xunit;

namespace SideWar.Runtime.Tests.Td;

public sealed class TdRunePopoverLayoutTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void Aligns_arrow_and_keeps_clicks_on_nodes_at_each_scale(float scale)
    {
        var work = new PixelRect(0, 0, 1920, 1080);
        var hud = new PixelRect(700, 100, 1600, 160);
        var result = TdRunePopoverLayout.Place(work, hud, 373, scale, 288, 270);
        Assert.True(result.ArrowTop);
        Assert.Equal(hud.Bottom + 2, result.Bounds.Top);
        Assert.InRange(Math.Abs((result.Bounds.Left + result.ArrowX * scale) - (hud.Left + 373 * scale)), 0, 1.5f);
        var item = new TdUiItem(new DipRect(10, 10, 20, 20), TdUiKind.Button, "", TdUiAction.Close);
        var view = new TdPopoverView("", [item], 288, 270, 0, ArrowTop: true, RuneWindow: true);
        Assert.Equal(0, view.HitTest(20, 20 + TdPopoverView.ArrowH));
        Assert.Equal(-1, view.HitTest(20, 5));
    }

    [Fact]
    public void Flips_above_when_the_bottom_edge_has_no_space()
    {
        var hud = new PixelRect(700, 1000, 1600, 1040);
        var result = TdRunePopoverLayout.Place(new PixelRect(0, 0, 1920, 1080), hud, 373, 1, 288, 270);
        Assert.False(result.ArrowTop);
        Assert.Equal(hud.Top - 2, result.Bounds.Bottom);
        Assert.Equal(hud.Left + 373, result.Bounds.Left + result.ArrowX);
    }
}
