namespace SideWar.Runtime;

public interface IClock
{
    long NowTicks { get; }
    double TicksPerSecond { get; }
}
