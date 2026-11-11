using SideWar.Core;
using SideWar.Platform.Windows.Interop;
using SideWar.Platform.Windows.Threading;
using SideWar.Platform.Windows.Windows;
using SideWar.Runtime;
using SideWar.Runtime.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Xunit;

namespace SideWar.Platform.Windows.Tests;

[Trait("Env", "WindowsInteractive")]
public sealed class OverlayWindowTests
{
    private sealed class IdleHost : IGameLoopHost
    {
        public long? NextDeadline => null;
        public void OnWake(IClock clock) { }
        public void OnCommand(IGameCommand command) { }
        public void Shutdown() { }
    }

    private static async Task<(GameThread Game, List<Exception> Errors)> Start()
    {
        var errors = new List<Exception>();
        var game = new GameThread(new IdleHost(), e => { lock (errors) errors.Add(e); }, _ => { });
        await game.StartAsync();
        return (game, errors);
    }

    private static Task<T> OnGame<T>(GameThread game, Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        game.Post(() => { try { tcs.SetResult(func()); } catch (Exception e) { tcs.SetException(e); } });
        return tcs.Task;
    }

    private static uint Ex(HWND h) => (uint)WindowLong.Get(h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
    private static LPARAM Pt(int x, int y) => new((y << 16) | (x & 0xFFFF));

    private static OverlayPlacement PlaceAt(OverlayKind kind, int left, int top)
    {
        var m = new MonitorInfo("T", new PixelRect(0, 0, 3000, 2000), new PixelRect(0, 0, 3000, 2000), 96, true, 1);
        var p = PanelGeometry.Compute(kind, m, new PixelRect(0, 1904, 3000, 2000));
        var b = new PixelRect(left, top, left + p.Bounds.Width, top + p.Bounds.Height);
        return p with { Bounds = b };
    }

    [Fact]
    public async Task StylesNoOwnerNoActivationAndVisibilityFollowTheScene() // CA-24.1, T-24.1
    {
        var (game, errors) = await Start();
        try
        {
            var r = await OnGame(game, () =>
            {
                var scene = game.Scene;
                scene.SetBounds(new PixelRect(0, 1904, 800, 2000));
                var wins = new OverlayWindow[] { scene.GetOrCreatePanel(), scene.GetOrCreateQuestionCard(), scene.GetOrCreateOfflineCard() };
                var kinds = new[] { OverlayKind.Panel, OverlayKind.QuestionCard, OverlayKind.OfflineCard };
                var result = new List<(uint Ex, uint Style, bool Owner, nint Mouse, bool Alone, bool Shown, bool Hidden)>();
                for (var i = 0; i < wins.Length; i++)
                {
                    wins[i].Apply(PlaceAt(kinds[i], 100 + 50 * i, 100));
                    var h = new HWND(wins[i].Handle);
                    wins[i].Show();
                    var alone = PInvoke.IsWindowVisible(h);
                    var fg = PInvoke.GetForegroundWindow();
                    scene.Show();
                    var shown = PInvoke.IsWindowVisible(h);
                    var mouse = PInvoke.SendMessage(h, PInvoke.WM_MOUSEACTIVATE, 0, 0).Value;
                    var fgSame = PInvoke.GetForegroundWindow() == fg;
                    scene.Hide();
                    var hidden = !PInvoke.IsWindowVisible(h);
                    result.Add((Ex(h), (uint)WindowLong.Get(h, WINDOW_LONG_PTR_INDEX.GWL_STYLE),
                        !PInvoke.GetWindow(h, GET_WINDOW_CMD.GW_OWNER).IsNull, mouse, alone, shown && fgSame, hidden));
                }
                return result;
            });
            const uint need = (uint)(WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOPMOST);
            foreach (var w in r)
            {
                Assert.Equal(need, w.Ex & need);
                Assert.Equal(0u, w.Ex & (uint)WINDOW_EX_STYLE.WS_EX_TRANSPARENT);
                Assert.NotEqual(0u, w.Style & (uint)WINDOW_STYLE.WS_POPUP);
                Assert.False(w.Owner);
                Assert.Equal((nint)PInvoke.MA_NOACTIVATE, w.Mouse);
                Assert.False(w.Alone);
                Assert.True(w.Shown);
                Assert.True(w.Hidden);
            }
            Assert.Empty(errors);
        }
        finally { await game.StopAsync(); }
    }

    [Fact]
    public async Task PressesNotifyOnceHoverAndWheelDoNotAndScrollWorks() // CA-24.9, CA-24.12, T-24.7, T-24.10
    {
        var (game, errors) = await Start();
        try
        {
            var r = await OnGame(game, () =>
            {
                var scene = game.Scene;
                scene.SetBounds(new PixelRect(0, 1904, 800, 2000));
                var panel = scene.GetOrCreatePanel();
                panel.Apply(PlaceAt(OverlayKind.Panel, 100, 100));
                panel.View.SetContent(2000, 491);
                var h = new HWND(panel.Handle);
                var presses = 0;
                var clicks = new List<PanelHit>();
                panel.Interaction = () => presses++;
                panel.Clicked = clicks.Add;
                panel.Show();
                scene.Show();
                var frames0 = panel.FrameCount;
                PInvoke.SendMessage(h, PInvoke.WM_MOUSEMOVE, 0, Pt(100, 100));
                var hoverBody = panel.View.Hover;
                PInvoke.SendMessage(h, PInvoke.WM_MOUSEMOVE, 0, Pt(100, 100));
                var framesAfterRepeatedMove = panel.FrameCount;
                PInvoke.SendMessage(h, PInvoke.WM_MOUSEWHEEL, new WPARAM((nuint)((uint)unchecked((ushort)(short)-120) << 16)), Pt(100, 100));
                var offAfterWheel = panel.View.Offset;
                var pressesAfterHoverWheel = presses;
                // Close: down+up on the same target = one press and one click.
                PInvoke.SendMessage(h, PInvoke.WM_LBUTTONDOWN, 0, Pt(360, 20));
                PInvoke.SendMessage(h, PInvoke.WM_LBUTTONUP, 0, Pt(360, 20));
                // Down on Close, up elsewhere: no click.
                PInvoke.SendMessage(h, PInvoke.WM_LBUTTONDOWN, 0, Pt(360, 20));
                PInvoke.SendMessage(h, PInvoke.WM_LBUTTONUP, 0, Pt(100, 200));
                // Right press in the shadow margin and on the track.
                PInvoke.SendMessage(h, PInvoke.WM_RBUTTONDOWN, 0, Pt(403, 100));
                // Track click scrolls a page; thumb drag moves the offset.
                PInvoke.SendMessage(h, PInvoke.WM_LBUTTONDOWN, 0, Pt(390, 500));
                PInvoke.SendMessage(h, PInvoke.WM_LBUTTONUP, 0, Pt(390, 500));
                var offAfterTrack = panel.View.Offset;
                PInvoke.SendMessage(h, PInvoke.WM_LBUTTONDOWN, 0, Pt(390, 209)); // thumb after one page: y 59+118..237
                var dragging = panel.View.Dragging;
                PInvoke.SendMessage(h, PInvoke.WM_MOUSEMOVE, 0, Pt(390, 400));
                var offAfterDrag = panel.View.Offset;
                PInvoke.SendMessage(h, PInvoke.WM_LBUTTONUP, 0, Pt(390, 400));
                // Hover leave clears.
                PInvoke.SendMessage(h, PInvoke.WM_MOUSELEAVE, 0, 0);
                return (frames0, hoverBody, framesAfterRepeatedMove, offAfterWheel, pressesAfterHoverWheel, presses, clicks, offAfterTrack, dragging, offAfterDrag, panel.View.Dragging, panel.View.Hover);
            });
            Assert.Equal(PanelHit.Body, r.hoverBody);
            Assert.Equal(r.frames0 + 1, r.framesAfterRepeatedMove); // first move repaints (hover changed); the repeat does not
            Assert.Equal(40f, r.offAfterWheel);
            Assert.Equal(0, r.pressesAfterHoverWheel);
            Assert.Equal(5, r.presses);
            Assert.Equal(new[] { PanelHit.Close }, r.clicks);
            Assert.True(r.offAfterTrack > 40f);
            Assert.True(r.dragging);
            Assert.True(r.offAfterDrag > r.offAfterTrack);
            Assert.False(r.Item11);
            Assert.Equal(PanelHit.None, r.Item12);
            Assert.Empty(errors);
        }
        finally { await game.StopAsync(); }
    }

    [Fact]
    public async Task ShadowPixelsConsumeTheClickAndNoFramesWhenStatic() // CA-24.16, T-24.13 (WindowFromPoint half), RF-24.13
    {
        var (game, errors) = await Start();
        try
        {
            var r = await OnGame(game, () =>
            {
                var scene = game.Scene;
                scene.SetBounds(new PixelRect(0, 1904, 800, 2000));
                var panel = scene.GetOrCreatePanel();
                var placement = PlaceAt(OverlayKind.Panel, 300, 200);
                panel.Apply(placement);
                panel.Show();
                scene.Show();
                var b = placement.Bounds;
                var shadowPoint = new System.Drawing.Point(b.Right - 2, b.Bottom - 2);
                var hit = PInvoke.WindowFromPoint(shadowPoint);
                var frames = panel.FrameCount;
                panel.InvalidateIfChanged();
                panel.InvalidateIfChanged();
                var moved = PlaceAt(OverlayKind.Panel, 320, 200);
                panel.Apply(moved); // same size: no render
                return (hit == new HWND(panel.Handle), frames == panel.FrameCount, panel.FrameCount, frames);
            });
            Assert.True(r.Item1);
            Assert.True(r.Item2);
            Assert.Empty(errors);
        }
        finally { await game.StopAsync(); }
    }
}
