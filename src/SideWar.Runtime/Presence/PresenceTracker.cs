namespace SideWar.Runtime.Presence;

/// <summary>
/// Máquina de presença (SDD-25 RF-25.1). Pura, sem Win32: recebe interação e visibilidade por parâmetro.
/// Todos os instantes são ticks de <see cref="IClock"/>. Chamado só na T-Game.
/// </summary>
public sealed class PresenceTracker
{
    public const double AbsentAfterSeconds = 300;
    private readonly IClock _clock;
    private readonly Func<DateTime> _utcNow;
    private readonly long _absentAfterTicks;
    private readonly long _marginTicks;
    private bool _visible;
    private long? _lastInteractionTicks;
    private long _lastUpdateTicks;
    private DateTime? _lastInteractionUtc;

    public PresenceTracker(IClock clock, Func<DateTime>? utcNow = null)
    {
        _clock = clock;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _absentAfterTicks = (long)(AbsentAfterSeconds * clock.TicksPerSecond);
        _marginTicks = Math.Max(1, (long)(clock.TicksPerSecond / 1000.0)); // 1 ms
        _lastUpdateTicks = clock.NowTicks;
        State = PresenceState.Offline; // Starting: oculto
    }

    public PresenceState State { get; private set; }
    public double PresentSeconds { get; private set; }
    public DateTime? LastInteractionUtc => _lastInteractionUtc;

    /// <summary>Instante (ticks) da transição Presente → Ausente, ou null se não há deadline.</summary>
    public long? NextDeadlineTicks =>
        State == PresenceState.Presente ? _lastInteractionTicks!.Value + _absentAfterTicks + _marginTicks : null;

    public double Multiplier(PresenceMultiplierConfig? config = null) =>
        PresenceMultiplier.Evaluate(State, PresentSeconds, config);

    /// <summary>Reavalia o estado em <paramref name="nowTicks"/> (chamar no deadline ou a cada frame).</summary>
    public void Advance(long nowTicks)
    {
        if (nowTicks < _lastUpdateTicks) nowTicks = _lastUpdateTicks;
        if (State == PresenceState.Presente)
        {
            var absentAt = _lastInteractionTicks!.Value + _absentAfterTicks; // estritamente maior
            if (nowTicks > absentAt)
            {
                PresentSeconds = 0;
                State = PresenceState.Ausente;
            }
            else
            {
                PresentSeconds += (nowTicks - _lastUpdateTicks) / _clock.TicksPerSecond;
            }
        }
        _lastUpdateTicks = nowTicks;
    }

    /// <summary>Clique em janela do jogo ou hotkey. Ignorada enquanto Offline.</summary>
    public void InteractionObserved(long nowTicks)
    {
        Advance(nowTicks);
        if (!_visible) return;
        RecordInteraction(_lastUpdateTicks);
        if (State == PresenceState.Ausente)
        {
            PresentSeconds = 0;
            State = PresenceState.Presente;
        }
    }

    /// <summary>
    /// Visibilidade vinda da AppStateMachine. <paramref name="byUser"/>: Show por ação do usuário
    /// conta como interação; Show automático não conta.
    /// </summary>
    public void VisibilityChanged(bool visible, long nowTicks, bool byUser = false)
    {
        Advance(nowTicks);
        if (!visible)
        {
            _visible = false;
            PresentSeconds = 0;
            State = PresenceState.Offline;
            return;
        }
        if (_visible) { if (byUser) InteractionObserved(nowTicks); return; }
        _visible = true;
        PresentSeconds = 0;
        if (byUser) RecordInteraction(_lastUpdateTicks);
        State = _lastInteractionTicks is long last && _lastUpdateTicks - last <= _absentAfterTicks
            ? PresenceState.Presente
            : PresenceState.Ausente;
    }

    private void RecordInteraction(long ticks)
    {
        _lastInteractionTicks = ticks;
        _lastInteractionUtc = _utcNow();
    }
}
