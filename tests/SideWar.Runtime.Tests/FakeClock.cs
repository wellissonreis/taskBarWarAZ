namespace SideWar.Runtime.Tests;

internal sealed class FakeClock : IClock
{
    public long NowTicks { get; private set; }
    public double TicksPerSecond => 30_000_000;
    public double Seconds => NowTicks / TicksPerSecond;
    public void AdvanceTicks(long ticks) => NowTicks += ticks;
}
