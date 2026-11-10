using SideWar.Core;
using SideWar.Platform.Windows.Input;
using SideWar.Platform.Windows.Threading;
using SideWar.Platform.Windows.Tray;
using SideWar.Runtime;
using Windows.Win32;
using Windows.Win32.Foundation;
using Xunit;

namespace SideWar.Platform.Windows.Tests;

[Trait("Env", "WindowsInteractive")]
[Collection("SceneWindowClass")] // One SceneWindow class per process: do not overlap GameThreadTests.
public sealed class HotkeyTrayTests
{
    private sealed class Host : IGameLoopHost
    {
        public readonly List<(IGameCommand Command, Thread Thread)> Commands = [];
        public long? NextDeadline => null;
        public void OnWake(IClock clock) { }
        public void OnCommand(IGameCommand command) { lock (Commands) Commands.Add((command, Thread.CurrentThread)); }
        public void Shutdown() { }
    }

    private static async Task<(GameThread Game, Host Host)> StartGame()
    {
        var host = new Host();
        var game = new GameThread(host, e => throw e, _ => { });
        await game.StartAsync();
        return (game, host);
    }

    private static Task<T> OnGame<T>(GameThread game, Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        game.Post(() => { try { tcs.SetResult(func()); } catch (Exception e) { tcs.SetException(e); } });
        return tcs.Task;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (!condition()) { Assert.True(DateTime.UtcNow < limit, "timeout"); await Task.Delay(10, TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task RegistersFiresCommandsOnGameThreadAndUnregistersOnDispose()
    {
        var (game, host) = await StartGame();
        await using var _ = game;
        var service = await OnGame(game, () => new HotkeyService(game, game.Scene, game, _ => { }));
        Assert.True(await OnGame(game, service.Register));
        Assert.Equal(HotkeyStatus.Registered, service.GetStatus(HotkeyAction.ToggleInteractive));
        Assert.Equal(HotkeyStatus.Registered, service.GetStatus(HotkeyAction.ToggleVisibility));

        // Same message the system sends for the registered chord (real keystrokes are not injected).
        await OnGame(game, () =>
        {
            PInvoke.SendMessage(new HWND(game.Scene.Handle), PInvoke.WM_HOTKEY, (WPARAM)(nuint)HotkeyService.IdFor(HotkeyAction.ToggleVisibility), default);
            PInvoke.SendMessage(new HWND(game.Scene.Handle), PInvoke.WM_HOTKEY, (WPARAM)(nuint)HotkeyService.IdFor(HotkeyAction.ToggleInteractive), default);
            PInvoke.SendMessage(new HWND(game.Scene.Handle), PInvoke.WM_HOTKEY, (WPARAM)(nuint)9999, default); // unknown id ignored
            return 0;
        });
        await WaitFor(() => { lock (host.Commands) return host.Commands.Count >= 2; });
        lock (host.Commands)
        {
            Assert.Equal(2, host.Commands.Count);
            Assert.Equal(new HotkeyCommand(HotkeyAction.ToggleVisibility), host.Commands[0].Command);
            Assert.Equal(new HotkeyCommand(HotkeyAction.ToggleInteractive), host.Commands[1].Command);
            Assert.All(host.Commands, c => Assert.Equal("SideWar.Game", c.Thread.Name));
        }

        await OnGame(game, () => { service.Dispose(); return 0; });
        Assert.Equal(HotkeyStatus.NotRegistered, service.GetStatus(HotkeyAction.ToggleVisibility));
        // Released: a fresh service registers again.
        var again = await OnGame(game, () => { var s = new HotkeyService(game, game.Scene, game, _ => { }); var ok = s.Register(); s.Dispose(); return ok; });
        Assert.True(again);
    }

    [Fact]
    public async Task TogglePauseAndCustomAndInvalidBindingsFromParameter()
    {
        var (game, host) = await StartGame();
        await using var _ = game;
        var bindings = new Dictionary<HotkeyAction, string>
        {
            [HotkeyAction.TogglePause] = "ctrl+shift+alt+F11",
            [HotkeyAction.ToggleVisibility] = "Ctrl+Nope",
        };
        var conflicts = new List<(HotkeyAction, string)>();
        var service = await OnGame(game, () =>
        {
            var s = new HotkeyService(game, game.Scene, game, _ => { }, bindings);
            s.Conflict += (a, m) => conflicts.Add((a, m));
            return s;
        });
        Assert.False(await OnGame(game, service.Register));
        Assert.Equal(HotkeyStatus.Registered, service.GetStatus(HotkeyAction.ToggleInteractive)); // default Ctrl+Alt+G
        Assert.Equal(HotkeyStatus.Registered, service.GetStatus(HotkeyAction.TogglePause));
        Assert.Equal(HotkeyStatus.Unavailable, service.GetStatus(HotkeyAction.ToggleVisibility));
        Assert.Equal("ctrl+shift+alt+F11", service.GetBinding(HotkeyAction.TogglePause));
        Assert.Equal("Ctrl+Alt+P", HotkeyService.Describe(HotkeyAction.TogglePause));
        Assert.Contains(conflicts, c => c.Item1 == HotkeyAction.ToggleVisibility && c.Item2.StartsWith("Atalho inválido", StringComparison.Ordinal));

        await OnGame(game, () =>
        {
            foreach (var a in new[] { HotkeyAction.TogglePause, HotkeyAction.ToggleVisibility })
                PInvoke.SendMessage(new HWND(game.Scene.Handle), PInvoke.WM_HOTKEY, (WPARAM)(nuint)HotkeyService.IdFor(a), default);
            return 0;
        });
        await WaitFor(() => { lock (host.Commands) return host.Commands.Count >= 1; });
        await Task.Delay(100, TestContext.Current.CancellationToken);
        lock (host.Commands)
        {
            Assert.Single(host.Commands); // invalid-binding action posts nothing
            Assert.Equal(new HotkeyCommand(HotkeyAction.TogglePause), host.Commands[0].Command);
        }
        await OnGame(game, () => { service.Dispose(); return 0; });
    }

    /// <summary>Another "app": a thread-bound RegisterHotKey of Ctrl+Alt+G/H, held until released.</summary>
    private static (Action Release, bool Ok) HoldChords()
    {
        var release = new ManualResetEventSlim();
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var mod = global::Windows.Win32.UI.Input.KeyboardAndMouse.HOT_KEY_MODIFIERS.MOD_CONTROL | global::Windows.Win32.UI.Input.KeyboardAndMouse.HOT_KEY_MODIFIERS.MOD_ALT;
            bool ok = PInvoke.RegisterHotKey(default, 1, mod, 0x47) && PInvoke.RegisterHotKey(default, 2, mod, 0x48);
            done.SetResult(ok);
            release.Wait();
            PInvoke.UnregisterHotKey(default, 1);
            PInvoke.UnregisterHotKey(default, 2);
        });
        thread.Start();
        bool registered = done.Task.GetAwaiter().GetResult();
        return (() => { release.Set(); thread.Join(); release.Dispose(); }, registered);
    }

    [Fact]
    public async Task ConflictMarksUnavailableAndNotifiesOncePerSession()
    {
        var (game, host) = await StartGame();
        await using var _ = game;
        var (release, ok) = HoldChords();
        try
        {
            Assert.True(ok);
            var logs = new List<string>();
            var conflicts = new List<(HotkeyAction, string)>();
            var service = await OnGame(game, () =>
            {
                var s = new HotkeyService(game, game.Scene, game, logs.Add);
                s.Conflict += (action, message) => conflicts.Add((action, message));
                return s;
            });
            Assert.False(await OnGame(game, service.Register));
            Assert.False(await OnGame(game, service.Register)); // retry: still unavailable, no second notification
            Assert.Equal(HotkeyStatus.Unavailable, service.GetStatus(HotkeyAction.ToggleInteractive));
            Assert.Equal(HotkeyStatus.Unavailable, service.GetStatus(HotkeyAction.ToggleVisibility));
            Assert.Equal(2, conflicts.Count);
            Assert.Equal("Atalho Ctrl+Alt+G em uso por outro app", conflicts.First(c => c.Item1 == HotkeyAction.ToggleInteractive).Item2);
            Assert.Contains(logs, l => l.Contains("Ctrl+Alt+H"));
            // Unavailable actions never post commands.
            await OnGame(game, () => { PInvoke.SendMessage(new HWND(game.Scene.Handle), PInvoke.WM_HOTKEY, (WPARAM)(nuint)HotkeyService.IdFor(HotkeyAction.ToggleVisibility), default); return 0; });
            await Task.Delay(100, TestContext.Current.CancellationToken);
            lock (host.Commands) Assert.Empty(host.Commands);

            release();
            release = () => { };
            Assert.True(await OnGame(game, service.Register)); // recovers once the other app releases the chord
            await OnGame(game, () => { service.Dispose(); return 0; });
        }
        finally { release(); }
    }

    private static T OnSta<T>(Func<T> body)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() => { try { result = body(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (error is not null) throw new InvalidOperationException("STA body failed", error);
        return result;
    }

    [Fact]
    public void TrayIconAddsRaisesClickEventsRecreatesAndRemoves()
    {
        var (added, left, right, recreated, disposedAdded) = OnSta(() =>
        {
            int l = 0, r = 0;
            var tray = new TrayIcon("SideWar");
            tray.ToggleVisibilityRequested += () => l++;
            tray.MenuRequested += () => r++;
            var hwnd = new HWND(tray.WindowHandle);
            const uint callback = PInvoke.WM_APP + 1;
            PInvoke.SendMessage(hwnd, callback, 1, (LPARAM)(nint)PInvoke.WM_LBUTTONUP);
            PInvoke.SendMessage(hwnd, callback, 1, (LPARAM)(nint)PInvoke.WM_RBUTTONUP);
            PInvoke.SendMessage(hwnd, callback, 1, (LPARAM)(nint)PInvoke.WM_MOUSEMOVE);
            uint tbc = PInvoke.RegisterWindowMessage("TaskbarCreated");
            PInvoke.SendMessage(hwnd, tbc, 0, 0);
            var wasAdded = tray.IsAdded;
            var rc = tray.RecreateCount;
            tray.Dispose();
            return (wasAdded, l, r, rc, tray.IsAdded);
        });
        Assert.True(added);
        Assert.Equal(1, left);
        Assert.Equal(1, right);
        Assert.Equal(1, recreated);
        Assert.False(disposedAdded);
    }

    [Fact]
    public void TrayTooltipIsLimitedToOneHertzAndOnlyWhenChanged()
    {
        var updates = OnSta(() =>
        {
            long now = 0;
            using var tray = new TrayIcon("SideWar", () => now);
            var results = new List<bool>();
            results.Add(tray.SetTooltip("SideWar")); // unchanged
            results.Add(tray.SetTooltip("A"));       // too soon (<1 s)
            now += System.Diagnostics.Stopwatch.Frequency;
            results.Add(tray.FlushTooltip());        // pending applied after 1 s
            results.Add(tray.SetTooltip("A"));       // unchanged
            results.Add(tray.SetTooltip(new string('x', 300))); // too soon again
            now += System.Diagnostics.Stopwatch.Frequency;
            results.Add(tray.SetTooltip(new string('x', 300))); // applied, truncated
            Assert.Equal(127, tray.Tooltip.Length);
            Assert.Equal(2, tray.TooltipUpdates);
            return results;
        });
        Assert.Equal([false, false, true, false, false, true], updates);
    }
}
