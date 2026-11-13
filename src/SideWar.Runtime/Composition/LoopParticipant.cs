namespace SideWar.Runtime.Composition;

/// <summary>Adaptador de <see cref="ILoopParticipant"/> por delegates (serviços que já expõem OnWake/NextDeadline).</summary>
public sealed class LoopParticipant(Func<long?> nextDeadline, Action<IClock> onWake) : ILoopParticipant
{
    private readonly Func<long?> _next = nextDeadline ?? throw new ArgumentNullException(nameof(nextDeadline));
    private readonly Action<IClock> _wake = onWake ?? throw new ArgumentNullException(nameof(onWake));

    public long? NextDeadline => _next();
    public void OnWake(IClock clock) => _wake(clock);
}
