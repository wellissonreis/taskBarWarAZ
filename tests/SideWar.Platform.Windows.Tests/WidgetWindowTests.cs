using SideWar.Core;
using SideWar.Platform.Windows.Interop;
using SideWar.Platform.Windows.Threading;
using SideWar.Runtime;
using SideWar.Runtime.Geometry;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Xunit;

namespace SideWar.Platform.Windows.Tests;

[Trait("Env", "WindowsInteractive")]
public sealed class WidgetWindowTests
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

    private static readonly WidgetPlacement Place = new(new PixelRect(20, 20, 440, 68), false);

    [Fact]
    public async Task StylesOwnerAndVisibilityFollowTheScene() // CA-23.1, T-23.1
    {
        var (game, errors) = await Start();
        try
        {
            var r = await OnGame(game, () =>
            {
                var scene = game.Scene;
                scene.SetBounds(new PixelRect(0, 0, 800, 96));
                var widget = scene.GetOrCreateWidget();
                widget.Apply(Place);
                var hwnd = new HWND(widget.Handle);
                var ex = Ex(hwnd);
                var style = (uint)WindowLong.Get(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
                var owner = PInvoke.GetWindow(hwnd, GET_WINDOW_CMD.GW_OWNER);
                var sizeOnApply = widget.Surface.Size;
                // Scene hidden: widget request is held, nothing appears alone.
                widget.Show();
                bool aloneVisible = PInvoke.IsWindowVisible(hwnd);
                var fg0 = PInvoke.GetForegroundWindow();
                scene.Show();
                bool sceneShown = PInvoke.IsWindowVisible(hwnd);
                var fg1 = PInvoke.GetForegroundWindow();
                scene.Hide();
                bool afterHide = PInvoke.IsWindowVisible(hwnd);
                scene.Show();
                bool reshown = PInvoke.IsWindowVisible(hwnd);
                widget.Hide();
                bool widgetHidden = PInvoke.IsWindowVisible(hwnd);
                return (ex, style, owner, sceneHandle: (nint)scene.Handle, sizeOnApply, aloneVisible, sceneShown,
                    afterHide, reshown, widgetHidden, fg0, fg1, widget.Bounds);
            });
            uint required = (uint)(WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW |
                WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOPMOST);
            Assert.Equal(required, r.ex & required);
            Assert.Equal(0u, r.ex & (uint)WINDOW_EX_STYLE.WS_EX_TRANSPARENT);
            Assert.NotEqual(0u, r.style & (uint)WINDOW_STYLE.WS_POPUP);
            Assert.True(r.owner == new HWND(r.sceneHandle));
            Assert.Equal(new PixelSize(420, 48), r.sizeOnApply);
            Assert.False(r.aloneVisible);
            Assert.True(r.sceneShown);
            Assert.False(r.afterHide);
            Assert.True(r.reshown);
            Assert.False(r.widgetHidden);
            Assert.Equal(r.fg0, r.fg1);
            Assert.Equal(new PixelRect(20, 20, 440, 68), r.Bounds);
        }
        finally { await game.StopAsync(); }
        Assert.Empty(errors);
        Assert.True(PInvoke.FindWindow("SideWar.Widget", (string?)null).IsNull);
    }

    [Fact]
    public async Task StaysAboveShellTrayAndCoalescesBurst() // CA-23.7, T-23.7
    {
        var (game, errors) = await Start();
        try
        {
            var tray = PInvoke.FindWindow("Shell_TrayWnd", (string?)null);
            await OnGame(game, () =>
            {
                game.Scene.SetBounds(new PixelRect(0, 0, 800, 96));
                var widget = game.Scene.GetOrCreateWidget();
                widget.Apply(Place);
                widget.Show();
                game.Scene.Show();
                return 0;
            });
            if (tray.IsNull) return; // No shell (headless session): nothing to compare against.
            Assert.True(await OnGame(game, () => IsAbove(new HWND(game.Scene.Widget!.Handle), tray)));

            // Shell raises itself (click on the bar); the burst of events triggers one reassertion.
            PInvoke.SetWindowPos(tray, HWND.HWND_TOPMOST, 0, 0, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE);
            long before = await OnGame(game, () => game.ZOrder.ReassertionCount);
            await OnGame(game, () => { for (int i = 0; i < 10; i++) game.ZOrder.Request(shellForeground: true); return 0; });
            await Task.Delay(300, TestContext.Current.CancellationToken);
            long after = await OnGame(game, () => game.ZOrder.ReassertionCount);
            Assert.Equal(before + 1, after);
            Assert.True(await OnGame(game, () => IsAbove(new HWND(game.Scene.Widget!.Handle), tray)));
        }
        finally { await game.StopAsync(); }
        Assert.Empty(errors);
    }

    // Walks the z-order from the top; the first of the two found is the upper one.
    private static bool IsAbove(HWND upper, HWND lower)
    {
        for (var h = PInvoke.GetWindow(lower, GET_WINDOW_CMD.GW_HWNDPREV); !h.IsNull;
             h = PInvoke.GetWindow(h, GET_WINDOW_CMD.GW_HWNDPREV))
            if (h == upper) return true;
        return false;
    }
}
