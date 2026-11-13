using System.Diagnostics;
using SideWar.Core;
using SideWar.Platform.Windows.Rendering;
using SideWar.Platform.Windows.Threading;
using SideWar.Runtime;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Xunit;

namespace SideWar.Platform.Windows.Tests;

[Trait("Env", "WindowsInteractive")]
public sealed class SceneModeTests(Xunit.ITestOutputHelper output)
{
    private sealed class IdleHost : IGameLoopHost
    {
        public long? NextDeadline => null;
        public void OnWake(IClock clock) { }
        public void OnCommand(IGameCommand command) { }
        public void Shutdown() { }
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<GameThread> Start()
    {
        var game = new GameThread(new IdleHost(), e => throw e, _ => { });
        await game.StartAsync();
        return game;
    }

    private static Task<T> OnGame<T>(GameThread game, Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        game.Post(() => { try { tcs.SetResult(func()); } catch (Exception e) { tcs.SetException(e); } });
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
    }

    // Opaque strip (alpha 255 everywhere) so WindowFromPoint can only skip it through WS_EX_TRANSPARENT.
    private static LayeredSurface ShowOpaqueStrip(GameThread game, PixelRect bounds)
    {
        game.Scene.SetBounds(bounds);
        var surface = new LayeredSurface(game.Scene.Handle);
        surface.Resize(new PixelSize(bounds.Width, bounds.Height));
        var frame = surface.BeginFrame();
        for (var i = 0; i < frame.Width * frame.Height; i++)
            System.Runtime.InteropServices.Marshal.WriteInt32(frame.Pixels, i * 4, unchecked((int)0xFF204080));
        surface.EndFrame(null);
        game.Dock.SceneSurface = surface;
        game.Scene.Show();
        return surface;
    }

    private static uint ExStyle(GameThread game) =>
        (uint)PInvoke.GetWindowLongPtr(new HWND(game.Scene.Handle), WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

    private static bool IsTransparent(GameThread game) => (ExStyle(game) & (uint)WINDOW_EX_STYLE.WS_EX_TRANSPARENT) != 0;

    private static bool SceneIsHit(GameThread game, int x, int y)
    {
        var hit = PInvoke.WindowFromPoint(new System.Drawing.Point(x, y));
        return hit == new HWND(game.Scene.Handle);
    }

    [Fact]
    public async Task ToggleFiftyTimesKeepsStyleAndHitTestInStep() // T-04.1, CA-04.1, RNF-04.1
    {
        var game = await Start();
        try
        {
            var (mismatches, times) = await OnGame(game, () =>
            {
                using var surface = ShowOpaqueStrip(game, new PixelRect(200, 200, 600, 296));
                var bad = 0;
                var samples = new List<double>();
                Assert.True(IsTransparent(game)); // starts Passive
                game.Mode.ToggleInteractive(); // warm-up pair (JIT, first style change)
                game.Mode.ToggleInteractive();
                for (var i = 0; i < 50; i++)
                {
                    var before = Stopwatch.GetTimestamp();
                    game.Mode.ToggleInteractive();
                    samples.Add(Stopwatch.GetElapsedTime(before).TotalMilliseconds);
                    var interactive = game.Mode.Mode == OverlayMode.Interactive;
                    if (IsTransparent(game) == interactive) bad++;
                    if (SceneIsHit(game, 400, 250) != interactive) bad++;
                    if ((ExStyle(game) & (uint)WINDOW_EX_STYLE.WS_EX_NOACTIVATE) == 0) bad++;
                }
                Assert.Equal(OverlayMode.Passive, game.Mode.Mode);
                return (bad, samples);
            });
            Assert.Equal(0, mismatches);
            times.Sort();
            output.WriteLine($"toggle ms: min {times[0]:F2} median {times[times.Count / 2]:F2} p95 {times[(int)(times.Count * 0.95)]:F2} max {times[^1]:F2}");
            Assert.True(times[times.Count / 2] < 16, $"median toggle {times[times.Count / 2]:F1} ms"); // RNF-04.1 meta 16 ms
        }
        finally { await game.StopAsync(); }
    }

    [Fact]
    public async Task WidgetNeverGetsTransparentInEitherMode() // ADR-028, CA-23.1
    {
        var game = await Start();
        try
        {
            await OnGame(game, () =>
            {
                using var surface = ShowOpaqueStrip(game, new PixelRect(200, 200, 600, 296));
                var widget = game.Scene.GetOrCreateWidget();
                foreach (var mode in new[] { OverlayMode.Interactive, OverlayMode.Passive, OverlayMode.Interactive })
                {
                    game.Mode.SetMode(mode);
                    var ex = (uint)PInvoke.GetWindowLongPtr(new HWND(widget.Handle), WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
                    Assert.Equal(0u, ex & (uint)WINDOW_EX_STYLE.WS_EX_TRANSPARENT);
                    Assert.True(game.Mode.Mode == mode);
                }
                return 0;
            });
        }
        finally { await game.StopAsync(); }
    }

    [Fact]
    public async Task OpacityIsNeverZeroAndFollowsPresence() // CA-23.1, SDD-22 RF-22.7
    {
        var game = await Start();
        try
        {
            await OnGame(game, () =>
            {
                using var surface = ShowOpaqueStrip(game, new PixelRect(200, 200, 600, 296));
                game.Mode.Reapply();
                Assert.Equal(255, surface.Opacity);
                game.Mode.SetAbsent(true);
                Assert.Equal(128, surface.Opacity);
                game.Mode.ToggleInteractive();
                Assert.Equal(128, surface.Opacity);
                game.Mode.SetAbsent(false);
                Assert.Equal(255, surface.Opacity);
                return 0;
            });
        }
        finally { await game.StopAsync(); }
    }

    [Fact]
    public async Task HideReturnsToPassiveAndRaisesModeChanged() // RF-04.2
    {
        var game = await Start();
        try
        {
            var changes = new List<OverlayMode>();
            await OnGame(game, () =>
            {
                using var surface = ShowOpaqueStrip(game, new PixelRect(200, 200, 600, 296));
                game.Mode.ModeChanged += changes.Add;
                game.Mode.SetMode(OverlayMode.Interactive);
                game.Scene.Hide();
                Assert.Equal(OverlayMode.Passive, game.Mode.Mode);
                Assert.True(IsTransparent(game));
                game.Scene.Show();
                Assert.Equal(OverlayMode.Passive, game.Mode.Mode);
                return 0;
            });
            Assert.Equal([OverlayMode.Interactive, OverlayMode.Passive], changes);
        }
        finally { await game.StopAsync(); }
    }

    [Fact]
    public async Task InteractiveReturnsToPassiveAboutEightSeconds() // CA-04.4 (real timer)
    {
        var game = await Start();
        try
        {
            var tcs = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
            long start = 0;
            PInvoke.SetCursorPos(10, 10); // a cursor over the strip legitimately holds Interactive (RF-04.3)
            await OnGame(game, () =>
            {
                ShowOpaqueStrip(game, new PixelRect(200, 200, 600, 296));
                game.Mode.ModeChanged += m =>
                {
                    if (m == OverlayMode.Passive) tcs.TrySetResult(Stopwatch.GetElapsedTime(start).TotalSeconds);
                };
                start = Stopwatch.GetTimestamp();
                game.Mode.ToggleInteractive();
                return 0;
            });
            var seconds = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(12), Token);
            Assert.InRange(seconds, 7.5, 8.5);
            Assert.True(await OnGame(game, () => IsTransparent(game)));
        }
        finally { await game.StopAsync(); }
    }

    [Fact]
    public async Task ClickOnRegionsRaisesTargetsOnlyInInteractive() // RF-22.3
    {
        var game = await Start();
        try
        {
            var hits = new List<HitTarget>();
            await OnGame(game, () =>
            {
                using var surface = ShowOpaqueStrip(game, new PixelRect(200, 200, 600, 296));
                game.Mode.ScaleOverride = 1f;
                var content = SideWar.Core.Content.ContentLoader.LoadEmbedded();
                var sim = SideWar.Core.Simulation.SimState.CreateInitial(content,
                    SideWar.Core.Simulation.StripLayout.Create(600, 560, 88, 4, 520, SideWar.Core.Simulation.TowerHost.Taskbar,
                        [new SideWar.Core.Simulation.FreeZone(0, 600)]), SideWar.Core.Simulation.Rng.Seed(1, 2));
                sim.Credits = 1_000_000;
                Assert.True(sim.Apply(new SideWar.Core.Simulation.SimContext(content), new SideWar.Core.Simulation.BuildTower(SideWar.Core.Simulation.TowerType.Bolt, 80)).IsOk);
                game.Mode.SetScene(sim, 48f);
                game.Mode.TargetClicked += hits.Add;
                int towerId = sim.Towers.At(0).Id;
                Assert.Equal(HitTarget.Tower(towerId), game.Mode.HitTest(80, 120));
                Assert.Equal(HitTarget.None, game.Mode.HitTest(300, 120));
                var hwnd = new HWND(game.Scene.Handle);
                LPARAM At(int x, int y) => new((y << 16) | (x & 0xFFFF));
                PInvoke.SendMessage(hwnd, PInvoke.WM_LBUTTONDOWN, default, At(80, 120)); // Passive: ignored
                game.Mode.SetMode(OverlayMode.Interactive);
                PInvoke.SendMessage(hwnd, PInvoke.WM_LBUTTONDOWN, default, At(80, 120));
                PInvoke.SendMessage(hwnd, PInvoke.WM_LBUTTONDOWN, default, At(300, 120)); // free space: no event
                PInvoke.SendMessage(hwnd, PInvoke.WM_LBUTTONDOWN, default, At(300, 5)); // outside: no event
                return 0;
            });
            Assert.Single(hits);
            Assert.Equal(HitKind.SceneTower, hits[0].Kind);
        }
        finally { await game.StopAsync(); }
    }

    [Fact]
    public async Task CursorOverStripHoldsInteractiveAndLeaveRestartsCountdown() // RF-04.3
    {
        var game = await Start();
        try
        {
            await OnGame(game, () =>
            {
                using var surface = ShowOpaqueStrip(game, new PixelRect(200, 200, 600, 296));
                game.Mode.SetMode(OverlayMode.Interactive);
                Assert.NotNull(game.Mode.NextDeadline);
                PInvoke.SendMessage(new HWND(game.Scene.Handle), PInvoke.WM_MOUSEMOVE, default, new LPARAM((20 << 16) | 20));
                Assert.Null(game.Mode.NextDeadline);
                PInvoke.SendMessage(new HWND(game.Scene.Handle), PInvoke.WM_MOUSELEAVE, default, default);
                Assert.NotNull(game.Mode.NextDeadline);
                return 0;
            });
        }
        finally { await game.StopAsync(); }
    }

    [Fact]
    public async Task PeekFadesWithinThreeHundredMsAndRestoresOnlyThroughOpacity() // CA-04.5
    {
        var game = await Start();
        try
        {
            // Strip at (200,200)-(600,296); put the cursor inside, never over the widget.
            await OnGame(game, () =>
            {
                ShowOpaqueStrip(game, new PixelRect(200, 200, 600, 296));
                game.Mode.SetPeekEnabled(true);
                PInvoke.SetCursorPos(400, 250);
                return 0;
            });
            var deadline = Stopwatch.StartNew();
            var faded = false;
            while (deadline.ElapsedMilliseconds < 1000 && !faded)
            {
                await Task.Delay(20, Token);
                PInvoke.SetCursorPos(400, 250); // keep it there against real-mouse jitter on a shared desktop
                faded = await OnGame(game, () => game.Mode.IsPeeking);
            }
            Assert.True(faded, "peek never started");
            Assert.True(deadline.ElapsedMilliseconds <= 600, $"peek after {deadline.ElapsedMilliseconds} ms"); // 250 ms + poll + scheduling slack
            Assert.Equal(PeekController.PeekAlpha, await OnGame(game, () => game.Dock.SceneSurface!.Opacity));
            await OnGame(game, () => { PInvoke.SetCursorPos(10, 10); return 0; });
            await Task.Delay(600, Token);
            Assert.False(await OnGame(game, () => game.Mode.IsPeeking));
            Assert.Equal(255, await OnGame(game, () => game.Dock.SceneSurface!.Opacity));
            await OnGame(game, () => { game.Mode.SetPeekEnabled(false); return 0; });
        }
        finally { await game.StopAsync(); }
    }

    [Fact]
    public async Task PeekIsOffByDefaultAndInInteractive() // DP-22.3, RF-04.5
    {
        var game = await Start();
        try
        {
            await OnGame(game, () =>
            {
                ShowOpaqueStrip(game, new PixelRect(200, 200, 600, 296));
                Assert.False(game.Mode.PeekEnabled);
                Assert.Null(game.Mode.NextDeadline);
                game.Mode.SetPeekEnabled(true);
                Assert.NotNull(game.Mode.NextDeadline);
                game.Mode.SetMode(OverlayMode.Interactive);
                Assert.True(game.Mode.NextDeadline > Stopwatch.GetTimestamp() + Stopwatch.Frequency * 7); // peek polling off, only the 8 s deadline
                return 0;
            });
        }
        finally { await game.StopAsync(); }
    }
}
