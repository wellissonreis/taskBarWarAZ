using SideWar.Core;
using SideWar.Core.Simulation;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Presence;
using SideWar.Runtime.Timing;

namespace SideWar.Runtime.Hosting;

/// <summary>
/// Laço lógico (SDD-10 RF-10.5, RF-10.6, RF-10.7, RF-10.10). Só na T-Game. Por <c>OnWake</c>: presença → comandos
/// (entre ticks) → N ticks de 30 Hz (<see cref="FixedStepper"/>) com hooks → <see cref="RenderPolicy"/>.
/// </summary>
public sealed class GameHost : IGameLoopHost
{
    private readonly IClock _clock;
    private readonly AppStateMachine _app;
    private readonly PresenceTracker _presence;
    private readonly SimContext _ctx;
    private readonly IGameFrameSink? _sink;
    private readonly FixedStepper _stepper = new();
    private readonly Queue<IGameCommand> _pending = new();
    private readonly List<IGameTickHook> _hooks = [];
    private readonly PresenceMultiplierConfig? _presenceConfig;

    private bool _visible;
    private PresenceState _lastPresence;
    private int _lastWave;
    private int _lastStage;
    private double _lastPresent = double.NegativeInfinity;
    private double _lastWake;
    private bool _started;
    private double? _renderDeadline;

    public GameHost(IClock clock, AppStateMachine app, PresenceTracker presence, SimState state, SimContext ctx,
        IGameFrameSink? frameSink = null, PresenceMultiplierConfig? presenceConfig = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _presence = presence ?? throw new ArgumentNullException(nameof(presence));
        State = state ?? throw new ArgumentNullException(nameof(state));
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _sink = frameSink;
        _presenceConfig = presenceConfig;
        _lastWave = state.Wave;
        _lastStage = state.Stage;
        _lastPresence = presence.State;
        _visible = app.IsVisible;
        Dirty = true;
    }

    public SimState State { get; }
    public bool Dirty { get; private set; }
    public bool AnimationsEnabled { get; set; } = true;
    public int ActiveFps { get; set; } = LoopConstants.DefaultActiveFps;
    public int AbsentFps { get; set; } = LoopConstants.AmbientFps;
    public double PresentationSeconds { get; private set; }
    public long TicksExecuted { get; private set; }
    public long PresentCount { get; private set; }
    public bool IsShutdown { get; private set; }

    /// <summary>Resultados dos comandos aplicados, na ordem; o consumidor (HUD/feedback) esvazia com <see cref="DrainResults"/>.</summary>
    private readonly Queue<(IGameCommand Command, CommandResult Result)> _results = new();

    public void AddHook(IGameTickHook hook) => _hooks.Add(hook ?? throw new ArgumentNullException(nameof(hook)));

    public void MarkDirty() => Dirty = true;

    public bool TryDequeueResult(out IGameCommand command, out CommandResult result)
    {
        if (_results.TryDequeue(out var r)) { command = r.Command; result = r.Result; return true; }
        command = null!; result = default;
        return false;
    }

    /// <summary>Registra interação do jogador (clique/hotkey) na presença (SDD-25).</summary>
    public void InteractionObserved()
    {
        _presence.InteractionObserved(_clock.NowTicks);
        Dirty = true;
    }

    public long? NextDeadline
    {
        get
        {
            if (IsShutdown) return null;
            if (_pending.Count > 0 || !_started) return _clock.NowTicks;
            double tps = _clock.TicksPerSecond;
            double? next = _renderDeadline;
            if (_app.SimulationRunning)
            {
                double tick = _lastWake + (LoopConstants.TickSeconds - _stepper.AccumulatedSeconds);
                next = next is null ? tick : Math.Min(next.Value, tick);
            }

            long? best = next is null ? null : (long)Math.Ceiling(next.Value * tps);
            if (_presence.NextDeadlineTicks is long p && (best is null || p < best)) best = p;
            return best;
        }
    }

    /// <summary>Enfileira; aplicado no início do próximo <see cref="OnWake"/>, antes dos ticks (RF-10.6).</summary>
    public void OnCommand(IGameCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (IsShutdown) return;
        _pending.Enqueue(command);
    }

    public void OnWake(IClock clock)
    {
        if (IsShutdown) return;
        long nowTicks = clock.NowTicks;
        double now = nowTicks / clock.TicksPerSecond;

        SyncVisibility(nowTicks);
        _presence.Advance(nowTicks);
        if (_presence.State != _lastPresence) { _lastPresence = _presence.State; Dirty = true; }

        DrainCommands();

        bool running = _app.SimulationRunning;
        double dt = _started ? Math.Clamp(now - _lastWake, 0, LoopConstants.MaxAccumulatedSeconds) : 0;
        if (running && _started) PresentationSeconds += dt;
        int ticks = _stepper.Advance(now, running);
        for (int i = 0; i < ticks; i++) RunTick(now);
        _lastWake = now;
        _started = true;

        if (State.Wave != _lastWave || State.Stage != _lastStage)
        {
            _lastWave = State.Wave;
            _lastStage = State.Stage;
            Dirty = true;
        }

        Render(now, running);
    }

    public void Shutdown()
    {
        IsShutdown = true;
        _pending.Clear();
    }

    private void SyncVisibility(long nowTicks)
    {
        bool visible = _app.IsVisible;
        if (visible == _visible) return;
        _visible = visible;
        _presence.VisibilityChanged(visible, nowTicks);
        Dirty = true;
    }

    private void DrainCommands()
    {
        while (_pending.TryDequeue(out var cmd))
        {
            var result = State.Apply(_ctx, cmd);
            _results.Enqueue((cmd, result));
            if (result.IsOk) Dirty = true;
        }
    }

    private void RunTick(double wallSeconds)
    {
        // CA-10.2/10.3 SDD-10: multiplicador de presença escrito entre ticks, nunca dentro do Step.
        State.PresenceMultiplier = _presence.Multiplier(_presenceConfig);
        var tc = new GameTickContext(State.Tick, State.Tick / (double)LoopConstants.SimHz, wallSeconds);
        for (int i = 0; i < _hooks.Count; i++) _hooks[i].BeforeStep(State, tc);
        State.Step(_ctx);
        TicksExecuted++;
        var after = new GameTickContext(State.Tick, State.Tick / (double)LoopConstants.SimHz, wallSeconds);
        for (int i = 0; i < _hooks.Count; i++) _hooks[i].AfterStep(State, after);
    }

    private void Render(double now, bool running)
    {
        // Com animações reduzidas e sem entidades em movimento, a cena é estática: só a sujeira pede frame (CA-10.3).
        bool animating = running && (AnimationsEnabled || State.Enemies.Count > 0);
        var inputs = new RenderInputs(
            _app.IsVisible, animating, AnimationsEnabled, _presence.State, Dirty, _app.PowerProfile,
            now, _lastPresent, ActiveFps, AbsentFps);
        var decision = RenderPolicy.Evaluate(inputs);
        _renderDeadline = decision.NextDeadline;
        if (!decision.RenderNow) return;
        _sink?.Present(State, PresentationSeconds);
        PresentCount++;
        _lastPresent = now;
        Dirty = false;
        // O deadline devolvido refere-se ao LastPresent anterior: recalcula a partir deste frame.
        _renderDeadline = RenderPolicy.Evaluate(inputs with { Dirty = false, LastPresent = now }).NextDeadline;
    }
}
