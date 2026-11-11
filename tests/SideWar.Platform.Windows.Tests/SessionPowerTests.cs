using System.Diagnostics;
using SideWar.Core;
using SideWar.Platform.Windows.Instance;
using SideWar.Platform.Windows.Session;
using SideWar.Platform.Windows.Threading;
using SideWar.Runtime;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Power;
using Windows.Win32;
using Windows.Win32.Foundation;
using Xunit;

namespace SideWar.Platform.Windows.Tests;

public sealed class SessionPowerCoordinatorTests
{
    private static SessionPowerCoordinator Visible(out AppStateMachine machine)
    {
        machine = new AppStateMachine();
        machine.LeaveHidden(HiddenReason.Starting);
        return new SessionPowerCoordinator(machine);
    }

    [Theory]
    [InlineData(false, 0u, false, true, false, PowerProfile.Normal)]
    [InlineData(true, 0u, false, true, false, PowerProfile.Saver)]
    [InlineData(false, 1u, false, true, false, PowerProfile.Saver)]
    [InlineData(false, 2u, false, true, false, PowerProfile.Saver)]
    [InlineData(false, 0u, true, true, false, PowerProfile.Saver)]
    [InlineData(false, 0u, true, false, false, PowerProfile.Normal)]
    [InlineData(false, 0u, false, true, true, PowerProfile.Saver)]
    public void ResolverFollowsRf075(bool saving, uint energy, bool battery, bool onBattery, bool force, PowerProfile expected)
        => Assert.Equal(expected, PowerProfileResolver.Resolve(saving, energy, battery, onBattery, force));

    [Fact]
    public void LockHidesWithSaveAndUnlockShowsWithRestore()
    {
        var c = Visible(out var m);
        var hide = c.OnSessionChange(SessionPowerCoordinator.WtsSessionLock).Effects;
        Assert.Equal([AppEffectKind.Pause, AppEffectKind.Hide, AppEffectKind.Save, AppEffectKind.SetEcoQos], hide.Select(e => e.Kind));
        Assert.Contains(hide, e => e.Kind == AppEffectKind.Save && e.SaveTrigger == SaveTrigger.Hidden);
        Assert.Equal(HiddenReason.SessionLocked, m.HiddenReasons);
        var show = c.OnSessionChange(SessionPowerCoordinator.WtsSessionUnlock).Effects;
        Assert.Contains(show, e => e.Kind == AppEffectKind.Show && e.HiddenReason == HiddenReason.SessionLocked);
        Assert.True(m.IsVisible);
    }

    [Fact]
    public void RemoteDisconnectAndConnectToggleTheirReason()
    {
        var c = Visible(out var m);
        c.OnSessionChange(SessionPowerCoordinator.WtsRemoteDisconnect);
        Assert.Equal(HiddenReason.RemoteDisconnected, m.HiddenReasons);
        c.OnSessionChange(SessionPowerCoordinator.WtsConsoleConnect);
        Assert.True(m.IsVisible);
        c.OnSessionChange(SessionPowerCoordinator.WtsConsoleDisconnect);
        Assert.Equal(HiddenReason.RemoteDisconnected, m.HiddenReasons);
        c.OnSessionChange(SessionPowerCoordinator.WtsRemoteConnect);
        Assert.True(m.IsVisible);
        Assert.Empty(c.OnSessionChange(99).Effects);
    }

    [Fact]
    public void SuspendSavesAsSuspendedAndResumeSchedulesReenumeration()
    {
        var c = Visible(out var m);
        var hide = c.OnSuspend();
        Assert.False(hide.ScheduleReenumeration);
        Assert.Contains(hide.Effects, e => e is { Kind: AppEffectKind.Save, SaveTrigger: SaveTrigger.Suspended });
        var resume = c.OnResumeAutomatic();
        Assert.True(resume.ScheduleReenumeration);
        Assert.Contains(resume.Effects, e => e.Kind == AppEffectKind.Show);
        Assert.True(m.IsVisible);
    }

    [Fact]
    public void SuspendWhileLockedStillSavesAndShowsOnlyAfterUnlock()
    {
        var c = Visible(out var m);
        c.OnSessionChange(SessionPowerCoordinator.WtsSessionLock);
        Assert.Contains(c.OnSuspend().Effects, e => e is { Kind: AppEffectKind.Save, SaveTrigger: SaveTrigger.Suspended });
        Assert.DoesNotContain(c.OnResumeAutomatic().Effects, e => e.Kind == AppEffectKind.Show);
        Assert.False(m.IsVisible);
    }

    [Fact]
    public void DisplayStateZeroHidesOneAndTwoShow()
    {
        var c = Visible(out var m);
        c.OnPowerSetting(SessionPowerCoordinator.ConsoleDisplayState, 0);
        Assert.Equal(HiddenReason.DisplayOff, m.HiddenReasons);
        c.OnPowerSetting(SessionPowerCoordinator.ConsoleDisplayState, 2);
        Assert.True(m.IsVisible);
        c.OnPowerSetting(SessionPowerCoordinator.ConsoleDisplayState, 0);
        c.OnPowerSetting(SessionPowerCoordinator.ConsoleDisplayState, 1);
        Assert.True(m.IsVisible);
    }

    [Fact]
    public void PowerSettingsDriveProfileAndEcoQos()
    {
        var c = Visible(out var m);
        Assert.Equal(PowerProfile.Normal, m.PowerProfile);
        c.OnPowerSetting(SessionPowerCoordinator.AcDcPowerSource, 1);
        Assert.Equal(PowerProfile.Saver, m.PowerProfile);
        Assert.True(m.EcoQosOn);
        c.SetSaverOnBattery(false);
        Assert.Equal(PowerProfile.Normal, m.PowerProfile);
        Assert.False(m.EcoQosOn);
        c.OnPowerSetting(SessionPowerCoordinator.PowerSavingStatus, 1);
        Assert.Equal(PowerProfile.Saver, m.PowerProfile);
        c.OnPowerSetting(SessionPowerCoordinator.PowerSavingStatus, 0);
        c.OnPowerSetting(SessionPowerCoordinator.EnergySaverStatus, 1);
        Assert.Equal(PowerProfile.Saver, m.PowerProfile);
        c.OnPowerSetting(SessionPowerCoordinator.EnergySaverStatus, 0);
        Assert.Equal(PowerProfile.Normal, m.PowerProfile);
        c.SetForceSaver(true);
        Assert.Equal(PowerProfile.Saver, m.PowerProfile);
        Assert.Empty(c.OnPowerSetting(Guid.NewGuid(), 1).Effects);
    }
}

[Trait("Env", "WindowsInteractive")]
public sealed class SingleInstanceTests
{
    private static (string Mutex, string Event) Names()
    {
        string id = Guid.NewGuid().ToString("N");
        return ($@"Local\SideWar.Test.{id}.Mutex", $@"Local\SideWar.Test.{id}.Activate");
    }

    [Fact]
    public void SecondInstanceSignalsFirstAndReturnsNullWithin200Ms()
    {
        var (mutex, evt) = Names();
        using var first = SingleInstance.TryAcquire(mutex, evt);
        Assert.NotNull(first);
        using var activated = new ManualResetEventSlim();
        int count = 0;
        first.StartListening(() => { Interlocked.Increment(ref count); activated.Set(); });

        var watch = Stopwatch.StartNew();
        using var second = SingleInstance.TryAcquire(mutex, evt);
        watch.Stop();
        Assert.Null(second);
        Assert.True(watch.ElapsedMilliseconds <= 200, $"{watch.ElapsedMilliseconds} ms");
        Assert.True(activated.Wait(2000, TestContext.Current.CancellationToken));
        Assert.Equal(1, Volatile.Read(ref count));
    }

    [Fact]
    public void EachSecondLaunchActivatesAndReleaseAllowsNewOwner()
    {
        var (mutex, evt) = Names();
        var first = SingleInstance.TryAcquire(mutex, evt);
        Assert.NotNull(first);
        using var sem = new SemaphoreSlim(0);
        first.StartListening(() => sem.Release());
        Assert.Null(SingleInstance.TryAcquire(mutex, evt));
        Assert.True(sem.Wait(2000, TestContext.Current.CancellationToken));
        Assert.Null(SingleInstance.TryAcquire(mutex, evt));
        Assert.True(sem.Wait(2000, TestContext.Current.CancellationToken));
        first.Dispose();
        using var next = SingleInstance.TryAcquire(mutex, evt);
        Assert.NotNull(next);
    }
}

[Trait("Env", "WindowsInteractive")]
[Collection("SceneWindowClass")]
public sealed class SessionPowerWatcherTests
{
    private sealed class Host : IGameLoopHost
    {
        public long? NextDeadline => null;
        public void OnWake(IClock clock) { }
        public void OnCommand(IGameCommand command) { }
        public void Shutdown() { }
    }

    private static Task<T> OnGame<T>(GameThread game, Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        game.Post(() => { try { tcs.SetResult(func()); } catch (Exception e) { tcs.SetException(e); } });
        return tcs.Task;
    }

    [Fact]
    public async Task RegistersOnSceneTranslatesMessagesAndUnregistersOnDispose()
    {
        var game = new GameThread(new Host(), e => throw e, _ => { });
        await game.StartAsync();
        await using var _ = game;
        var results = new List<SessionPowerResult>();
        var machine = new AppStateMachine();
        machine.LeaveHidden(HiddenReason.Starting);
        var coordinator = new SessionPowerCoordinator(machine);
        var watcher = await OnGame(game, () =>
        {
            var w = new SessionPowerWatcher(game, game.Scene, coordinator, results.Add, _ => { });
            w.Start();
            return w;
        });
        Assert.True(watcher.SessionRegistered);
        Assert.Equal(4, watcher.RegisteredPowerSettings);

        await OnGame(game, () =>
        {
            var hwnd = new HWND(game.Scene.Handle);
            PInvoke.SendMessage(hwnd, 0x2B1, (WPARAM)(nuint)SessionPowerCoordinator.WtsSessionLock, default);
            PInvoke.SendMessage(hwnd, 0x2B1, (WPARAM)(nuint)SessionPowerCoordinator.WtsSessionUnlock, default);
            PInvoke.SendMessage(hwnd, 0x218, (WPARAM)(nuint)0x4, default);
            PInvoke.SendMessage(hwnd, 0x218, (WPARAM)(nuint)0x12, default);
            return 0;
        });
        Assert.Equal(4, results.Count);
        Assert.Contains(results[0].Effects, e => e.Kind == AppEffectKind.Hide);
        Assert.Contains(results[1].Effects, e => e.Kind == AppEffectKind.Show);
        Assert.Contains(results[2].Effects, e => e is { Kind: AppEffectKind.Save, SaveTrigger: SaveTrigger.Suspended });
        Assert.True(results[3].ScheduleReenumeration);
        Assert.True(machine.IsVisible);

        await OnGame(game, () => { watcher.Dispose(); return 0; });
        Assert.False(watcher.SessionRegistered);
        Assert.Equal(0, watcher.RegisteredPowerSettings);
        await OnGame(game, () => PInvoke.SendMessage(new HWND(game.Scene.Handle), 0x2B1, (WPARAM)(nuint)SessionPowerCoordinator.WtsSessionLock, default).Value);
        Assert.Equal(4, results.Count); // handler removed
    }
}
