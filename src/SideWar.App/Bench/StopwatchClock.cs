using System.Diagnostics;
using SideWar.Runtime;

namespace SideWar.App.Bench;

/// <summary>Same monotonic source as the T-Game clock (Stopwatch ticks), for services that need an IClock at construction.</summary>
internal sealed class StopwatchClock : IClock
{
    public long NowTicks => Stopwatch.GetTimestamp();
    public double TicksPerSecond => Stopwatch.Frequency;
}
