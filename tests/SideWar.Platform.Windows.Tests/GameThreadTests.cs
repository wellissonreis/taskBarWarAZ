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
public sealed class GameThreadTests
{
    private sealed record TestCommand : IGameCommand;

    private sealed class FakeHost : IGameLoopHost, IDisposable
    {
        private long wakeCount;
        private int shutdowns;
        public long Deadline = long.MinValue; // MinValue = none.
        public void Dispose() => CommandSeen.Dispose();
        public Action<IClock>? OnWakeAction;
        public Thread? WakeThread;
        public Thread? CommandThread;
        public readonly ManualResetEventSlim CommandSeen = new();
        public int WakeCount => (int)Interlocked.Read(ref wakeCount);
        public int Shutdowns => Volatile.Read(ref shutdowns);
        public long? NextDeadline { get { long d = Volatile.Read(ref Deadline); return d == long.MinValue ? null : d; } }
        public void OnWake(IClock clock)
        {
            WakeThread = Thread.CurrentThread;
            Interlocked.Increment(ref wakeCount);
            OnWakeAction?.Invoke(clock);
        }
        public void OnCommand(IGameCommand command) { CommandThread = Thread.CurrentThread; CommandSeen.Set(); }
        public void Shutdown() => Interlocked.Increment(ref shutdowns);
    }

    private static (GameThread Game, FakeHost Host, List<Exception> Errors) Create()
    {
        var host = new FakeHost();
        var errors = new List<Exception>();
        var game = new GameThread(host, e => { lock (errors) errors.Add(e); }, _ => { });
        return (game, host, errors);
    }

    private static Task<T> OnGame<T>(GameThread game, Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        game.Post(() => { try { tcs.SetResult(func()); } catch (Exception e) { tcs.SetException(e); } });
        return tcs.Task;
    }

    private static long Ms(int ms) => System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency * ms / 1000;

    [Fact]
    public async Task ThreadIsNamedStaForegroundAndPostRunsOnIt()
    {
        var (game, host, _) = Create();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await game.StartAsync();
        Assert.True(watch.Elapsed <= TimeSpan.FromSeconds(5));
        try
        {
            var thread = await OnGame(game, () => Thread.CurrentThread);
            Assert.Equal("SideWar.Game", thread.Name);
            Assert.Equal(ApartmentState.STA, thread.GetApartmentState());
            Assert.False(thread.IsBackground);
            game.Post(new TestCommand());
            Assert.True(host.CommandSeen.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
            Assert.Same(thread, host.CommandThread);
        }
        finally { await game.StopAsync(); }
    }

    [Fact]
    public async Task DeadlineWakesThroughTimer()
    {
        var (game, host, _) = Create();
        await game.StartAsync();
        try
        {
            using var woke = new ManualResetEventSlim();
            host.OnWakeAction = _ =>
            {
                if (Volatile.Read(ref host.Deadline) == long.MinValue) return;
                if (System.Diagnostics.Stopwatch.GetTimestamp() >= Volatile.Read(ref host.Deadline))
                {
                    Volatile.Write(ref host.Deadline, long.MinValue);
                    woke.Set();
                }
            };
            Volatile.Write(ref host.Deadline, Ms(50));
            game.Post(() => { }); // Force an iteration so the deadline is armed; only the timer can wake afterwards.
            Assert.True(woke.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken), "Timer did not wake T-Game.");
        }
        finally { await game.StopAsync(); host.Dispose(); }
    }

    [Fact]
    public async Task IdleLoopWakesAtMostOncePerSecond()
    {
        var (game, host, _) = Create();
        await game.StartAsync();
        try
        {
            await Task.Delay(300, TestContext.Current.CancellationToken); // Let startup iterations settle.
            long before = game.WakeCount;
            await Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            long delta = game.WakeCount - before;
            Assert.True(delta <= 5, $"Idle wakes: {delta} in 5 s");
        }
        finally { await game.StopAsync(); }
    }

    [Fact]
    public async Task ActionAndHostFailuresAreReportedAndLoopContinues()
    {
        var (game, host, errors) = Create();
        await game.StartAsync();
        try
        {
            game.Post(() => throw new InvalidOperationException("action"));
            int failures = 0;
            host.OnWakeAction = _ => { if (Interlocked.Increment(ref failures) == 1) throw new InvalidOperationException("wake"); };
            game.Post(() => { });
            Assert.Equal(42, await OnGame(game, () => 42).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
            lock (errors)
            {
                Assert.Contains(errors, e => e.Message == "action");
                Assert.Contains(errors, e => e.Message == "wake");
            }
            Assert.False(game.Completion.IsCompleted);
        }
        finally { await game.StopAsync(); }
    }

    [Fact]
    public async Task StopDestroysWindowShutsDownOnceAndRejectsPost()
    {
        var (game, host, _) = Create();
        await game.StartAsync();
        nint hwnd = await OnGame(game, () => game.Scene.Handle);
        Assert.True(PInvoke.IsWindow(new HWND(hwnd)));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await game.StopAsync();
        Assert.True(watch.Elapsed <= TimeSpan.FromSeconds(3));
        Assert.False(PInvoke.IsWindow(new HWND(hwnd)));
        Assert.Equal(1, host.Shutdowns);
        Assert.Throws<InvalidOperationException>(() => game.Post(() => { }));
        await game.StopAsync(); // Idempotent.
        Assert.Equal(1, host.Shutdowns);
    }

    [Fact]
    public async Task StopBeforeStartupFinishesStillCompletes()
    {
        var (game, host, _) = Create();
        using var gate = new ManualResetEventSlim();
        game.StartupHook = () => gate.Wait(TimeSpan.FromSeconds(2), CancellationToken.None);
        game.Start();
        var stop = game.StopAsync();
        gate.Set();
        await stop;
        Assert.Equal(0, host.Shutdowns); // Startup abandoned: host never ran.
        Assert.True(game.Ready.IsCanceled);
        Assert.True(game.Completion.IsCompletedSuccessfully);
        // Handle requires T-Game (already gone); read the private field to prove the HWND is dead.
        var field = typeof(SideWar.Platform.Windows.Windows.SceneWindow)
            .GetField("handle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var hwnd = (HWND)field.GetValue(game.Scene)!;
        Assert.False(PInvoke.IsWindow(hwnd));
    }

    [Fact]
    public async Task StartupFailureRethrowsAndLeavesNothingAlive()
    {
        var (game, host, _) = Create();
        game.StartupHook = () => throw new InvalidOperationException("boom");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => game.StartAsync());
        Assert.Equal("boom", error.Message);
        Assert.True(game.Completion.IsFaulted);
        Assert.Equal(0, host.Shutdowns); // Host never ran.
        await game.StopAsync(); // Does not rethrow the already reported failure.
        Assert.Throws<InvalidOperationException>(() => game.Post(() => { }));
    }

    [Fact]
    public async Task ReadyTimeoutThrowsAndThreadEventuallyExits()
    {
        var (game, host, _) = Create();
        game.StartupHook = () => Thread.Sleep(700);
        await Assert.ThrowsAsync<TimeoutException>(() => game.StartAsync(TimeSpan.FromMilliseconds(100)));
        await game.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(0, host.Shutdowns);
        Assert.True(game.Ready.IsCanceled);
        Assert.True(game.Completion.IsCompletedSuccessfully);
        Assert.Throws<InvalidOperationException>(() => game.Post(() => { }));
    }

    [Fact]
    public async Task SceneWindowIsPassiveAndDoesNotStealForeground()
    {
        var (game, _, _) = Create();
        await game.StartAsync();
        try
        {
            var info = await OnGame(game, () =>
            {
                var hwnd = new HWND(game.Scene.Handle);
                var ex = (uint)WindowLong.Get(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
                var style = (uint)WindowLong.Get(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
                var fg0 = PInvoke.GetForegroundWindow();
                game.Scene.SetBounds(new PixelRect(0, 0, 100, 50));
                long c0 = game.ZOrder.ReassertionCount;
                game.Scene.Show();
                var fg1 = PInvoke.GetForegroundWindow();
                long c1 = game.ZOrder.ReassertionCount;
                var exShown = (uint)WindowLong.Get(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
                game.Scene.Hide();
                var fg2 = PInvoke.GetForegroundWindow();
                game.ZOrder.Request();
                game.ZOrder.OnWake(long.MaxValue);
                long c2 = game.ZOrder.ReassertionCount;
                return (ex, style, fg0, fg1, fg2, c0, c1, c2, exShown, game.Scene.Handle);
            });
            uint required = (uint)(WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW |
                WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOPMOST);
            Assert.Equal(required, info.ex & required);
            Assert.Equal(required, info.exShown & required);
            Assert.NotEqual(0u, info.style & (uint)WINDOW_STYLE.WS_POPUP);
            Assert.Equal(0u, info.style & (uint)WINDOW_STYLE.WS_VISIBLE);
            Assert.Equal(info.fg0, info.fg1);
            Assert.Equal(info.fg0, info.fg2);
            Assert.NotEqual(info.fg0, new HWND(info.Item10));
            Assert.Equal(info.c0 + 1, info.c1);
            Assert.Equal(info.c1, info.c2); // Hidden: no reassertion.
        }
        finally { await game.StopAsync(); }
    }
}
