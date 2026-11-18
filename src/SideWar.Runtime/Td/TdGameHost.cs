using SideWar.Core;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Composition;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Timing;

namespace SideWar.Runtime.Td;

/// <summary>Consumidor dos fatos da partida TD (render/HUD/perfil). Chamado na T-Game, fora do Step.</summary>
public interface ITdMatchObserver
{
    /// <summary>Eventos acumulados desde a última chamada; o host chama <c>ClearEvents</c> depois.</summary>
    void OnEvents(TdMatchState match, IReadOnlyList<TdEvent> events);

    /// <summary>Uma vez por partida, ao entrar em Vitória ou Derrota.</summary>
    void OnMatchEnded(TdMatchState match, TdMatchResult result);

    /// <summary>Registra uma corrida ainda em andamento ao encerrar o aplicativo.</summary>
    void OnMatchAbandoned(TdMatchState match) { }
}

/// <summary>
/// Pedido "Iniciar onda" (hotkey/bandeja): vira <see cref="StartWave"/> em Preparo ou <see cref="CallNextWave"/> em Onda,
/// decidido no momento da aplicação (entre ticks). Só o comando traduzido entra no <see cref="TdGameHost.CommandLog"/>.
/// </summary>
public sealed record TdWaveRequest : IGameCommand;

/// <summary>
/// Host da partida TD (SDD-28, SDD-10 adaptado). Só na T-Game, ao lado do <c>CompositeLoopHost</c> como
/// <see cref="ILoopParticipant"/>. Por <c>OnWake</c>: comandos (fila, entre ticks) → N ticks de 30 Hz →
/// eventos → fim de partida. Sem presença, offline nem eventos ao vivo.
/// <list type="bullet">
/// <item>Preparo: 0 fps e nenhum <c>Step</c>, exceto com auto-início em contagem (a contagem só corre com Step).</item>
/// <item>A simulação pausa sempre que <see cref="AppStateMachine.SimulationRunning"/> é falso (fullscreen, sessão bloqueada,
/// ocultar por bandeja/hotkey, pausa do usuário): sem Step, sem deadline, sem acúmulo de tempo na volta.</item>
/// </list>
/// </summary>
public sealed class TdGameHost : ILoopParticipant, IGameCommandSink
{
    private readonly IClock _clock;
    private readonly AppStateMachine _app;
    private readonly ITdMatchObserver? _observer;
    private readonly ITdPresence? _presence;
    private readonly FixedStepper _stepper = new();
    private readonly Queue<IGameCommand> _pending = new();
    private readonly List<TdLogEntry> _log = [];
    private readonly Queue<(IGameCommand Command, CommandResult Result)> _results = new();
    private SideWar.Core.Td.LayoutChanged? _lastLayout;
    private double _lastWake;
    private bool _started;
    private bool _endReported;

    public TdGameHost(IClock clock, AppStateMachine app, TdMatchState match, ITdMatchObserver? observer = null, ITdPresence? presence = null)
    {
        _presence = presence;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _app = app ?? throw new ArgumentNullException(nameof(app));
        Match = match ?? throw new ArgumentNullException(nameof(match));
        _observer = observer;
    }

    /// <summary>TD.17: portão do pedido "Iniciar onda" em Preparo (ex.: fase 1 do tutorial espera a 1ª torre). Falso ⇒ o pedido é descartado e não entra no log.</summary>
    public Func<bool>? WaveGate { get; set; }

    /// <summary>
    /// TD.28c: velocidade do jogo (1x, 2x, 3x): cada tick de 30 Hz executa N Steps. Determinístico (o Core só vê Steps e comandos entre eles;
    /// o log guarda o Frame de cada comando, então o replay reproduz igual) e independente da pausa da modal (a pausa zera os ticks).
    /// </summary>
    public int GameSpeed { get; set; } = 1;

    public TdMatchState Match { get; private set; }
    public bool IsShutdown { get; private set; }

    /// <summary>Steps executados (inclusive em Preparo com auto-início).</summary>
    public long StepsExecuted { get; private set; }

    /// <summary>Comandos aplicados, na ordem, com o <c>Frame</c> da partida no momento (replay: <see cref="TdReplayScript"/>).</summary>
    public IReadOnlyList<TdLogEntry> CommandLog => _log;

    /// <summary>TD.28b: script de replay da partida/corrida atual; <c>Options</c> do Core vai junto (sem elas o replay seria jogado no modo clássico).</summary>
    public TdReplayScript BuildReplay(int startStage, ulong seedState, ulong seedStream) =>
        new(startStage, seedState, seedStream, [.. _log], Match.Frame, Match.Options);

    /// <summary>Verdadeiro quando a partida está viva (Preparo/Onda) e nada pausa a simulação.</summary>
    public bool SimulationActive => !IsShutdown && _app.SimulationRunning && Match.Phase is MatchPhase.Preparo or MatchPhase.Onda;

    /// <summary>Precisa de Step: Onda, ou Preparo com a contagem de auto-início ligada (senão 0 fps).</summary>
    private bool NeedsSteps => Match.Phase == MatchPhase.Onda
        || (Match.Phase == MatchPhase.Preparo && Match.AutoStart == AutoStartMode.Countdown);

    private bool Running => !IsShutdown && _app.SimulationRunning && !Match.Paused && NeedsSteps;

    public long? NextDeadline
    {
        get
        {
            if (IsShutdown) return null;
            if (_pending.Count > 0 || !_started) return _clock.NowTicks;
            if (!Running) return null;
            double tick = _lastWake + (LoopConstants.TickSeconds - _stepper.AccumulatedSeconds);
            return (long)Math.Ceiling(tick * _clock.TicksPerSecond);
        }
    }

    public bool TryDequeueResult(out IGameCommand command, out CommandResult result)
    {
        if (_results.TryDequeue(out var r)) { command = r.Command; result = r.Result; return true; }
        command = null!; result = default;
        return false;
    }

    /// <summary>Enfileira; aplicado no início do próximo <see cref="OnWake"/>, antes dos ticks.</summary>
    public void OnCommand(IGameCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (IsShutdown) return;
        _pending.Enqueue(command);
    }

    /// <summary>Destino do <c>SceneLayoutWatcher</c> do modo TD: <c>LayoutChanged</c> entra na fila e é aplicado entre ticks (TD.7).</summary>
    void IGameCommandSink.Post(IGameCommand command) => OnCommand(command);

    /// <summary>"Iniciar onda" da hotkey/bandeja.</summary>
    public void RequestWave() => OnCommand(new TdWaveRequest());

    /// <summary>Aplica as opções de auto-início (padrão desligado; segundos 5 a 60) como comando <see cref="SetAutoStart"/>.</summary>
    public void ApplyAutoStart(bool enabled, int seconds) =>
        OnCommand(new SetAutoStart(enabled ? AutoStartMode.Countdown : AutoStartMode.Off, Math.Clamp(seconds, 5, 60)));

    /// <summary>Troca a partida (nova fase/revanche). O log recomeça; o último layout da barra é reaplicado na nova partida.</summary>
    public void Restart(TdMatchState next)
    {
        Match = next ?? throw new ArgumentNullException(nameof(next));
        if (_lastLayout is not null) Match.Apply(_lastLayout);
        _pending.Clear();
        _log.Clear();
        _endReported = false;
        _stepper.Reset(_clock.NowTicks / _clock.TicksPerSecond, simRunning: false);
    }

    public void OnWake(IClock clock)
    {
        if (IsShutdown) return;
        double now = clock.NowTicks / clock.TicksPerSecond;

        DrainCommands();
        UpdatePresence();

        bool running = Running;
        int ticks = _stepper.Advance(now, running);
        int steps = ticks * Math.Clamp(GameSpeed, 1, 3);
        for (int i = 0; i < steps && NeedsSteps; i++)
        {
            Match.Step();
            StepsExecuted++;
        }
        _lastWake = now;
        _started = true;

        if (Match.Events.Count > 0)
        {
            _observer?.OnEvents(Match, Match.Events);
            Match.ClearEvents();
        }
        if (!_endReported && Match.Phase is MatchPhase.Vitoria or MatchPhase.Derrota)
        {
            _endReported = true;
            _observer?.OnMatchEnded(Match, Match.Result);
        }
    }

    public void Shutdown()
    {
        if (IsShutdown) return;
        if (Match.Options.PersistentRun && Match.Phase is MatchPhase.Preparo or MatchPhase.Onda)
            _observer?.OnMatchAbandoned(Match);
        IsShutdown = true;
        _pending.Clear();
    }

    /// <summary>
    /// Sinal "presente agora" (RF-28.19, TD.14): só em Onda e com a simulação rodando, lê <see cref="ITdPresence"/> (ausente após ativar
    /// outro app; presente ao interagir com o SideWar) e envia <see cref="SetPresence"/> ao log apenas quando o estado muda.
    /// O Core usa só o último estado registrado (determinismo).
    /// </summary>
    private void UpdatePresence()
    {
        if (_presence is null || !_app.SimulationRunning || Match.Phase != MatchPhase.Onda) return;
        bool present = _presence.IsPresent;
        if (present != Match.Present) Apply(new SetPresence(present));
    }

    private void DrainCommands()
    {
        while (_pending.TryDequeue(out var cmd))
        {
            if (cmd is TdWaveRequest && Match.Phase == MatchPhase.Preparo && WaveGate is { } gate && !gate()) continue;
            var applied = cmd is TdWaveRequest
                ? (Match.Phase == MatchPhase.Preparo ? new StartWave() : (IGameCommand)new CallNextWave())
                : cmd;
            Apply(applied);
        }
    }

    private void Apply(IGameCommand applied)
    {
        if (applied is SideWar.Core.Td.LayoutChanged layout) _lastLayout = layout;
        var result = Match.Apply(applied);
        int seq = _log.Count > 0 && _log[^1].Frame == Match.Frame ? _log[^1].Seq + 1 : 0;
        _log.Add(new TdLogEntry(Match.Frame, applied, seq));
        _results.Enqueue((applied, result));
    }
}
