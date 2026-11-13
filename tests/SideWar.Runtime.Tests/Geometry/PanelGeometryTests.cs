using SideWar.Runtime.Geometry;
using Xunit;

namespace SideWar.Runtime.Tests.Geometry;

public sealed class PanelGeometryTests
{
    private static MonitorInfo Monitor(uint dpi, int w = 1920, int h = 1080, int taskbar = 48) =>
        new("M", new PixelRect(0, 0, w, h), new PixelRect(0, 0, w, h - taskbar), dpi, true, 1);

    private static PixelRect Strip(MonitorInfo m, bool autoHide = false)
    {
        var bottom = m.WorkArea.Bottom - (autoHide ? 2 : 0);
        return new PixelRect(m.WorkArea.Left, bottom - (int)m.Dpi, m.WorkArea.Right, bottom);
    }

    [Theory] // CA-24.3, T-24.3
    [InlineData(96u)] [InlineData(120u)] [InlineData(144u)] [InlineData(168u)] [InlineData(192u)]
    public void PanelAndCardsFollowTheTable(uint dpi)
    {
        var m = Monitor(dpi, 3840, 2160, 80);
        var strip = Strip(m);
        var s = dpi / 96.0;
        int Px(double d) => (int)Math.Round(d * s, MidpointRounding.AwayFromZero);
        var shadow = Px(6);

        var p = PanelGeometry.Compute(OverlayKind.Panel, m, strip);
        Assert.Equal(new PixelSize(Px(400), Px(548)), p.Box);
        Assert.Equal(Px(400) + shadow, p.Bounds.Width);
        Assert.Equal(Px(548) + shadow, p.Bounds.Height);
        Assert.Equal(strip.Left + Px(strip.Width / s - 532), p.Bounds.Left); // ADR-034: left = W − 532
        Assert.Equal(strip.Top - Px(8), p.Bounds.Bottom - shadow);
        Assert.False(p.Clamped);
        Assert.Equal(491f, p.ViewportDip, 0.6f);

        var q = PanelGeometry.Compute(OverlayKind.QuestionCard, m, strip);
        Assert.Equal(new PixelSize(Px(520), Px(242)), q.Box);
        Assert.Equal(strip.Left + Px((strip.Width / s - 520) / 2), q.Bounds.Left);
        Assert.Equal(strip.Top - Px(8), q.Bounds.Bottom - shadow);

        var o = PanelGeometry.Compute(OverlayKind.OfflineCard, m, strip);
        Assert.Equal(new PixelSize(Px(472), Px(612)), o.Box);
        Assert.Equal(strip.Left + Px((strip.Width / s - 472) / 2), o.Bounds.Left);
    }

    [Fact]
    public void WindowSizeAt100PercentIs406x554()
    {
        var m = Monitor(96);
        var p = PanelGeometry.Compute(OverlayKind.Panel, m, Strip(m));
        Assert.Equal(406, p.Bounds.Width);
        Assert.Equal(554, p.Bounds.Height);
        Assert.Equal(6, p.ShadowPx);
    }

    [Fact]
    public void AutoHideMarginMovesStripTopAndTheBoxFollows()
    {
        var m = Monitor(96);
        var a = PanelGeometry.Compute(OverlayKind.Panel, m, Strip(m, false));
        var b = PanelGeometry.Compute(OverlayKind.Panel, m, Strip(m, true));
        Assert.Equal(a.Bounds.Top - 2, b.Bounds.Top);
    }

    [Fact]
    public void SmallScreenClampsToWorkAreaTopAndShrinksOnlyThePanel()
    {
        var m = Monitor(144, 1366, 768, 60); // work height 708, panel needs 822+9
        var strip = Strip(m);
        var p = PanelGeometry.Compute(OverlayKind.Panel, m, strip);
        Assert.True(p.Clamped);
        Assert.Equal(m.WorkArea.Top, p.Bounds.Top);
        Assert.Equal(m.WorkArea.Height, p.Bounds.Height);
        Assert.True(p.ViewportDip < 491f && p.ViewportDip > 0f);
        var o = PanelGeometry.Compute(OverlayKind.OfflineCard, m, strip); // 612 DIP = 918 px > 708: not shrunk (DP-24.12)
        Assert.Equal(m.WorkArea.Top, o.Bounds.Top);
        Assert.Equal(918, o.Box.Height);
    }

    [Fact]
    public void CardIsClampedInsideTheWorkAreaHorizontally()
    {
        var m = Monitor(96, 400, 1000);
        var p = PanelGeometry.Compute(OverlayKind.QuestionCard, m, Strip(m));
        Assert.True(p.Bounds.Left >= 0);
    }

    [Fact]
    public void InvalidDpiAndEmptyWorkAreaThrow()
    {
        var m = Monitor(96);
        Assert.Throws<ArgumentOutOfRangeException>(() => PanelGeometry.Compute(OverlayKind.Panel, m with { Dpi = 0 }, Strip(m)));
        Assert.Throws<ArgumentOutOfRangeException>(() => PanelGeometry.Compute(OverlayKind.Panel, m with { WorkArea = default }, Strip(m)));
    }
}

public sealed class PanelScrollTests
{
    [Fact] // CA-24.9, T-24.7
    public void WheelMovesBy40PerDetentWithLimits()
    {
        Assert.Equal(40f, PanelScroll.Wheel(0, -120, 2000, 491));
        Assert.Equal(0f, PanelScroll.Wheel(0, 120, 2000, 491));
        Assert.Equal(1509f, PanelScroll.Wheel(1500, -240, 2000, 491));
        Assert.Equal(0f, PanelScroll.Wheel(30, -120, 400, 491)); // content fits
        Assert.Equal(20f, PanelScroll.Wheel(0, -60, 2000, 491));
    }

    [Fact]
    public void ThumbHasMinimumAndTracksOffset()
    {
        Assert.Null(PanelScroll.Thumb(491, 491, 0));
        var t0 = PanelScroll.Thumb(10000, 491, 0)!.Value;
        Assert.Equal(44f, t0.Height);
        Assert.Equal(0f, t0.Top);
        var tEnd = PanelScroll.Thumb(10000, 491, PanelScroll.MaxOffset(10000, 491))!.Value;
        Assert.Equal(PanelScroll.TrackHeight(491) - 44f, tEnd.Top, 0.01f);
        var big = PanelScroll.Thumb(982, 491, 0)!.Value;
        Assert.Equal(483f * 0.5f, big.Height, 0.5f);
    }

    [Fact]
    public void DragAndTrackClickMoveTheOffset()
    {
        var c = 2000f; var v = 491f;
        Assert.Equal(0f, PanelScroll.OffsetFromDrag(-50, 10, c, v));
        Assert.Equal(PanelScroll.MaxOffset(c, v), PanelScroll.OffsetFromDrag(9999, 10, c, v));
        var mid = PanelScroll.OffsetFromDrag(PanelScroll.TrackHeight(v) / 2f, 0f, c, v);
        Assert.InRange(mid, 1f, PanelScroll.MaxOffset(c, v) - 1f);
        Assert.Equal(451f, PanelScroll.TrackClick(0, 400, c, v)); // below thumb: one page (491-40)
        Assert.Equal(0f, PanelScroll.TrackClick(100, 0, c, v), 0.01f + 0f); // above thumb at offset 100 -> clamped
    }

    [Fact]
    public void ViewStateRaisesVersionOnlyOnChange()
    {
        var s = new PanelViewState();
        s.SetContent(2000, 491);
        var v = s.Version;
        Assert.False(s.Wheel(120)); // already at 0
        Assert.True(s.Wheel(-120));
        Assert.Equal(40f, s.Offset);
        Assert.True(s.Version > v);
        v = s.Version;
        Assert.True(s.SetHover(PanelHit.Close));
        Assert.False(s.SetHover(PanelHit.Close));
        Assert.Equal(v + 1, s.Version);
        s.ResetOffset();
        Assert.Equal(0f, s.Offset);
        s.BeginDrag(10);
        Assert.True(s.Dragging);
        Assert.True(s.Drag(200));
        s.EndDrag();
        Assert.False(s.Dragging);
    }
}

public sealed class PanelChromeHitTests
{
    private const float H = 548f;

    [Fact] // CA-24.16 (geometry half), RF-24.11
    public void ShadowAndOutsideAreBackgroundNotNone()
    {
        Assert.Equal(PanelHit.Background, PanelChromeHit.Test(403, 100, H, 2000, 491, 0)); // right shadow
        Assert.Equal(PanelHit.Background, PanelChromeHit.Test(100, 551, H, 2000, 491, 0)); // bottom shadow
        Assert.Equal(PanelHit.Background, PanelChromeHit.Test(100, 20, H, 2000, 491, 0));  // header
    }

    [Fact]
    public void CloseIs44x44()
    {
        Assert.Equal(PanelHit.Close, PanelChromeHit.Test(346, 6, H, 100, 491, 0));
        Assert.Equal(PanelHit.Close, PanelChromeHit.Test(389.9f, 49.9f, H, 100, 491, 0));
        Assert.NotEqual(PanelHit.Close, PanelChromeHit.Test(390, 20, H, 100, 491, 0));
        Assert.NotEqual(PanelHit.Close, PanelChromeHit.Test(360, 50, H, 100, 491, 0));
    }

    [Fact]
    public void BodyIsClippedToTheViewportAndScrollbarOnlyWhenContentOverflows()
    {
        Assert.Equal(PanelHit.Body, PanelChromeHit.Test(100, 55, H, 100, 491, 0));
        Assert.Equal(PanelHit.Body, PanelChromeHit.Test(100, 545.9f, H, 100, 491, 0));
        Assert.Equal(PanelHit.Background, PanelChromeHit.Test(100, 54, H, 100, 491, 0));
        Assert.Equal(PanelHit.Background, PanelChromeHit.Test(100, 546, H, 100, 491, 0));
        Assert.Equal(PanelHit.Body, PanelChromeHit.Test(390, 100, H, 100, 491, 0)); // no scrollbar
        Assert.Equal(PanelHit.ScrollThumb, PanelChromeHit.Test(390, 70, H, 2000, 491, 0));
        Assert.Equal(PanelHit.ScrollTrack, PanelChromeHit.Test(390, 500, H, 2000, 491, 0));
        Assert.Equal(PanelHit.Body, PanelChromeHit.Test(381, 500, H, 2000, 491, 0));
    }
}
