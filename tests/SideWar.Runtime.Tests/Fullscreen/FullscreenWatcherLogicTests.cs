using SideWar.Runtime.Fullscreen;
using SideWar.Runtime.Geometry;
using Xunit;

namespace SideWar.Runtime.Tests.Fullscreen;

public sealed class FullscreenWatcherLogicTests
{
    private const uint Own = 1;
    private static readonly PixelRect M1 = new(0, 0, 1920, 1080);
    private static readonly PixelRect M2 = new(1920, 0, 3840, 1080);

    private sealed class FakeProbe : IWindowProbe
    {
        public nint Foreground;
        public Quns Quns = Quns.AcceptsNotifications;
        public readonly Dictionary<nint, WindowSnapshot> Windows = new();
        public readonly List<nint> ZOrder = new(); // top first
        public nint GetForeground() => Foreground;
        public bool IsWindow(nint h) => Windows.ContainsKey(h);
        public WindowSnapshot Capture(nint h) => Windows.TryGetValue(h, out var w) ? w : default;
        public Quns QueryQuns() => Quns;
        public bool IsAbove(nint upper, nint lower) => ZOrder.IndexOf(upper) is var a and >= 0 && ZOrder.IndexOf(lower) is var b and >= 0 && a < b;

        public void Add(nint h, string cls, PixelRect frame, string monitor, uint style = 0x80000000, bool zoomed = false, uint pid = 100)
        {
            var mb = monitor == "M2" ? M2 : M1;
            Windows[h] = new WindowSnapshot(h, pid, cls, style, 0, zoomed, false, true, false, frame, monitor, mb, mb);
            ZOrder.Add(h);
        }
    }

    private static WindowSnapshot With(WindowSnapshot w, Func<WindowSnapshot, WindowSnapshot> f) => f(w);

    // ---- Tracker ----

    [Fact]
    public void FullscreenForegroundIsTrackedOnItsMonitor()
    {
        var p = new FakeProbe();
        p.Add(10, "Chrome_WidgetWin_1", M1, "M1");
        p.Foreground = 10;
        var t = new FullscreenTracker(p, Own);
        Assert.Equal(["M1"], t.Evaluate());
    }

    [Fact]
    public void MaximizedWindowIsNotTracked()
    {
        var p = new FakeProbe();
        p.Add(10, "Chrome_WidgetWin_1", new PixelRect(0, 0, 1920, 1040), "M1", 0x00C00000 | 0x80000000, zoomed: true);
        p.Foreground = 10;
        Assert.Empty(new FullscreenTracker(p, Own).Evaluate());
    }

    [Fact]
    public void FullscreenOnMonitor2SurvivesFocusOnMonitor1()
    {
        var p = new FakeProbe();
        p.Add(20, "Video", M2, "M2");
        p.Add(10, "Editor", new PixelRect(100, 100, 900, 700), "M1", 0x00C00000);
        var t = new FullscreenTracker(p, Own);
        p.Foreground = 20; Assert.Equal(["M2"], t.Evaluate());
        p.Foreground = 10; Assert.Equal(["M2"], t.Evaluate());
    }

    [Fact]
    public void FocusOnOtherWindowOnSameMonitorUntracksWhenAbove()
    {
        var p = new FakeProbe();
        p.Add(20, "Video", M1, "M1");
        p.Add(10, "Editor", new PixelRect(100, 100, 900, 700), "M1", 0x00C00000);
        var t = new FullscreenTracker(p, Own);
        p.Foreground = 20; t.Evaluate();
        p.ZOrder.Remove(10); p.ZOrder.Insert(0, 10); p.Foreground = 10;
        Assert.Empty(t.Evaluate());
    }

    [Fact]
    public void SameMonitorFocusBelowInZOrderKeepsTracking()
    {
        var p = new FakeProbe();
        p.Add(20, "Video", M1, "M1");
        p.Add(10, "Editor", new PixelRect(100, 100, 900, 700), "M1", 0x00C00000);
        var t = new FullscreenTracker(p, Own);
        p.Foreground = 20; t.Evaluate();
        p.Foreground = 10; // 20 stays first in ZOrder: foreground is not above
        Assert.Equal(["M1"], t.Evaluate());
    }

    [Fact]
    public void ShellTransientForegroundKeepsState()
    {
        var p = new FakeProbe();
        p.Add(20, "Video", M1, "M1");
        p.Add(30, "Windows.UI.Core.CoreWindow", new PixelRect(0, 500, 600, 1080), "M1", 0x80000000);
        var t = new FullscreenTracker(p, Own);
        p.Foreground = 20; t.Evaluate();
        p.ZOrder.Remove(30); p.ZOrder.Insert(0, 30); p.Foreground = 30;
        Assert.Equal(["M1"], t.Evaluate());
    }

    [Fact]
    public void DesktopForegroundUntracks()
    {
        var p = new FakeProbe();
        p.Add(20, "Video", M1, "M1");
        p.Add(40, "Progman", M1, "M1", 0x80000000);
        var t = new FullscreenTracker(p, Own);
        p.Foreground = 20; t.Evaluate();
        p.ZOrder.Remove(40); p.ZOrder.Insert(0, 40); p.Foreground = 40;
        Assert.Empty(t.Evaluate());
    }

    [Fact]
    public void DestroyedWindowIsUntracked()
    {
        var p = new FakeProbe();
        p.Add(20, "Video", M1, "M1");
        p.Add(10, "Editor", new PixelRect(100, 100, 900, 700), "M2", 0x00C00000);
        var t = new FullscreenTracker(p, Own);
        p.Foreground = 20; t.Evaluate();
        p.Foreground = 10; p.Windows.Remove(20);
        Assert.Empty(t.Evaluate());
    }

    [Theory]
    [InlineData("iconic")]
    [InlineData("cloaked")]
    [InlineData("hidden")]
    [InlineData("zoomed")]
    [InlineData("shrunk")]
    public void TrackedWindowThatChangesStateIsUntracked(string change)
    {
        var p = new FakeProbe();
        p.Add(20, "Video", M1, "M1");
        p.Add(10, "Editor", new PixelRect(2000, 100, 2900, 700), "M2", 0x00C00000);
        var t = new FullscreenTracker(p, Own);
        p.Foreground = 20; t.Evaluate();
        p.Foreground = 10;
        p.Windows[20] = With(p.Windows[20], w => change switch
        {
            "iconic" => w with { IsIconic = true },
            "cloaked" => w with { IsCloaked = true },
            "hidden" => w with { IsVisible = false },
            "zoomed" => w with { IsZoomed = true },
            _ => w with { Frame = new PixelRect(0, 0, 1000, 700) },
        });
        Assert.Empty(t.Evaluate());
    }

    [Fact]
    public void OwnProcessForegroundIsIgnored()
    {
        var p = new FakeProbe();
        p.Add(20, "Video", M1, "M1");
        p.Add(50, "HwndWrapper", M1, "M1", pid: Own);
        var t = new FullscreenTracker(p, Own);
        p.Foreground = 20; t.Evaluate();
        p.ZOrder.Remove(50); p.ZOrder.Insert(0, 50); p.Foreground = 50;
        Assert.Equal(["M1"], t.Evaluate());
    }

    [Fact]
    public void ZeroForegroundDoesNotRegister()
    {
        var p = new FakeProbe();
        Assert.Empty(new FullscreenTracker(p, Own).Evaluate());
    }

    [Fact]
    public void D3DExclusiveWindowNotCoveringIsTrackedWhileForeground()
    {
        var p = new FakeProbe();
        p.Add(60, "Game", new PixelRect(0, 0, 1280, 720), "M1", 0x00C00000);
        p.Quns = Quns.D3DFullscreen; p.Foreground = 60;
        var t = new FullscreenTracker(p, Own);
        Assert.Equal(["M1"], t.Evaluate());
        Assert.Equal(["M1"], t.Evaluate());
    }

    // ---- Policy (hysteresis) ----

    private static HashSet<string> Set(params string[] m) => new HashSet<string>(m);
    private static long Ms(FakeClock c, int ms) => (long)(c.TicksPerSecond * ms / 1000);

    [Fact]
    public void EnterRequires250msStable()
    {
        var c = new FakeClock();
        var pol = new FullscreenPolicy(c, HideOnFullscreen.SameMonitor, "M1");
        Assert.Null(pol.Update(Set("M1")));
        c.AdvanceTicks(Ms(c, 249)); Assert.Null(pol.Tick());
        c.AdvanceTicks(Ms(c, 1));
        Assert.Equal(FullscreenTransition.Entered, pol.Tick());
        Assert.True(pol.Hidden);
    }

    [Fact]
    public void ExitRequires600msStable()
    {
        var c = new FakeClock();
        var pol = new FullscreenPolicy(c, HideOnFullscreen.SameMonitor, "M1");
        pol.Update(Set("M1")); c.AdvanceTicks(Ms(c, 250)); Assert.NotNull(pol.Tick());
        Assert.Null(pol.Update(Set()));
        c.AdvanceTicks(Ms(c, 599)); Assert.Null(pol.Tick());
        c.AdvanceTicks(Ms(c, 1));
        Assert.Equal(FullscreenTransition.Exited, pol.Tick());
    }

    [Fact]
    public void BurstShorterThanDelayProducesNoTransition()
    {
        var c = new FakeClock();
        var pol = new FullscreenPolicy(c, HideOnFullscreen.SameMonitor, "M1");
        for (int i = 0; i < 10; i++)
        {
            pol.Update(Set("M1")); c.AdvanceTicks(Ms(c, 100));
            Assert.Null(pol.Update(Set())); c.AdvanceTicks(Ms(c, 100));
        }
        Assert.False(pol.Hidden);
        Assert.Null(pol.NextDeadline);
    }

    [Theory]
    [InlineData(HideOnFullscreen.SameMonitor, "M1", "M2", false)]
    [InlineData(HideOnFullscreen.SameMonitor, "M2", "M2", true)]
    [InlineData(HideOnFullscreen.AnyMonitor, "M1", "M2", true)]
    [InlineData(HideOnFullscreen.Never, "M2", "M2", false)]
    public void ModesDecideShouldHide(HideOnFullscreen mode, string overlay, string fullscreen, bool expected)
    {
        var pol = new FullscreenPolicy(new FakeClock(), mode, overlay);
        pol.Update(Set(fullscreen));
        Assert.Equal(expected, pol.ShouldHide);
    }

    [Fact]
    public void PendingDeadlineIsReported()
    {
        var c = new FakeClock();
        var pol = new FullscreenPolicy(c, HideOnFullscreen.SameMonitor, "M1");
        pol.Update(Set("M1"));
        Assert.Equal(c.NowTicks + Ms(c, 250), pol.NextDeadline);
    }

    // ---- Controller ----

    private static (FullscreenController Ctl, FakeProbe Probe, FakeClock Clock, List<FullscreenTransition> Events) Make(string overlay = "M1")
    {
        var p = new FakeProbe(); var c = new FakeClock();
        var ctl = new FullscreenController(c, p, Own, HideOnFullscreen.SameMonitor, overlay);
        var ev = new List<FullscreenTransition>();
        ctl.Transition += ev.Add;
        return (ctl, p, c, ev);
    }

    [Fact]
    public void SignalsCoalesceInto100msDeadline()
    {
        var (ctl, _, c, _) = Make();
        ctl.Signal();
        var first = ctl.NextDeadline;
        c.AdvanceTicks(Ms(c, 50)); ctl.Signal();
        Assert.Equal(c.NowTicks - Ms(c, 50) + Ms(c, 100), Math.Min(first!.Value, ctl.NextDeadline!.Value));
        Assert.Equal(first, ctl.NextDeadline);
    }

    [Fact]
    public void FullscreenEntersAfterCoalesceAndHysteresis()
    {
        var (ctl, p, c, ev) = Make();
        p.Add(20, "Video", M1, "M1"); p.Foreground = 20;
        ctl.Signal();
        c.AdvanceTicks(Ms(c, 100)); ctl.OnWake();
        Assert.Empty(ev);
        Assert.Equal(c.NowTicks + Ms(c, 250), ctl.NextDeadline);
        c.AdvanceTicks(Ms(c, 250)); ctl.OnWake();
        Assert.Equal([FullscreenTransition.Entered], ev);
    }

    [Fact]
    public void F11TwentyTimesYieldsOneTransitionPerToggle()
    {
        var (ctl, p, c, ev) = Make();
        p.Add(20, "Chrome", new PixelRect(0, 0, 1000, 700), "M1", 0x00C00000, zoomed: false);
        p.Foreground = 20;
        var full = p.Windows[20] with { Frame = M1, Style = 0x80000000 };
        var windowed = p.Windows[20];
        for (int i = 0; i < 20; i++)
        {
            p.Windows[20] = full; ctl.Signal();
            for (int t = 0; t < 10; t++) { c.AdvanceTicks(Ms(c, 100)); ctl.OnWake(); }
            p.Windows[20] = windowed; ctl.Signal();
            for (int t = 0; t < 10; t++) { c.AdvanceTicks(Ms(c, 100)); ctl.OnWake(); }
        }
        Assert.Equal(40, ev.Count);
        Assert.False(ctl.Hidden);
        for (int i = 0; i < ev.Count; i++) Assert.Equal(i % 2 == 0 ? FullscreenTransition.Entered : FullscreenTransition.Exited, ev[i]);
    }

    [Fact]
    public void SameMonitorModeIgnoresFullscreenOnOtherMonitorAndFollowsOverlayMonitor()
    {
        var (ctl, p, c, ev) = Make("M1");
        p.Add(20, "Video", M2, "M2"); p.Foreground = 20;
        ctl.Signal(); c.AdvanceTicks(Ms(c, 100)); ctl.OnWake(); c.AdvanceTicks(Ms(c, 1000)); ctl.OnWake();
        Assert.Empty(ev);
        ctl.Reconfigure(HideOnFullscreen.SameMonitor, "M2");
        c.AdvanceTicks(Ms(c, 250)); ctl.OnWake();
        Assert.Equal([FullscreenTransition.Entered], ev);
        // focus moves to monitor 1: still hidden (CA-06.4)
        p.Add(10, "Editor", new PixelRect(100, 100, 900, 700), "M1", 0x00C00000);
        p.Foreground = 10; ctl.Signal(); c.AdvanceTicks(Ms(c, 100)); ctl.OnWake();
        c.AdvanceTicks(Ms(c, 2000)); ctl.OnWake();
        Assert.True(ctl.Hidden);
        Assert.Single(ev);
    }

    [Fact]
    public void PollingIntervalsFollowState()
    {
        var (ctl, p, c, _) = Make();
        var start = c.NowTicks;
        Assert.Equal(start + Ms(c, 2000), ctl.NextDeadline);
        ctl.HiddenForOtherReason = true;
        Assert.Null(ctl.NextDeadline);
        ctl.HiddenForOtherReason = false;
        p.Add(20, "Video", M1, "M1"); p.Foreground = 20;
        ctl.Signal(); c.AdvanceTicks(Ms(c, 100)); ctl.OnWake();
        Assert.True(ctl.Tracker.HasTracked);
        Assert.Equal(c.NowTicks + Ms(c, 250), ctl.NextDeadline); // hysteresis earlier than 1 s poll
        c.AdvanceTicks(Ms(c, 250)); ctl.OnWake();
        Assert.Equal(c.NowTicks + Ms(c, 750), ctl.NextDeadline); // 1 s poll since last evaluate (250 ms ago)
    }

    [Fact]
    public void PollingReevaluatesWithoutSignals()
    {
        var (ctl, p, c, ev) = Make();
        p.Add(20, "Video", M1, "M1"); p.Foreground = 20;
        c.AdvanceTicks(Ms(c, 2000)); ctl.OnWake();
        c.AdvanceTicks(Ms(c, 250)); ctl.OnWake();
        Assert.Equal([FullscreenTransition.Entered], ev);
    }
}
