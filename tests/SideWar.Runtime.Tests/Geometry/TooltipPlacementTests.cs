using SideWar.Runtime.Geometry;
using Xunit;

namespace SideWar.Runtime.Tests.Geometry;

public sealed class TooltipPlacementTests
{
    [Fact]
    public void Tooltip_follows_moved_menu_element()
    {
        var work = new PixelRect(0, 0, 1920, 1080);
        var item = new PixelRect(500, 250, 600, 320);
        Assert.Equal(new PixelRect(512, 332, 742, 382), TooltipPlacement.Beside(work, item, 230, 50));
        var moved = new PixelRect(650, 330, 750, 400);
        Assert.Equal(new PixelRect(662, 412, 892, 462), TooltipPlacement.Beside(work, moved, 230, 50));
    }

    [Fact]
    public void Tooltip_flips_and_stays_inside_offset_monitor()
    {
        var work = new PixelRect(-1920, 100, 0, 1100);
        var item = new PixelRect(-90, 1030, -10, 1080);
        var result = TooltipPlacement.Beside(work, item, 230, 50);
        Assert.Equal(new PixelRect(-252, 968, -22, 1018), result);
        Assert.True(result.Left >= work.Left && result.Right <= work.Right && result.Top >= work.Top && result.Bottom <= work.Bottom);
    }
}
