using SideWar.Core;

namespace SideWar.Runtime.Composition;

/// <summary>
/// Host único da T-Game no App de produção (SDD-01 RF-01.5, T4.10): por wake, os participantes de entrada
/// (barra, fullscreen) → o laço lógico (<c>GameHost</c>: presença, comandos, ticks, <c>RenderPolicy</c>) → os
/// participantes de saída (widget). O próximo deadline é o menor de todos. Comandos do App (hotkeys, menus)
/// são interceptados antes de chegar à simulação; o resto vai ao laço lógico, aplicado entre ticks.
/// O <see cref="Shutdown"/> encerra o laço lógico primeiro (rejeita comandos) e depois a sequência de recursos
/// da T-Game em ordem reversa (RF-07.10 passo 3). Só na T-Game.
/// </summary>
public sealed class CompositeLoopHost : IGameLoopHost
{
    private readonly IGameLoopHost _game;
    private readonly List<ILoopParticipant> _before = [];
    private readonly List<ILoopParticipant> _after = [];
    private readonly Action<Exception> _report;
    private bool _shutdown;

    public CompositeLoopHost(IGameLoopHost game, Action<Exception>? report = null)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _report = report ?? (_ => { });
    }

    /// <summary>Comandos do App (hotkeys, menu, instância única): devolve true se tratou; senão vai à simulação.</summary>
    public Func<IGameCommand, bool>? Intercept { get; set; }

    /// <summary>Recursos da T-Game encerrados em ordem reversa no <see cref="Shutdown"/> (unhook, hotkeys, janelas, superfícies).</summary>
    public LifecycleSequence Resources { get; } = new();

    public bool IsShutdown => _shutdown;

    public void AddBefore(ILoopParticipant participant) => _before.Add(participant ?? throw new ArgumentNullException(nameof(participant)));

    public void AddAfter(ILoopParticipant participant) => _after.Add(participant ?? throw new ArgumentNullException(nameof(participant)));

    public long? NextDeadline
    {
        get
        {
            if (_shutdown) return null;
            long? next = _game.NextDeadline;
            foreach (var p in _before) next = Min(next, p.NextDeadline);
            foreach (var p in _after) next = Min(next, p.NextDeadline);
            return next;
        }
    }

    public void OnWake(IClock clock)
    {
        if (_shutdown) return;
        // Uma falha de um serviço não derruba os demais (RF-03.8); a política é do App (reportError).
        foreach (var p in _before) Guard(() => p.OnWake(clock));
        _game.OnWake(clock);
        foreach (var p in _after) Guard(() => p.OnWake(clock));
    }

    public void OnCommand(IGameCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (_shutdown) return;
        if (Intercept?.Invoke(command) == true) return;
        _game.OnCommand(command);
    }

    public void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true;
        try { _game.Shutdown(); } catch (Exception e) { _report(e); }
        foreach (var failure in Resources.StopAll()) _report(failure);
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception e) { _report(e); }
    }

    private static long? Min(long? a, long? b) => a is null ? b : b is null ? a : Math.Min(a.Value, b.Value);
}
