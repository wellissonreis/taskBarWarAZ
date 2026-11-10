using System.Text.Json;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Taskbar;
using Xunit;

namespace SideWar.Runtime.Tests.Taskbar;

public sealed class TaskbarServiceTests
{
    private static readonly MonitorInfo Monitor = new("DISPLAY", new(0, 0, 1440, 900), new(0, 0, 1440, 852), 96, true, 0);
    private static UiaNode N(int x, string id = "", string cls = "TaskListButton") => new(cls, "", id, new(x, 852, x + 44, 900));

    private sealed class Locator : ITaskbarLocator
    {
        public TaskbarTarget? Target { get; set; } = new(7, new(Monitor, new(0, 852, 1440, 900), TaskbarEdge.Bottom, false));
        public TaskbarTarget? Locate(MonitorInfo monitor) => Target;
    }

    private sealed class Probe : ITaskbarProbe
    {
        public List<TaskCompletionSource<UiaNode[]>> Calls { get; } = [];
        public List<int> Threads { get; } = [];
        public Task<UiaNode[]> QueryAsync(nint hwnd)
        {
            Threads.Add(Environment.CurrentManagedThreadId);
            var tcs = new TaskCompletionSource<UiaNode[]>();
            Calls.Add(tcs);
            return tcs.Task;
        }
        public void Succeed(int index, int apps = 2) =>
            Calls[index].SetResult([N(500, "StartButton"), .. Enumerable.Range(0, apps).Select(i => N(548 + 48 * i)), N(1250, "", "SystemTray.NormalButton")]);
    }

    private sealed class Rig
    {
        public FakeClock Clock { get; } = new();
        public Locator Locator { get; } = new();
        public Probe Probe { get; } = new();
        public TaskbarService Service { get; }
        public List<TaskbarLayout> Changes { get; } = [];
        public Rig(bool poll = false)
        {
            Service = new(Locator, Probe, Clock, Monitor) { SafetyPollEnabled = poll };
            Service.LayoutChanged += Changes.Add;
        }
        public void Advance(int ms)
        {
            // Deadline-driven like the host: wake at each deadline, never in between.
            var end = Clock.NowTicks + (long)(ms / 1000.0 * Clock.TicksPerSecond);
            while (Service.NextDeadline is { } d && d <= end)
            {
                Clock.AdvanceTicks(Math.Max(0, d - Clock.NowTicks));
                Service.OnWake();
            }
            Clock.AdvanceTicks(end - Clock.NowTicks);
            Service.OnWake();
        }
        public void Complete(int index, int apps = 2) { Probe.Succeed(index, apps); Service.OnWake(); }
    }

    [Fact]
    public void ShowQueriesImmediatelyAndPublishesUiaLayout()
    {
        var rig = new Rig();
        rig.Service.SetVisible(true);
        Assert.Single(rig.Probe.Calls);
        rig.Complete(0);
        Assert.Equal(TaskbarSource.Uia, rig.Service.Current!.Value.Source);
        Assert.Equal(2, rig.Service.Current!.Value.AppButtons.Count);
        Assert.Single(rig.Changes);
        Assert.Equal(1, rig.Service.QueryCount);
    }

    [Fact]
    public void SignalsDebounceFiveHundredMsAndRescheduleSameDeadline()
    {
        var rig = new Rig();
        rig.Service.SetVisible(true);
        rig.Complete(0);
        rig.Service.Signal(); rig.Advance(300); rig.Service.Signal(); rig.Advance(300);
        Assert.Single(rig.Probe.Calls); // 600 ms after first signal, but only 300 ms after the last
        rig.Advance(200);
        Assert.Equal(2, rig.Probe.Calls.Count);
    }

    [Fact]
    public void UnchangedLayoutIsNotNotified()
    {
        var rig = new Rig();
        rig.Service.SetVisible(true);
        rig.Complete(0);
        rig.Service.Signal(); rig.Advance(500);
        rig.Complete(1);
        Assert.Single(rig.Changes);
        rig.Service.Signal(); rig.Advance(500);
        rig.Complete(2, apps: 3); // pinned an app
        Assert.Equal(2, rig.Changes.Count);
        Assert.Equal(3, rig.Service.Current!.Value.AppButtons.Count);
        Assert.Equal(2, rig.Service.UpdateCount);
    }

    [Fact]
    public void HiddenOverlayNeverQueriesFiveMinutesAndShowQueriesOnce()
    {
        var rig = new Rig(poll: true);
        rig.Advance(300_000);
        rig.Service.Signal(); rig.Service.Signal();
        rig.Advance(300_000);
        Assert.Equal(0, rig.Service.QueryCount);
        Assert.True(rig.Service.LayoutDirty);
        Assert.Null(rig.Service.NextDeadline);
        rig.Service.SetVisible(true);
        Assert.Equal(1, rig.Service.QueryCount);
        Assert.False(rig.Service.LayoutDirty);
    }

    [Fact]
    public void VisibleStableFiveMinutesStaysWithinSix()
    {
        var rig = new Rig(poll: true);
        rig.Service.SetVisible(true);
        for (var i = 0; i < 400 && rig.Clock.Seconds < 300; i++)
        {
            if (rig.Probe.Calls.Count > 0 && !rig.Probe.Calls[^1].Task.IsCompleted) rig.Complete(rig.Probe.Calls.Count - 1);
            rig.Advance(1000);
        }
        Assert.InRange(rig.Service.QueryCount, 1, 6);
    }

    [Fact]
    public void NoPollWhenDisabled()
    {
        var rig = new Rig(poll: false);
        rig.Service.SetVisible(true);
        rig.Complete(0);
        rig.Advance(300_000);
        Assert.Equal(1, rig.Service.QueryCount);
    }

    [Fact]
    public void HideCancelsPendingDeadlinesAndShowAfterHideQueriesAgain()
    {
        var rig = new Rig(poll: true);
        rig.Service.SetVisible(true); rig.Complete(0);
        rig.Service.Signal();
        rig.Service.SetVisible(false);
        Assert.Null(rig.Service.NextDeadline);
        rig.Service.SetVisible(true);
        Assert.Equal(2, rig.Service.QueryCount);
    }

    [Fact]
    public void TimeoutCountsFailureBlocksNewQueriesAndIgnoresLateResult()
    {
        var rig = new Rig();
        rig.Service.SetVisible(true);
        rig.Advance(299);
        Assert.Equal(0, rig.Service.ConsecutiveFailures);
        rig.Advance(2);
        Assert.Equal(1, rig.Service.ConsecutiveFailures);
        Assert.Equal(TaskbarSource.Fallback, rig.Service.Current!.Value.Source); // never had Uia
        rig.Service.Signal(); rig.Advance(600);
        Assert.Single(rig.Probe.Calls); // previous query has not returned: nothing new emitted
        rig.Complete(0); // late arrival: ignored, no layout from it
        Assert.Equal(TaskbarSource.Fallback, rig.Service.Current!.Value.Source);
        Assert.Equal(2, rig.Probe.Calls.Count); // pending request goes out only after the return
    }

    [Fact]
    public void OneOrTwoFailuresKeepLastUiaLayoutAndRetryAfterFiveSeconds()
    {
        var rig = new Rig();
        rig.Service.SetVisible(true); rig.Complete(0);
        rig.Service.Signal(); rig.Advance(500);
        rig.Probe.Calls[1].SetException(new InvalidOperationException("RPC_E_DISCONNECTED")); rig.Service.OnWake();
        Assert.Equal(1, rig.Service.ConsecutiveFailures);
        Assert.Equal(TaskbarSource.Uia, rig.Service.Current!.Value.Source);
        rig.Advance(4900);
        Assert.Equal(2, rig.Probe.Calls.Count);
        rig.Advance(200); // retry at 5 s: exactly one new query
        Assert.Equal(3, rig.Probe.Calls.Count);
        rig.Probe.Calls[2].SetException(new InvalidOperationException()); rig.Service.OnWake();
        Assert.Equal(2, rig.Service.ConsecutiveFailures);
        Assert.Equal(TaskbarSource.Uia, rig.Service.Current!.Value.Source);
    }

    [Fact]
    public void ThirdConsecutiveFailureFallsBackAndSuccessResetsCounter()
    {
        var rig = new Rig();
        rig.Service.SetVisible(true); rig.Complete(0);
        for (var i = 1; i <= 3; i++)
        {
            rig.Service.Signal(); rig.Advance(500);
            rig.Probe.Calls[i].SetException(new InvalidOperationException()); rig.Service.OnWake();
        }
        Assert.Equal(TaskbarSource.Fallback, rig.Service.Current!.Value.Source);
        Assert.Empty(rig.Service.Current!.Value.AppButtons);
        Assert.Equal(8, SlotMap.From(rig.Service.Current!.Value, 1440).Count);
        rig.Service.Signal(); rig.Advance(500);
        rig.Complete(4);
        Assert.Equal(0, rig.Service.ConsecutiveFailures);
        Assert.Equal(TaskbarSource.Uia, rig.Service.Current!.Value.Source);
    }

    [Fact]
    public void UseUiaFalseNeverCallsProbe()
    {
        var rig = new Rig { };
        rig.Service.UseUia = false;
        rig.Service.SetVisible(true);
        rig.Service.Signal(); rig.Advance(1000);
        Assert.Empty(rig.Probe.Calls);
        Assert.Equal(TaskbarSource.Fallback, rig.Service.Current!.Value.Source);
    }

    [Fact]
    public void MonitorWithoutTaskbarOrAutoHideSkipsProbeAndFallsBack()
    {
        var rig = new Rig();
        rig.Locator.Target = null;
        rig.Service.SetVisible(true);
        Assert.Empty(rig.Probe.Calls);
        Assert.Equal(TaskbarEdge.Unknown, rig.Service.Current!.Value.Edge);
        rig.Locator.Target = new(7, new(Monitor, new(0, 852, 1440, 900), TaskbarEdge.Bottom, true));
        rig.Service.Signal(); rig.Advance(500);
        Assert.Empty(rig.Probe.Calls);
        Assert.Equal(TaskbarSource.Fallback, rig.Service.Current!.Value.Source);
    }

    [Fact]
    public void ProbeThatThrowsSynchronouslyCountsAsFailure()
    {
        var rig = new Rig();
        var svc = new TaskbarService(rig.Locator, new ThrowingProbe(), rig.Clock, Monitor);
        svc.SetVisible(true);
        svc.OnWake();
        Assert.Equal(1, svc.ConsecutiveFailures);
    }

    private sealed class ThrowingProbe : ITaskbarProbe
    {
        public Task<UiaNode[]> QueryAsync(nint hwnd) => throw new InvalidOperationException("boom");
    }

    [Fact]
    public void DumpRoundTripReproducesLayoutAndHasNoNameField()
    {
        DumpNode D(string cls, string parent, string id, string type, int x) =>
            new(cls, parent, id, type, new(x, 852, x + 44, 900));
        var nodes = new[]
        {
            D("Taskbar.TaskbarFrame", "", "", "Pane", 0),
            D("", "", "StartButton", "Button", 500),
            D("Taskbar.TaskListButtonAutomationPeer", "", "Appid1", "Button", 548),
            D("Taskbar.TaskListButtonAutomationPeer", "", "Appid2", "Button", 596),
            D("SystemTray.NormalButton", "", "", "Button", 1250),
        };
        var dump = TaskbarDump.Create(26100, Monitor, TaskbarEdge.Bottom, false, new(0, 852, 1440, 900), nodes);
        var json = dump.ToJson();
        using (var doc = JsonDocument.Parse(json)) Assert.False(HasName(doc.RootElement));
        Assert.Equal("app-0", dump.Nodes[2].Label);
        Assert.Equal("app-1", dump.Nodes[3].Label);
        var back = TaskbarDump.FromJson(json);
        Assert.Equal(TaskbarSource.Uia, dump.Layout.Source);
        Assert.True(TaskbarLayoutComparison.Equivalent(dump.Layout.ToLayout(), back.Reclassify()));
        Assert.Equal(JsonSerializer.Serialize(dump.Layout), JsonSerializer.Serialize(DumpLayout.From(back.Reclassify())));
        Assert.Equal(2, back.Reclassify().AppButtons.Count);
    }

    private static bool HasName(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().Any(p => p.Name.Equals("name", StringComparison.OrdinalIgnoreCase) || HasName(p.Value)),
        JsonValueKind.Array => e.EnumerateArray().Any(HasName),
        _ => false,
    };
}

public sealed class TaskbarEdgesTests
{
    private static readonly PixelRect Mon = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData(0, 1032, 1920, 1080, TaskbarEdge.Bottom)]
    [InlineData(0, 0, 1920, 48, TaskbarEdge.Top)]
    [InlineData(0, 0, 48, 1080, TaskbarEdge.Left)]
    [InlineData(1872, 0, 1920, 1080, TaskbarEdge.Right)]
    [InlineData(10, 1032, 1920, 1080, TaskbarEdge.Unknown)]
    [InlineData(0, 0, 1920, 1080, TaskbarEdge.Unknown)]
    [InlineData(0, 500, 1920, 548, TaskbarEdge.Unknown)]
    [InlineData(0, 0, 0, 0, TaskbarEdge.Unknown)]
    public void InfersEdgeFromRectAndMonitor(int l, int t, int r, int b, TaskbarEdge expected) =>
        Assert.Equal(expected, TaskbarEdges.Infer(new(l, t, r, b), Mon));

    [Fact]
    public void SecondaryMonitorWithNegativeOrigin() =>
        Assert.Equal(TaskbarEdge.Bottom, TaskbarEdges.Infer(new(-1920, 1032, 0, 1080), new(-1920, 0, 0, 1080)));
}
