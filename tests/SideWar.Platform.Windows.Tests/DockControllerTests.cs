using System.Diagnostics;
using SideWar.Core;
using SideWar.Platform.Windows.Rendering;
using SideWar.Platform.Windows.Threading;
using SideWar.Platform.Windows.Windows;
using SideWar.Runtime;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Taskbar;
using Windows.Win32;
using Windows.Win32.Foundation;
using Xunit;

namespace SideWar.Platform.Windows.Tests;

[Trait("Env", "WindowsInteractive")]
public sealed class DockControllerTests
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

    private static MonitorInfo Mon(string name, int left, uint dpi, bool primary, int w = 1920, int h = 1080)
    {
        var bounds = new PixelRect(left, 0, left + w, h);
        var bar = (int)(48 * dpi / 96.0);
        return new(name, bounds, new PixelRect(left, 0, left + w, h - bar), dpi, primary, 0);
    }

    private static TaskbarLayout Bar(string device, bool autoHide = false) =>
        new(device, TaskbarEdge.Bottom, autoHide, new DipRect(0, 1032, 1920, 48), 48, [], null, (10, 700), [], 1900,
            TaskbarAlignment.Left, TaskbarSource.Uia, 96);

    private static PixelRect WindowRect(nint hwnd)
    {
        PInvoke.GetWindowRect(new HWND(hwnd), out var r);
        return new PixelRect(r.left, r.top, r.right, r.bottom);
    }

    private static void Notify(nint hwnd, uint message, nuint wParam = 0) =>
        PInvoke.SendMessage(new HWND(hwnd), message, new WPARAM(wParam), default);

    private sealed record Fixture(DockController Dock, SceneWindow Scene, WidgetWindow Widget, LayeredSurface Surface,
        Func<MonitorInfo[]> Monitors, Action<Func<MonitorInfo[]>> SetMonitors);

    private static Fixture Setup(GameThread game, params MonitorInfo[] initial)
    {
        Func<MonitorInfo[]> monitors = () => initial;
        var scene = game.Scene;
        var dock = game.Dock;
        dock.Enumerate = () => monitors();
        var surface = new LayeredSurface(scene.Handle);
        dock.SceneSurface = surface;
        var widget = scene.GetOrCreateWidget();
        return new Fixture(dock, scene, widget, surface, () => monitors(), f => monitors = f);
    }

    [Theory]
    [InlineData(96u)]
    [InlineData(144u)]
    [InlineData(192u)]
    public async Task RelayoutAppliesSceneLayoutAndWidgetGeometry(uint dpi) // CA-05.2 (geometry), CA-05.1 reuse
    {
        var (game, errors) = await Start();
        try
        {
            var r = await OnGame(game, () =>
            {
                var m = Mon("A", 0, dpi, true);
                var f = Setup(game, m);
                f.Dock.SetTaskbar(Bar("A"));
                var result = f.Dock.Relayout();
                var expectedScene = SceneLayout.Compute(m, Bar("A"), TowerHost.Band);
                var expectedWidget = WidgetGeometry.Compute(m, expectedScene, Bar("A"));
                return (result, expectedScene, expectedWidget, SceneRect: WindowRect(f.Scene.Handle),
                    WidgetRect: WindowRect(f.Widget.Handle), f.Surface.Size, WSize: f.Widget.Surface.Size, m.WorkArea);
            });
            Assert.Equal(r.expectedScene, r.result.Scene);
            Assert.Equal(r.expectedScene, r.SceneRect);
            Assert.Equal(new PixelSize(r.expectedScene.Width, r.expectedScene.Height), r.Size);
            Assert.Equal(r.expectedWidget, r.result.Widget);
            Assert.Equal(r.expectedWidget.Bounds, r.WidgetRect);
            Assert.Equal(new PixelSize(r.expectedWidget.Bounds.Width, r.expectedWidget.Bounds.Height), r.WSize);
            Assert.True(r.SceneRect.Bottom <= r.WorkArea.Bottom && r.SceneRect.Left >= r.WorkArea.Left); // RNF-05.3
            Assert.Empty(errors);
        }
        finally { await game.StopAsync(); }
    }

    [Fact]
    public async Task DpiChangedRecalculatesDebouncedBurstInUnderOneSecond() // CA-05.3
    {
        var (game, errors) = await Start();
        try
        {
            var f = await OnGame(game, () =>
            {
                var fx = Setup(game, Mon("A", 0, 96, true));
                fx.Dock.SetTaskbar(Bar("A"));
                fx.Dock.Relayout(); // activates the controller (App does this at startup)
                fx.Scene.Show();
                return fx;
            });
            var before = await OnGame(game, () => (f.Dock.RelayoutCount, f.Dock.Current!.Scene, f.Dock.Current.Widget.Bounds));
            var sw = Stopwatch.StartNew();
            await OnGame(game, () =>
            {
                f.SetMonitors(() => [Mon("A", 0, 192, true)]);
                for (var i = 0; i < 10; i++) Notify(f.Scene.Handle, PInvoke.WM_DPICHANGED, 0xC0C0C0C0);
                return 0;
            });
            DockLayoutResult? applied = null;
            while (sw.ElapsedMilliseconds < 1000)
            {
                applied = await OnGame(game, () => f.Dock.Current);
                if (applied!.Monitor.Dpi == 192) break;
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
            Assert.Equal(192u, applied!.Monitor.Dpi);
            Assert.True(sw.ElapsedMilliseconds < 1000, $"{sw.ElapsedMilliseconds} ms");
            var after = await OnGame(game, () => (f.Dock.RelayoutCount, WindowRect(f.Scene.Handle), WindowRect(f.Widget.Handle),
                f.Surface.Size, f.Widget.Surface.Size));
            Assert.Equal(before.RelayoutCount + 1, after.RelayoutCount); // burst of 10 = one relayout
            Assert.Equal(192, after.Item2.Height);
            Assert.Equal(applied.Scene, after.Item2);
            Assert.Equal(applied.Widget.Bounds, after.Item3);
            Assert.Equal(new PixelSize(after.Item2.Width, 192), after.Item4);
            Assert.Equal(new PixelSize(840, 96), after.Item5);
            Assert.Empty(errors);
        }
        finally { await game.StopAsync(); }
    }

    [Fact]
    public async Task DisplayChangeFallsBackToPrimaryAndDoesNotReturnAutomatically() // CA-05.4
    {
        var (game, errors) = await Start();
        try
        {
            var a = Mon("A", 0, 96, true);
            var b = Mon("B", 1920, 144, false);
            var f = await OnGame(game, () =>
            {
                var fx = Setup(game, a, b);
                fx.Scene.Show();
                fx.Dock.MoveToMonitor(b); // "Mover para o monitor N"
                return fx;
            });
            var onB = await OnGame(game, () => (f.Dock.Current!.Monitor.DeviceName, WindowRect(f.Scene.Handle)));
            Assert.Equal("B", onB.DeviceName);
            Assert.Equal(1920, onB.Item2.Left);
            Assert.Equal(144, onB.Item2.Height); // CA-05.2: scene is 96 DIP at the target monitor DPI
            await OnGame(game, () => { f.SetMonitors(() => [a]); Notify(f.Scene.Handle, PInvoke.WM_DISPLAYCHANGE); return 0; });
            await Task.Delay(500, TestContext.Current.CancellationToken);
            var onA = await OnGame(game, () => (f.Dock.Current!.Monitor.DeviceName, WindowRect(f.Scene.Handle), f.Dock.MonitorHint));
            Assert.Equal("A", onA.DeviceName);
            Assert.Equal(96, onA.Item2.Height);
            Assert.Equal(0, onA.Item2.Left);
            // Reconnected with the same name and bounds: the hint still names B, but nothing is re-evaluated
            // until the next change; only an explicit message would pick it up (no automatic return at runtime).
            await OnGame(game, () => { f.SetMonitors(() => [a, b]); return 0; });
            await Task.Delay(400, TestContext.Current.CancellationToken);
            Assert.Equal("A", (await OnGame(game, () => f.Dock.Current!.Monitor.DeviceName)));
            Assert.Empty(errors);
        }
        finally { await game.StopAsync(); }
    }

    [Fact]
    public async Task WorkAreaChangeAndTaskbarAutoHideRelayout() // CA-05.5
    {
        var (game, errors) = await Start();
        try
        {
            var f = await OnGame(game, () =>
            {
                var fx = Setup(game, Mon("A", 0, 96, true));
                fx.Dock.SetTaskbar(Bar("A"));
                fx.Dock.Relayout(); // activates the controller (App does this at startup)
                fx.Scene.Show();
                return fx;
            });
            var normal = await OnGame(game, () => f.Dock.Current!.Scene);
            var sw = Stopwatch.StartNew();
            await OnGame(game, () =>
            {
                // Auto-hide on: work area grows to the full monitor; shell sends SPI_SETWORKAREA.
                f.SetMonitors(() => [new MonitorInfo("A", new(0, 0, 1920, 1080), new(0, 0, 1920, 1080), 96, true, 0)]);
                f.Dock.SetTaskbar(Bar("A", autoHide: true));
                Notify(f.Scene.Handle, PInvoke.WM_SETTINGCHANGE, 0x2F);
                return 0;
            });
            PixelRect moved = normal;
            while (sw.ElapsedMilliseconds < 1000 && moved == normal)
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
                moved = await OnGame(game, () => WindowRect(f.Scene.Handle));
            }
            Assert.NotEqual(normal, moved);
            Assert.Equal(new PixelRect(0, 1080 - 2 - 96, 1920, 1080 - 2), moved); // 2 px margin (RF-05.7)
            Assert.True(sw.ElapsedMilliseconds < 1000);
            Assert.Empty(errors);
        }
        finally { await game.StopAsync(); }
    }

    [Fact]
    public async Task HiddenSceneOnlyMarksDirtyAndShowRecomputes()
    {
        var (game, errors) = await Start();
        try
        {
            var f = await OnGame(game, () => Setup(game, Mon("A", 0, 96, true)));
            await OnGame(game, () => { f.Dock.Relayout(); return 0; });
            var n0 = await OnGame(game, () => f.Dock.RelayoutCount);
            await OnGame(game, () =>
            {
                f.SetMonitors(() => [Mon("A", 0, 144, true)]);
                Notify(f.Scene.Handle, PInvoke.WM_DPICHANGED);
                return 0;
            });
            await Task.Delay(400, TestContext.Current.CancellationToken);
            Assert.Equal(n0, await OnGame(game, () => f.Dock.RelayoutCount)); // nothing while hidden
            var h = await OnGame(game, () => { f.Scene.Show(); return WindowRect(f.Scene.Handle).Height; });
            Assert.Equal(144, h);
            Assert.Equal(n0 + 1, await OnGame(game, () => f.Dock.RelayoutCount));
            Assert.Empty(errors);
        }
        finally { await game.StopAsync(); }
    }

    [Fact]
    public async Task UnrelatedSettingChangeIsIgnoredAndThreadIsPerMonitorV2()
    {
        var (game, errors) = await Start();
        try
        {
            var r = await OnGame(game, () =>
            {
                var f = Setup(game, Mon("A", 0, 96, true));
                f.Scene.Show();
                var n = f.Dock.RelayoutCount;
                Notify(f.Scene.Handle, PInvoke.WM_SETTINGCHANGE, 0x2A /* other SPI */);
                return (f.Dock.NextDeadline, n,
                    PInvoke.AreDpiAwarenessContextsEqual(PInvoke.GetThreadDpiAwarenessContext(), MonitorService.PerMonitorV2));
            });
            Assert.Null(r.NextDeadline);
            Assert.True(r.Item3);
            Assert.Empty(errors);
        }
        finally { await game.StopAsync(); }
    }

    [Fact]
    public void MonitorServiceEnumeratesRealMonitors() // RF-05.2
    {
        var list = MonitorService.Enumerate();
        Assert.NotEmpty(list);
        Assert.Contains(list, m => m.IsPrimary);
        Assert.All(list, m =>
        {
            Assert.False(string.IsNullOrEmpty(m.DeviceName));
            Assert.True(m.Dpi >= 96);
            Assert.True(m.Bounds.Width > 0 && m.WorkArea.Width > 0 && m.Handle != 0);
        });
    }
}
