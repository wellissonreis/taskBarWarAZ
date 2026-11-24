using SideWar.Runtime.Geometry;
using SideWar.Runtime.Td;
using Xunit;

namespace SideWar.Runtime.Tests.Td;

public sealed class TdModalLayoutTests
{
    [Theory]
    [InlineData(1f, 720, 480)]
    [InlineData(1.5f, 1080, 720)]
    public void Modal_uses_band_monitor_origin_and_dpi(float scale, int width, int height)
    {
        var work = new PixelRect(-2560, -200, 0, 1200);
        var bounds = TdModalLayout.Bounds(work, scale);
        Assert.Equal(width, bounds.Width);
        Assert.Equal(height, bounds.Height);
        Assert.Equal(work.Left + (work.Width - width) / 2, bounds.Left);
        Assert.Equal(work.Top + (work.Height - height) / 2, bounds.Top);
    }

    [Fact]
    public void Small_monitor_limits_modal_to_ninety_percent()
    {
        var work = new PixelRect(100, 100, 700, 500);
        var bounds = TdModalLayout.Bounds(work, 1.5f);
        Assert.InRange(bounds.Width, 1, 540);
        Assert.InRange(bounds.Height, 1, 360);
    }

    [Fact]
    public void Offset_moves_the_modal_and_is_clamped_inside_the_work_area()
    {
        var work = new PixelRect(0, 0, 1920, 1040);
        var centered = TdModalLayout.Bounds(work, 1f);
        var moved = TdModalLayout.Bounds(work, 1f, 100, -50);
        Assert.Equal(centered.Left + 100, moved.Left);
        Assert.Equal(centered.Top - 50, moved.Top);
        var far = TdModalLayout.Bounds(work, 1f, 99999, 99999);
        Assert.Equal(work.Right, far.Right);
        Assert.Equal(work.Bottom, far.Bottom);
        var near = TdModalLayout.Bounds(work, 1f, -99999, -99999);
        Assert.Equal(work.Left, near.Left);
        Assert.Equal(work.Top, near.Top);
    }

    [Fact]
    public void Saved_offset_from_a_bigger_monitor_falls_back_inside_a_smaller_one()
    {
        var small = new PixelRect(0, 0, 1280, 700);
        var b = TdModalLayout.Bounds(small, 1f, 1800, 900);
        Assert.True(b.Left >= small.Left && b.Right <= small.Right && b.Top >= small.Top && b.Bottom <= small.Bottom);
    }

    [Fact]
    public void Drag_header_is_the_top_strip_of_the_modal()
    {
        Assert.True(TdModalLayout.InDragHeader(300, 20));
        Assert.False(TdModalLayout.InDragHeader(300, 60));
        Assert.False(TdModalLayout.InDragHeader(-1, 20));
    }

    [Fact]
    public void Modal_suspends_external_absence_until_closed()
    {
        var focus = new TdFocusState();
        focus.OnExternalInput();
        Assert.True(focus.Absent);
        focus.SetModalOpen(true);
        focus.OnExternalInput();
        focus.OnForeignActivated();
        Assert.True(focus.IsPresent);
        Assert.Equal(255, focus.Alpha);
        focus.SetModalOpen(false);
        focus.OnExternalInput();
        Assert.True(focus.Absent);
    }
}
