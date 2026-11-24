using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Runtime.Tests.Td;

public sealed class TdSpeedStabilityTests
{
    private readonly ITestOutputHelper output;
    public TdSpeedStabilityTests(ITestOutputHelper output) => this.output = output;

    private sealed class Sink : ITdFrameSink
    {
        public int Frames;
        public void Present(TdMatchState match, double nowSeconds, in TdView view) => Frames++;
    }

    [Theory]
    [InlineData(4)] [InlineData(5)] [InlineData(8)] [InlineData(10)]
    [InlineData(16)] [InlineData(20)] [InlineData(50)] [InlineData(TdSpeed.Max)]
    public void Extra_speeds_progress_and_render_with_immediate_autostart(int speed)
    {
        var clock = new FakeClock();
        var app = new AppStateMachine();
        app.LeaveHidden(HiddenReason.Starting);
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(42, 54));
        var host = new TdGameHost(clock, app, match) { ExtraSpeeds = true, GameSpeed = speed };
        var sink = new Sink();
        var render = new TdRenderDriver(host, app, clock, sink);
        host.OnCommand(new LayoutChanged(TdSanityBot.TypicalLayout()));
        host.OnCommand(new BuildTower("bolt", 600));
        host.OnCommand(new SetAutoStart(AutoStartMode.Immediate, 5));
        host.OnCommand(new SetShadow(true));
        host.OnWake(clock);
        Assert.True(match.Shadow);
        host.OnCommand(new SetShadow(false));
        host.OnWake(clock);
        Assert.False(match.Shadow);
        host.OnCommand(new SetShadow(true));
        host.OnWake(clock);
        Assert.True(match.Shadow);
        long maxSteps = 0;
        double maxMs = 0;
        for (int i = 0; i < 200; i++)
        {
            if (i == 30) host.OnCommand(new SetShadow(false));
            if (i == 60) host.OnCommand(new SetShadow(true));
            host.GameSpeed = i % 20 == 0 ? 1 : speed;
            clock.AdvanceTicks((long)(clock.TicksPerSecond / 30));
            long before = host.StepsExecuted;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            host.OnWake(clock);
            watch.Stop();
            maxMs = Math.Max(maxMs, watch.Elapsed.TotalMilliseconds);
            maxSteps = Math.Max(maxSteps, host.StepsExecuted - before);
            render.OnWake(clock);
        }
        output.WriteLine($"speed={speed} steps={host.StepsExecuted} maxSteps/frame={maxSteps} maxMs/frame={maxMs:F2} renders={sink.Frames} phase={match.Phase}");
        Assert.True(host.StepsExecuted > 100);
        Assert.InRange(maxSteps, 1, 1000);
        Assert.True(sink.Frames > 10);
        Assert.True(match.Tick > 0);
    }

    [Fact]
    public void Max_speed_is_bounded_even_if_budget_clock_stalls()
    {
        var clock = new FakeClock();
        var app = new AppStateMachine();
        app.LeaveHidden(HiddenReason.Starting);
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(42, 54));
        int reads = 0;
        var host = new TdGameHost(clock, app, match) { ExtraSpeeds = true, GameSpeed = TdSpeed.Max,
            BudgetClockMs = () => ++reads > 10000 ? 100 : 0 };
        host.OnCommand(new SetAutoStart(AutoStartMode.Immediate, 5));
        host.OnWake(clock);
        clock.AdvanceTicks((long)(clock.TicksPerSecond / 30));
        host.OnWake(clock);
        Assert.InRange(host.StepsExecuted, 1, 1000);
    }
}
