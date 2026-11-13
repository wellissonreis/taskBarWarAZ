namespace SideWar.Runtime.Timing;

/// <summary>
/// Single re-armable debounce deadline (SDD-16 RF-16.8): each trigger replaces the pending deadline with
/// <c>now + delay</c> (the last trigger wins), so a burst collapses into one action after the burst goes quiet.
/// No timer of its own: the host reads <see cref="Deadline"/> into its single wait.
/// </summary>
public struct RearmableDeadline
{
    public long? Deadline { get; private set; }

    public void Trigger(long now, long delayTicks) => Deadline = now + delayTicks;

    public readonly bool IsDue(long now) => Deadline is { } due && now >= due;

    public void Clear() => Deadline = null;
}
