namespace SideWar.Platform.Windows.Interop;

internal static class WaitableTimerDeadline
{
    /// <summary>
    /// Converts a delay to a relative, one-shot Win32 due time in 100 ns units.
    /// Zero and overdue deadlines become -1: zero is an absolute FILETIME, not relative.
    /// </summary>
    internal static long FromDelay(TimeSpan delay) => delay.Ticks <= 0 ? -1 : -delay.Ticks;
}
