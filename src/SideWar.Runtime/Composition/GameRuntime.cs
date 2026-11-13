using SideWar.Core;
using SideWar.Core.Content;
using SideWar.Core.Simulation;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Hosting;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Power;
using SideWar.Runtime.Presence;
using SideWar.Runtime.Taskbar;
using SideWar.Runtime.Widget;

namespace SideWar.Runtime.Composition;

/// <summary>
/// Parte pura (sem Win32) da raiz de composição do App (T4.10, SDD-01 RF-01.3): conteúdo, simulação, máquina de
/// estados do App, presença, <see cref="GameHost"/> com a <c>RenderPolicy</c> por presença (Presente = fps ativo,
/// Ausente = fps de ausente, Offline/oculto = 0; SDD-10 CA-10.3), o <see cref="SceneLayoutWatcher"/> (barra →
/// <c>LayoutChanged</c> entre ticks) e o roteamento dos efeitos de ciclo de vida. Também agenda o
/// <c>ReleaseCaches</c> 60 s após o Hide (RF-07.3 passo 7). Só na T-Game depois de construído.
/// </summary>
public sealed class GameRuntime : IPresenceSink, ILoopParticipant
{
    private readonly IClock _clock;
    private long? _releaseCachesAt;

    public GameRuntime(GameRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _clock = options.Clock ?? throw new ArgumentException("Clock is required.", nameof(options));
        Content = options.Content ?? ContentLoader.LoadEmbedded();
        Context = new SimContext(Content);
        App = new AppStateMachine();
        Presence = new PresenceTracker(_clock, options.UtcNow);
        Restored = options.Restore?.Invoke(Context);
        State = Restored ?? SimState.CreateInitial(Content, options.InitialLayout ?? DefaultLayout(1920),
            Rng.Seed(options.Seed, 54));
        Host = new GameHost(_clock, App, Presence, State, Context, options.FrameSink)
        {
            ActiveFps = options.ActiveFps,
            AbsentFps = options.AbsentFps,
            AutoScaleEnabled = options.AutoScale,
            AutoScaleAccumSeconds = options.AutoScaleAccumSeconds,
        };
        ReducedMotion = options.ReducedMotion;
        UpdateAnimations();
        Commands = new HostCommandSink(Host);
        LayoutWatcher = new SceneLayoutWatcher(Commands, State.Layout);
    }

    public GameContent Content { get; }
    public SimContext Context { get; }
    public AppStateMachine App { get; }
    public PresenceTracker Presence { get; }
    public SimState State { get; }

    /// <summary>A partida veio do save (não é nova).</summary>
    public SimState? Restored { get; }
    public GameHost Host { get; }

    /// <summary>Fila da simulação na T-Game (comandos aplicados entre ticks pelo <see cref="GameHost"/>).</summary>
    public IGameCommandSink Commands { get; }

    public SceneLayoutWatcher LayoutWatcher { get; }

    /// <summary>Executor dos efeitos de plataforma; nulo = só o estado lógico muda (testes headless).</summary>
    public IAppEffectHandler? Effects { get; set; }

    public bool ReducedMotion { get; private set; }

    /// <summary>Efeitos executados, na ordem (diagnóstico e testes).</summary>
    public int EffectCount { get; private set; }

    public long? NextDeadline => _releaseCachesAt;

    /// <summary>Faixa padrão (hospedeiro Band, sem barra conhecida) para uma largura de monitor em DIP.</summary>
    public static StripLayout DefaultLayout(int widthDip) =>
        StripLayoutFactory.Create(BuildZoneMap.From(default, widthDip, new BuildZoneOptions()), widthDip);

    /// <summary>Primeiro frame pronto: remove <see cref="HiddenReason.Starting"/> (SDD-07 RF-07.1).</summary>
    public void CompleteStartup() => Dispatch(App.LeaveHidden(HiddenReason.Starting));

    /// <summary>Hotkey/bandeja/menu "Ocultar/Mostrar overlay" (RF-07.1, motivo <c>User</c>).</summary>
    public void ToggleVisibilityByUser() =>
        Dispatch((App.HiddenReasons & HiddenReason.User) != 0 ? App.LeaveHidden(HiddenReason.User) : App.EnterHidden(HiddenReason.User));

    /// <summary>Segunda instância (RF-07.8): remove <c>User</c>; o modo Interativo é da plataforma.</summary>
    public void ShowByUser() => Dispatch(App.LeaveHidden(HiddenReason.User));

    public void TogglePause() =>
        Dispatch((App.PauseReasons & Timing.PauseReason.User) != 0 ? App.ClearPause(Timing.PauseReason.User) : App.SetPause(Timing.PauseReason.User));

    public void EnterHidden(HiddenReason reason) => Dispatch(App.EnterHidden(reason));

    public void LeaveHidden(HiddenReason reason) => Dispatch(App.LeaveHidden(reason));

    public void SetPowerProfile(PowerProfile profile)
    {
        Dispatch(App.SetPowerProfile(profile));
        UpdateAnimations();
    }

    /// <summary>Configurações que afetam o laço (SDD-15): fps de Presente/Ausente, <c>reducedMotion</c>, auto-escala.</summary>
    public void ApplySettings(int activeFps, int absentFps, bool reducedMotion, bool autoScale)
    {
        Host.ActiveFps = activeFps;
        Host.AbsentFps = absentFps;
        Host.AutoScaleEnabled = autoScale;
        ReducedMotion = reducedMotion;
        UpdateAnimations();
        Host.MarkDirty();
    }

    /// <summary>Clique/hotkey em janela do jogo (SDD-25 RF-25.1).</summary>
    public void NotifyInteraction() => Host.InteractionObserved();

    /// <summary>Monitor/barra/opções observados pela plataforma: posta <c>LayoutChanged</c> se a faixa mudou.</summary>
    public SceneLayoutUpdate? ObserveLayout(MonitorInfo monitor, TaskbarLayout bar, BuildZoneOptions options) =>
        LayoutWatcher.Observe(monitor, bar, options);

    /// <summary>Executa os efeitos na ordem; Pause/Resume só marcam o frame como sujo.</summary>
    public void Dispatch(IReadOnlyList<AppEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        foreach (var e in effects)
        {
            EffectCount++;
            switch (e.Kind)
            {
                case AppEffectKind.Show:
                    _releaseCachesAt = null;
                    Effects?.Show(e.HiddenReason);
                    break;
                case AppEffectKind.Hide:
                    _releaseCachesAt = _clock.NowTicks + (long)(AppStateMachine.ReleaseCachesDelaySeconds * _clock.TicksPerSecond);
                    Effects?.Hide(e.HiddenReason);
                    break;
                case AppEffectKind.Save: Effects?.Save(e.SaveTrigger); break;
                case AppEffectKind.ReleaseCaches: Effects?.ReleaseCaches(); break;
                case AppEffectKind.RestoreContext: Effects?.RestoreContext(); break;
                case AppEffectKind.SetEcoQos: Effects?.SetEcoQos(e.EcoQosOn); break;
                case AppEffectKind.Pause:
                case AppEffectKind.Resume:
                    Host.MarkDirty();
                    break;
            }
        }
        UpdateAnimations(); // o perfil de energia pode ter mudado por fora (SessionPowerCoordinator)
    }

    public void OnWake(IClock clock)
    {
        if (_releaseCachesAt is { } due && clock.NowTicks >= due)
        {
            _releaseCachesAt = null;
            Dispatch(App.ReleaseCachesTimerElapsed());
        }
    }

    // SDD-20: animationsEnabled = !reducedMotion && !Saver.
    private void UpdateAnimations() => Host.AnimationsEnabled = !ReducedMotion && App.PowerProfile != PowerProfile.Saver;

    private sealed class HostCommandSink(GameHost host) : IGameCommandSink
    {
        public void Post(IGameCommand command) => host.OnCommand(command);
    }
}
