using SideWar.Platform.Windows.Taskbar;
using SideWar.Runtime.Taskbar;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Xunit;

namespace SideWar.Platform.Windows.Tests;

public sealed class TaskbarTriggerTests
{
    private const uint Created = 0xC123;

    [Theory]
    [InlineData(0x007Eu, 0, null, TaskbarTriggerKind.Signal)]            // WM_DISPLAYCHANGE
    [InlineData(0x02E0u, 0, null, TaskbarTriggerKind.Signal)]            // WM_DPICHANGED
    [InlineData(0x001Au, 0x2F, null, TaskbarTriggerKind.Signal)]         // SPI_SETWORKAREA
    [InlineData(0x001Au, 0, "TraySettings", TaskbarTriggerKind.Signal)]
    [InlineData(0x001Au, 0, "Environment", TaskbarTriggerKind.None)]
    [InlineData(0x001Au, 0x30, null, TaskbarTriggerKind.None)]
    [InlineData(0x0200u, 0x2F, "TraySettings", TaskbarTriggerKind.None)] // other message, even with same payload
    [InlineData(Created, 0, null, TaskbarTriggerKind.ExplorerRestarted)]
    public void ClassifiesMessages(uint msg, int wParam, string? text, TaskbarTriggerKind expected) =>
        Assert.Equal(expected, TaskbarTriggers.Classify(msg, wParam, text, Created));

    [Fact]
    public void UnregisteredTaskbarCreatedNeverMatchesZero() =>
        Assert.Equal(TaskbarTriggerKind.None, TaskbarTriggers.Classify(0, 0, (string?)null, 0));

    [Fact]
    public void RegisteredMessageIsInSystemRange() => Assert.True(TaskbarTriggers.TaskbarCreatedMessage >= 0xC000);
}

[Trait("Env", "WindowsInteractive")]
public sealed class TaskbarProbeTests
{
    [Fact]
    public async Task QueryRunsOnShellMtaThreadAndFindsStartAndApps()
    {
        var bars = TaskbarLocator.FindBars();
        Assert.NotEmpty(bars);
        using var probe = new TaskbarProbe();
        var nodes = await probe.QueryAsync(bars[0].Hwnd).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(TaskbarProbe.ThreadName, probe.LastQueryThreadName);
        Assert.NotEmpty(nodes);
        Assert.Contains(nodes, n => n.AutomationId == "StartButton");
    }

    [Fact]
    public async Task DumpRoundTripsAndNeverCarriesNames()
    {
        var bar = TaskbarLocator.FindBars()[0];
        var target = TaskbarLocator.Describe(bar, bar.Monitor);
        using var probe = new TaskbarProbe();
        var nodes = await probe.DumpAsync(bar.Hwnd).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var dump = TaskbarDump.Create(Environment.OSVersion.Version.Build, bar.Monitor, target.Context.Edge,
            target.Context.AutoHide, target.Context.TaskbarRectPx, nodes);
        var back = TaskbarDump.FromJson(dump.ToJson());
        Assert.True(TaskbarLayoutComparison.Equivalent(dump.Layout.ToLayout(), back.Reclassify()));
        Assert.DoesNotContain("\"name\"", dump.ToJson(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HundredQueriesNeverMakeThisProcessForeground()
    {
        // Other processes (the user, parallel test runs) may legitimately change the foreground while this runs,
        // so the assertion is that nothing in this process ever becomes foreground because of the queries.
        var bar = TaskbarLocator.FindBars()[0];
        using var probe = new TaskbarProbe();
        var self = Environment.ProcessId;
        var changes = 0;
        var last = PInvoke.GetForegroundWindow();
        for (var i = 0; i < 100; i++)
        {
            await probe.QueryAsync(bar.Hwnd).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var now = PInvoke.GetForegroundWindow();
            if (now != last) changes++;
            last = now;
            _ = PInvoke.GetWindowThreadProcessId(now, out var pid);
            Assert.NotEqual((uint)self, pid);
        }
        Assert.Equal(100, probe.QueriesRun);
        Console.WriteLine($"foreground changes by third parties during 100 queries: {changes}");
    }

    [Fact]
    public async Task UiaCallOffTheShellThreadIsRejectedAndFailureIsReported()
    {
        using var probe = new TaskbarProbe();
        // Invalid handle: the COM failure must surface as a faulted task, not crash the thread.
        await Assert.ThrowsAnyAsync<Exception>(() => probe.QueryAsync(1));
        var bar = TaskbarLocator.FindBars()[0];
        Assert.NotEmpty(await probe.QueryAsync(bar.Hwnd).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void LocatorAndServiceWorkEndToEnd()
    {
        var bar = TaskbarLocator.FindBars()[0];
        var target = new TaskbarLocator().Locate(bar.Monitor);
        Assert.NotNull(target);
        Assert.Equal(bar.Hwnd, target!.Value.Hwnd);
        using var probe = new TaskbarProbe();
        using var done = new ManualResetEventSlim();
        var clock = new Clock();
        var service = new TaskbarService(new TaskbarLocator(), probe, clock, bar.Monitor, done.Set);
        service.SetVisible(true);
        Assert.True(done.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        service.OnWake();
        Assert.NotNull(service.Current);
        Assert.Equal(1, service.QueryCount);
    }

    private sealed class Clock : SideWar.Runtime.IClock
    {
        public long NowTicks => System.Diagnostics.Stopwatch.GetTimestamp();
        public double TicksPerSecond => System.Diagnostics.Stopwatch.Frequency;
    }

    [Fact]
    public async Task WatcherInstallsAndQueriesDoNotFloodIt()
    {
        var bar = TaskbarLocator.FindBars()[0];
        using var probe = new TaskbarProbe();
        var signals = 0;
        long accepted = -1, received = -1;
        var thread = new Thread(() =>
        {
            using var watcher = new TaskbarWatcher(() => signals++);
            Assert.True(watcher.Install(bar.Hwnd));
            Assert.True(watcher.IsInstalled);
            for (var i = 0; i < 20; i++)
            {
                probe.QueryAsync(bar.Hwnd).Wait(TimeSpan.FromSeconds(10));
                Pump(100);
            }
            accepted = watcher.EventsAccepted;
            received = watcher.EventsReceived;
            watcher.Uninstall();
            Assert.False(watcher.IsInstalled);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await Task.Run(thread.Join, TestContext.Current.CancellationToken);
        Assert.True(received >= accepted && accepted >= 0);
        Assert.Equal(accepted, signals);
        // Recorded in the evidence: UIA queries must not feed the hook (RNF-21.5).
        Console.WriteLine($"watcher received={received} accepted={accepted} signals={signals}");
    }

    private static void Pump(int ms)
    {
        var end = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < end)
        {
            while (PInvoke.PeekMessage(out var msg, HWND.Null, 0, 0,
                       PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
            {
                PInvoke.TranslateMessage(in msg);
                PInvoke.DispatchMessage(in msg);
            }
            Thread.Sleep(5);
        }
    }
}

/// <summary>Headless (fake hook API): no window, no real hook (SDD-21 RF-21.6 TaskbarCreated).</summary>
public sealed class TaskbarWatcherReinstallTests
{
    private sealed class FakeApi : TaskbarWatcher.IHookApi
    {
        public Dictionary<nint, uint> Pids { get; } = new();
        public List<(uint Pid, nint Hook)> Installed { get; } = new();
        public List<nint> Removed { get; } = new();
        private nint next = 100;
        public bool TryGetProcessId(nint window, out uint pid) => Pids.TryGetValue(window, out pid);
        public nint Install(uint pid, global::Windows.Win32.UI.Accessibility.WINEVENTPROC callback)
        {
            Installed.Add((pid, next));
            return next++;
        }
        public void Remove(nint hook) => Removed.Add(hook);
    }

    [Fact]
    public void TaskbarCreatedReinstallsHookWithNewPidAndSignalsOnce()
    {
        var api = new FakeApi();
        api.Pids[1] = 10;
        api.Pids[2] = 20; // new Explorer, new taskbar window
        var signals = 0;
        using var w = new TaskbarWatcher(() => signals++, api);
        Assert.True(w.Install(1));
        Assert.True(w.OnTaskbarCreated(() => 2));
        Assert.Equal([(10u, (nint)100), (20u, (nint)101)], api.Installed);
        Assert.Equal([(nint)100], api.Removed);
        Assert.Equal(1, w.ReinstallCount);
        Assert.Equal(1, signals);
        Assert.True(w.IsInstalled);
    }

    [Fact]
    public void TaskbarCreatedWhileHiddenDoesNothing()
    {
        var api = new FakeApi();
        api.Pids[1] = 10;
        var located = 0;
        using var w = new TaskbarWatcher(() => { }, api);
        Assert.False(w.OnTaskbarCreated(() => { located++; return 1; })); // never shown
        w.Install(1);
        w.Uninstall(); // Hide
        Assert.False(w.OnTaskbarCreated(() => { located++; return 1; }));
        Assert.Equal(0, located);
        Assert.Single(api.Installed);
    }

    [Fact]
    public void TaskbarNotYetAvailableKeepsWantingAndRetriesOnNextMessage()
    {
        var api = new FakeApi();
        api.Pids[1] = 10;
        api.Pids[3] = 30;
        using var w = new TaskbarWatcher(() => { }, api);
        w.Install(1);
        Assert.False(w.OnTaskbarCreated(() => 0)); // bar not found yet: old hook is still the stale one
        Assert.True(w.OnTaskbarCreated(() => 3));
        Assert.Equal(30u, api.Installed[^1].Pid);
    }
}
