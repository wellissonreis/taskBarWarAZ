namespace SideWar.Runtime.Timing;

public static class LoopConstants
{
    public const int SimHz = 30;
    public const double TickSeconds = 1.0 / SimHz;
    public const double MaxAccumulatedSeconds = 0.25;
    public const int MaxTicksPerWake = 8;
    public const int AmbientFps = 8;
    public const int DefaultActiveFps = 30;
    public const int SaverActiveFps = 15;
    public const double RenderToleranceSeconds = 0.0005;
}
