using SideWar.Runtime.Timing;
using SideWar.Runtime.Power;
using SideWar.Runtime.Presence;
using Xunit;

namespace SideWar.Runtime.Tests;

public sealed class LoopTests
{
    [Theory]
    [InlineData(10000)]
    [InlineData(100000)]
    [InlineData(1000000)]
    public void TenSecondsProduceExactly300Ticks(long interval)
    {
        var clock = new FakeClock();
        var stepper = new FixedStepper();
        stepper.Reset(clock.Seconds, true);
        int ticks = 0;
        while (clock.NowTicks < 300_000_000)
        {
            clock.AdvanceTicks(interval);
            ticks += stepper.Advance(clock.Seconds, true);
        }
        Assert.Equal(300, ticks);
    }

    [Fact]
    public void LargeJumpDiscardsExcessIncludingPreviousRemainder()
    {
        var stepper = new FixedStepper();
        stepper.Reset(0, true);
        Assert.Equal(0, stepper.Advance(0.03, true));
        Assert.InRange(stepper.Advance(5.03, true), 1, 8);
        Assert.Equal(0, stepper.AccumulatedSeconds);
        Assert.Equal(0, stepper.Advance(5.03, true));
    }

    [Fact]
    public void PauseAndResumeDiscardTimeAndFraction()
    {
        var stepper = new FixedStepper();
        stepper.Reset(0, true);
        stepper.Advance(0.02, true);
        Assert.Equal(0, stepper.Advance(5, false));
        Assert.Equal(0, stepper.Advance(10, true));
        Assert.Equal(1, stepper.Advance(10 + LoopConstants.TickSeconds, true));
        stepper.Reset(20, true); // explicit resume event, even with no wakes while paused
        Assert.Equal(0, stepper.Advance(20, true));
        Assert.Equal(0, stepper.Advance(19, true));
    }

    [Fact]
    public void PauseFlagsRemainIndependent()
    {
        var reasons = PauseReason.User | PauseReason.Hidden | PauseReason.Dialog;
        reasons &= ~PauseReason.Hidden;
        Assert.Equal(PauseReason.User | PauseReason.Dialog, reasons);
        Assert.NotEqual(PauseReason.None, reasons);
    }

    [Fact]
    public void RenderTableCoversBooleanProfilePresenceAndSettingsCombinations()
    {
        foreach (bool visible in new[] { false, true })
        foreach (bool running in new[] { false, true })
        foreach (bool animations in new[] { false, true })
        foreach (bool dirty in new[] { false, true })
        foreach (var presence in Enum.GetValues<PresenceState>())
        foreach (var profile in Enum.GetValues<PowerProfile>())
        foreach (int active in new[] { 20, 30, 60 })
        foreach (int absent in new[] { 4, 8 })
        {
            int fps = presence == PresenceState.Ausente ? absent : active;
            if (profile == PowerProfile.Saver) fps = Math.Min(fps, 15);
            bool hidden = !visible || presence == PresenceState.Offline;
            foreach (double now in new[] { 0, 1.0 / fps - 0.000501, 1.0 / fps - 0.0005, 1.0 / fps })
            {
                var decision = RenderPolicy.Evaluate(new(visible, running, animations, presence,
                    dirty, profile, now, 0, active, absent));
                Assert.Equal(!hidden && (dirty || (running && now >= 1.0 / fps - 0.0005)), decision.RenderNow);
                Assert.Equal(hidden || !running ? (double?)null : 1.0 / fps, decision.NextDeadline);
            }
        }
    }

    [Theory]
    [InlineData(25, 8)]
    [InlineData(30, 5)]
    public void UnsupportedFpsIsRejected(int active, int absent) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RenderPolicy.Evaluate(
            new(true, true, true, PresenceState.Presente, false, PowerProfile.Normal, 0, 0, active, absent)));

    [Theory]
    [InlineData(PresenceState.Presente, true, true, 900, 900)]
    [InlineData(PresenceState.Ausente, true, true, 240, 900)]
    [InlineData(PresenceState.Offline, false, false, 0, 0)]
    [InlineData(PresenceState.Presente, false, true, 0, 0)]
    [InlineData(PresenceState.Presente, true, false, 0, 900)]
    public void FakeConsumerCountsPresentsAndKeepsSimulationIndependent(
        PresenceState presence, bool running, bool moving, int expectedPresents, int expectedTicks)
    {
        var clock = new FakeClock();
        var stepper = new FixedStepper();
        stepper.Reset(0, running);
        int presents = 0, ticks = 0;
        double lastPresent = 0;
        for (int i = 0; i < 3600; i++)
        {
            clock.AdvanceTicks(250000);
            ticks += stepper.Advance(clock.Seconds, running);
            var decision = RenderPolicy.Evaluate(new(true, running, moving, presence,
                false, PowerProfile.Normal, clock.Seconds, lastPresent));
            // Fake unchanged-frame suppression: reduced motion and no moving entities.
            if (decision.RenderNow && moving)
            {
                presents++;
                lastPresent = clock.Seconds;
            }
        }
        Assert.Equal(expectedPresents, presents);
        Assert.Equal(expectedTicks, ticks);
    }
}
