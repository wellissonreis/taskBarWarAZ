using System.Diagnostics;
using SideWar.Core;
using SideWar.Persistence.Settings;
using SideWar.Platform.Windows.Fullscreen;
using SideWar.Platform.Windows.Input;
using SideWar.Platform.Windows.Rendering;
using SideWar.Platform.Windows.Session;
using SideWar.Platform.Windows.Taskbar;
using SideWar.Platform.Windows.Threading;
using SideWar.Platform.Windows.Windows;
using SideWar.Rendering.Animation;
using SideWar.Rendering.Icons;
using SideWar.Rendering.Scene;
using SideWar.Rendering.Sprites;
using SideWar.Rendering.Text;
using SideWar.Rendering.Widget;
using SideWar.Runtime;
using SideWar.Runtime.Composition;
using SideWar.Runtime.Fullscreen;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Input;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Presence;
using SideWar.Runtime.Taskbar;
using SideWar.Runtime.Widget;

namespace SideWar.App.Composition;

/// <summary>
/// Lado T-Game da raiz de composição (T4.10). <see cref="Setup"/> cria, na ordem, superfície e renderers da faixa,
/// widget, dock, barra de tarefas (<see cref="TaskbarService"/> + <see cref="SceneLayoutWatcher"/>; hospedeiro Band
/// enquanto <c>towersInBar</c> for falso), modo/peek, hotkeys, sessão/energia e fullscreen; cada recurso entra em
/// <see cref="CompositeLoopHost.Resources"/> e é encerrado em ordem reversa no <c>Shutdown</c> da T-Game
/// (SDD-07 RF-07.10 passo 3). Executa os efeitos da <see cref="AppStateMachine"/>. Só na T-Game depois de construído.
/// </summary>
internal sealed class GameComposition : IAppEffectHandler, IDisposable
{
    private const double WidgetIntervalSeconds = 0.5; // SDD-23: no máximo 2 atualizações de texto por segundo
    private const double ReenumerateDelaySeconds = 1; // SDD-07 RF-07.7

    private readonly GameRuntime runtime;
    private readonly CompositeLoopHost loop;
    private readonly SceneFrameSink sink;
    private readonly SessionPersistence persistence;
    private readonly Action<string> log;
    private readonly Action<Exception> report;
    private readonly Action<UiRequest> ui;
    private readonly SideWar.App.Bench.StopwatchClock clock = new();
    private readonly WidgetModelBuilder widgetModel = new();
    private AppSettings settings;
    private GameThread game = null!;
    private LayeredSurface? surface;
    private Typography? typography;
    private IconCatalog? icons;
    private SpriteAtlas? atlas;
    private SceneRenderer? sceneRenderer;
    private WidgetRenderer? widgetRenderer;
    private WidgetFramePainter? widgetPainter;
    private WidgetController? widget;
    private OfflineCardController? card;
    private OfflineCardPresenter? cardPresenter;
    private OfflineReturnCoordinator? offline;
    private TaskbarService? taskbar;
    private TaskbarProbe? probe;
    private TaskbarWatcher? watcher;
    private HotkeyService? hotkeys;
    private SessionPowerCoordinator? power;
    private SessionPowerWatcher? powerWatcher;
    private FullscreenWatcher? fullscreen;
    private long? widgetAt;
    private long? reenumerateAt;
    private float barHeightDip = SceneGeometry.DefaultBarHeight;
    private MenuSnapshot menu = MenuSnapshot.Initial;

    public GameComposition(GameRuntime runtime, CompositeLoopHost loop, SceneFrameSink sink, SessionPersistence persistence,
        AppSettings settings, Action<string> log, Action<Exception> report, Action<UiRequest> ui)
    {
        this.runtime = runtime;
        this.loop = loop;
        this.sink = sink;
        this.persistence = persistence;
        this.settings = settings;
        this.log = log;
        this.report = report;
        this.ui = ui;
        runtime.Effects = this;
        loop.Intercept = Intercept;
        loop.AddBefore(runtime); // ReleaseCaches 60 s após o Hide
        loop.AddBefore(new LoopParticipant(() => taskbar?.NextDeadline, _ => OnTaskbarWake()));
        loop.AddBefore(new LoopParticipant(() => fullscreen?.NextDeadline, _ => fullscreen?.OnWake()));
        loop.AddBefore(new LoopParticipant(() => reenumerateAt, OnReenumerate));
        loop.AddAfter(new LoopParticipant(() => widgetAt, OnWidgetWake));
    }

    /// <summary>Estado do menu da bandeja (lido na T-UI; publicado pela T-Game).</summary>
    public MenuSnapshot Menu => Volatile.Read(ref menu);

    /// <summary>Barra usada pela cena (altura em DIP); a cena padrão (Band) usa 48.</summary>
    public float BarHeightDip => barHeightDip;

    public StripEdgeMode? Edge => runtime.Presence.State switch
    {
        PresenceState.Presente => StripEdgeMode.Present,
        PresenceState.Ausente => StripEdgeMode.Away,
        _ => null,
    };

    public AnimationPolicy Animation => new(settings.Accessibility.ReducedMotion, runtime.App.PowerProfile);

    /// <summary>Monta os serviços da T-Game (chamar via <c>GameThread.Post</c> depois de Ready). Termina com o Show inicial.</summary>
    public void Setup(GameThread owner)
    {
        game = owner;
        var res = loop.Resources;

        // 1. Superfície e renderers da faixa (SDD-08/09).
        surface = new LayeredSurface(game.Scene.Handle, log: log);
        res.Started("scene-surface", () => surface.Dispose());
        typography = new Typography();
        icons = new IconCatalog();
        atlas = SpriteAtlas.LoadEmbedded();
        sceneRenderer = new SceneRenderer(atlas, typography);
        res.Started("scene-renderer", () =>
        {
            sceneRenderer.Dispose();
            atlas.Dispose();
            icons.Dispose();
            typography.Dispose();
        });
        sink.Attach(surface, sceneRenderer);
        res.Started("frame-sink", sink.Dispose);

        // 2. Widget (SDD-23) com o menu nativo (T5.8) e a presença (SDD-25).
        var widgetWindow = game.Scene.GetOrCreateWidget();
        widgetRenderer = new WidgetRenderer(typography, icons);
        widgetPainter = new WidgetFramePainter(widgetRenderer);
        widget = new WidgetController(widgetWindow, widgetPainter, runtime, OnPanelCommand, new NativeWidgetMenu(),
            WidgetMenuState, OnWidgetMenu);
        res.Started("widget", () =>
        {
            widget.Dispose();
            widgetPainter.Dispose();
            widgetRenderer.Dispose();
        });

        // 2b. Card "Bem-vindo de volta" (SDD-24/25, T4.10b): controlador + janelas sob demanda + coordenador no laço.
        card = new OfflineCardController(clock);
        persistence.RestorePending(card, runtime.State); // RF-25.11: pending/cardState do save
        cardPresenter = new OfflineCardPresenter(game, typography, icons, card, () => runtime.Host.AutoScaleEnabled,
            () => ScheduleWidget(now: true));
        offline = new OfflineReturnCoordinator(runtime, card, cardPresenter, clock, log: log);
        cardPresenter.Attach(offline);
        widget.Interaction.OnBadgeClick = offline.BadgeClicked; // badge "+X" → ReopenFromBadge
        if (persistence.RestoredSession is { } restored) // RF-25.6: o snapshot offline é o do save
            offline.SetBaseline(restored.SavedUtc, restored.AutoScaleOn, restored.Presence.AutoScaleAccumSeconds);
        loop.AddBefore(offline); // prazo de 60 s do card, tick e 45 s da pergunta (oculto = sem deadline)
        res.Started("offline-card", cardPresenter.Dispose);

        // 3. Dock (SDD-05): monitor salvo, geometria da cena e do widget.
        var dock = game.Dock;
        dock.SceneSurface = surface;
        dock.LayoutApplied += OnLayoutApplied;
        res.Started("dock", () => dock.LayoutApplied -= OnLayoutApplied);
        if (settings.Dock.MonitorDevice is { Length: > 0 } device && settings.Dock.BoundsHint is [var l, var t, var r, var b])
            dock.SetMonitorHint(new DockMonitorHint(device, new PixelRect(l, t, r, b)));
        var first = dock.Relayout();

        // 4. Barra de tarefas (SDD-21): UIA na T-Shell, resultado → dock + LayoutChanged entre ticks.
        probe = new TaskbarProbe();
        taskbar = new TaskbarService(new TaskbarLocator(), probe, clock, first.Monitor, WakeGame) { UseUia = settings.Taskbar.UseUia };
        taskbar.LayoutChanged += OnTaskbarLayout;
        game.Scene.TaskbarCreated += OnTaskbarCreated;
        res.Started("taskbar", () =>
        {
            game.Scene.TaskbarCreated -= OnTaskbarCreated;
            watcher?.Dispose(); // UnhookWinEvent
            probe.Dispose();
        });

        // 5. Modo Passivo/Interativo e peek (SDD-04).
        game.Mode.Surface = surface;
        game.Mode.TargetClicked += OnTargetClicked;
        game.Mode.ModeChanged += OnModeChanged;
        game.Mode.SetPeekEnabled(settings.Behavior.PeekOnHover);
        game.Mode.SetPowerProfile(runtime.App.PowerProfile); // T7.6 D8: peek só no perfil Normal
        game.Mode.Reapply();
        res.Started("mode", () =>
        {
            game.Mode.TargetClicked -= OnTargetClicked;
            game.Mode.ModeChanged -= OnModeChanged;
        });

        // 6. Hotkeys (SDD-15 RF-15.6): conflito não impede o resto.
        hotkeys = new HotkeyService(game, game.Scene, game, log, new Dictionary<HotkeyAction, string>
        {
            [HotkeyAction.ToggleInteractive] = settings.Hotkeys.ToggleInteractive,
            [HotkeyAction.ToggleVisibility] = settings.Hotkeys.ToggleVisibility,
            [HotkeyAction.TogglePause] = settings.Hotkeys.TogglePause,
        });
        hotkeys.Conflict += (_, message) => log(message);
        hotkeys.Register();
        res.Started("hotkeys", hotkeys.Dispose); // UnregisterHotKey

        // 7. Sessão e energia (SDD-07 RF-07.1/07.5/07.7).
        power = new SessionPowerCoordinator(runtime.App);
        ApplyPower(power.SetSaverOnBattery(settings.Performance.SaverOnBattery));
        ApplyPower(power.SetForceSaver(settings.Performance.ForceSaver));
        powerWatcher = new SessionPowerWatcher(game, game.Scene, power, ApplyPower, log);
        powerWatcher.Start();
        res.Started("session-power", powerWatcher.Dispose); // WTSUnRegister + UnregisterPowerSettingNotification

        // 8. Fullscreen (SDD-06).
        fullscreen = new FullscreenWatcher(clock, ToFullscreenMode(settings.Behavior.HideOnFullscreen), first.Monitor.DeviceName, report);
        fullscreen.Transition += OnFullscreen;
        res.Started("fullscreen", fullscreen.Dispose);

        // 9. Primeiro frame pronto → remove Starting (RestoreContext + Show, RF-07.6).
        runtime.CompleteStartup();
        PublishMenu();
    }

    /// <summary>
    /// Os recursos são possuídos por <see cref="CompositeLoopHost.Resources"/> e encerrados no <c>Shutdown</c> da T-Game;
    /// aqui só se repete essa parada (idempotente). Chamar na T-Game.
    /// </summary>
    public void Dispose()
    {
        foreach (var failure in loop.Resources.StopAll()) report(failure);
    }

    /// <summary>Encerramento passo 2 (RF-07.10): pausa e grava sincronamente. Chamar na T-Game.</summary>
    public bool SaveForShutdown()
    {
        runtime.Dispatch(runtime.App.SetPause(Runtime.Timing.PauseReason.User));
        var (session, profile) = persistence.Encode(runtime, card);
        return persistence.Write(session, profile);
    }

    /// <summary>Novas configurações salvas pela janela WPF (T-UI → T-Game).</summary>
    public void ApplySettings(AppSettings value)
    {
        settings = value;
        runtime.ApplySettings(value.Performance.PresentFps, value.Performance.AbsentFps, value.Accessibility.ReducedMotion, value.AutoScale);
        game.Mode.SetPeekEnabled(value.Behavior.PeekOnHover);
        if (taskbar is not null) taskbar.UseUia = value.Taskbar.UseUia;
        if (power is not null)
        {
            ApplyPower(power.SetSaverOnBattery(value.Performance.SaverOnBattery));
            ApplyPower(power.SetForceSaver(value.Performance.ForceSaver));
        }
        if (game.Dock.Current is { } current) fullscreen?.Reconfigure(ToFullscreenMode(value.Behavior.HideOnFullscreen), current.Monitor.DeviceName);
        ObserveLayout();
    }

    // ---------------------------------------------------------------- efeitos da AppStateMachine (RF-07.3/07.6)

    public void Show(HiddenReason cause)
    {
        // RF-07.6 passo 4: um frame completo antes do ShowWindow (sem conteúdo velho).
        sink.Present(runtime.State, runtime.Host.PresentationSeconds);
        game.Scene.Widget?.Show();
        game.Scene.Show();
        taskbar?.SetVisible(true);
        InstallWatcher();
        ScheduleWidget(now: true);
        PublishMenu();
    }

    public void Hide(HiddenReason cause)
    {
        offline?.OnHide(); // RF-25.6: snapshot offline congelado no Hide
        watcher?.Uninstall();
        taskbar?.SetVisible(false);
        game.Scene.Hide(); // Passivo, peek e z-order cancelados pela cena
        widgetAt = null;
        PublishMenu();
    }

    public void Save(SaveTrigger trigger)
    {
        var (session, profile) = persistence.Encode(runtime, card);
        if (trigger == SaveTrigger.Suspended)
        {
            persistence.Write(session, profile); // RF-07.7: síncrono na T-Game
            return;
        }
        _ = Task.Run(() => persistence.Write(session, profile)); // T-IO
    }

    public void ReleaseCaches()
    {
        surface?.Release();
        sink.Invalidate();
        widgetPainter?.Invalidate();
        GC.Collect(2, GCCollectionMode.Optimized, blocking: false, compacting: true);
    }

    public void RestoreContext()
    {
        // RF-07.6 passos 1–2: reenumera e recomputa; a superfície é recriada no próximo BeginFrame.
        game.Dock.Relayout();
        // Passo 3: OfflineCalculator.Compute → OfflineResultApplier.Apply (entre ticks) → card "Bem-vindo de volta" (T4.10b).
        offline?.OnRestoreContext();
    }

    public void SetEcoQos(bool enabled) => log($"EcoQoS {(enabled ? "on" : "off")} requested (EcoQosController not implemented)");

    // ---------------------------------------------------------------- comandos do App (interceptados antes da simulação)

    private bool Intercept(IGameCommand command)
    {
        switch (command)
        {
            case HotkeyCommand hotkey:
                runtime.NotifyInteraction();
                Execute(hotkey.Action switch
                {
                    HotkeyAction.ToggleInteractive => AppCommandKind.ToggleInteractive,
                    HotkeyAction.ToggleVisibility => AppCommandKind.ToggleVisibility,
                    _ => AppCommandKind.TogglePause,
                });
                return true;
            case AppCommand app:
                Execute(app.Kind, app.MonitorIndex);
                return true;
            default:
                return false;
        }
    }

    private void Execute(AppCommandKind kind, int monitorIndex = -1)
    {
        switch (kind)
        {
            case AppCommandKind.ToggleInteractive: game.Mode.ToggleInteractive(); break;
            case AppCommandKind.ToggleVisibility: runtime.ToggleVisibilityByUser(); break;
            case AppCommandKind.TogglePause: runtime.TogglePause(); break;
            case AppCommandKind.SelectMonitor: SelectMonitor(monitorIndex); break;
            case AppCommandKind.ShowAndInteract:
                runtime.ShowByUser();
                if (runtime.App.IsVisible) game.Mode.SetMode(OverlayMode.Interactive);
                break;
        }
        PublishMenu();
    }

    private void SelectMonitor(int index)
    {
        var monitors = MonitorService.Enumerate();
        if (index < 0 || index >= monitors.Count) return;
        game.Dock.MoveToMonitor(monitors[index]);
    }

    private void OnWidgetMenu(WidgetMenuResult result)
    {
        switch (result.Command)
        {
            case WidgetMenuCommand.ToggleInteractive: Execute(AppCommandKind.ToggleInteractive); break;
            case WidgetMenuCommand.TogglePause: Execute(AppCommandKind.TogglePause); break;
            case WidgetMenuCommand.HideOverlay: Execute(AppCommandKind.ToggleVisibility); break;
            case WidgetMenuCommand.SelectMonitor: Execute(AppCommandKind.SelectMonitor, result.MonitorIndex); break;
            case WidgetMenuCommand.Settings: ui(UiRequest.OpenSettings); break;
            case WidgetMenuCommand.About: ui(UiRequest.OpenAbout); break;
            case WidgetMenuCommand.Exit: ui(UiRequest.Exit); break;
        }
    }

    private WidgetMenuState WidgetMenuState()
    {
        var m = Menu;
        return new WidgetMenuState(m.Interactive, m.Paused, m.Monitors, m.SelectedMonitor);
    }

    // Painel e cards (SDD-24) ficam fora desta ficha: o widget registra a interação e o pedido é só anotado.
    private void OnPanelCommand(PanelCommand command) => log($"panel command {command.Kind}/{command.Tab} (panel not wired: T4.10 REPORT)");

    private void OnTargetClicked(HitTarget target) => runtime.NotifyInteraction();

    private void OnModeChanged(OverlayMode mode) => PublishMenu();

    // ---------------------------------------------------------------- barra, dock, fullscreen, energia

    private void OnTaskbarWake()
    {
        taskbar?.OnWake();
    }

    private void OnTaskbarLayout(TaskbarLayout layout)
    {
        barHeightDip = layout.HeightDip > 0 ? layout.HeightDip : SceneGeometry.DefaultBarHeight;
        game.Dock.SetTaskbar(layout);
        ObserveLayout();
    }

    private void OnTaskbarCreated()
    {
        // Explorer reiniciou (T7.6 D9): o hook da barra antiga morreu; o watcher reinstala com o PID novo e sinaliza
        // (não faz nada se a cena estiver oculta); a reconsulta da barra vem pelo sinal.
        watcher?.OnTaskbarCreated(LocateTaskbar);
        game.ZOrder.Request(shellForeground: true);
    }

    private nint LocateTaskbar() =>
        game.Dock.Current is { } current && new TaskbarLocator().Locate(current.Monitor) is { } target ? target.Hwnd : 0;

    private void OnLayoutApplied(DockLayoutResult result)
    {
        int dpi = (int)result.Monitor.Dpi;
        float scale = dpi / 96f;
        if (surface is not null) surface.Scale = scale;
        sceneRenderer?.SetDpi(dpi);
        widget?.SetDpi(dpi);
        cardPresenter?.Relayout();
        sink.Invalidate();
        taskbar?.SetMonitor(result.Monitor);
        fullscreen?.Reconfigure(ToFullscreenMode(settings.Behavior.HideOnFullscreen), result.Monitor.DeviceName);
        ObserveLayout();
        runtime.Host.MarkDirty();
        PublishMenu();
    }

    /// <summary>Monitor + barra atuais → <see cref="SceneLayoutWatcher"/> (LayoutChanged só se a faixa mudou).</summary>
    private void ObserveLayout()
    {
        if (game.Dock.Current is not { } current) return;
        var bar = taskbar?.Current is { } t && (string.IsNullOrEmpty(t.MonitorDevice) || t.MonitorDevice == current.Monitor.DeviceName)
            ? t : default;
        // TA.5-Windows (cena sobre a barra) depende do spike TA.1: o dock segue com o hospedeiro Band e a
        // simulação recebe o layout do mesmo hospedeiro enquanto towersInBar for falso (padrão, DP-33.4).
        var options = new BuildZoneOptions { UseUia = settings.Taskbar.UseUia, TowersInBar = settings.Taskbar.TowersInBar };
        runtime.ObserveLayout(current.Monitor, bar, options);
    }

    private void OnFullscreen(FullscreenTransition transition)
    {
        if (transition == FullscreenTransition.Entered) runtime.EnterHidden(HiddenReason.Fullscreen);
        else runtime.LeaveHidden(HiddenReason.Fullscreen);
        PublishMenu();
    }

    private void ApplyPower(SessionPowerResult result)
    {
        if (result.Effects.Count > 0) runtime.Dispatch(result.Effects);
        game.Mode.SetPowerProfile(runtime.App.PowerProfile); // T7.6 D8
        if (result.ScheduleReenumeration)
            reenumerateAt = clock.NowTicks + (long)(ReenumerateDelaySeconds * clock.TicksPerSecond);
        PublishMenu();
    }

    private void OnReenumerate(IClock now)
    {
        if (reenumerateAt is not { } due || now.NowTicks < due) return;
        reenumerateAt = null;
        game.Dock.Invalidate();
        taskbar?.Signal();
    }

    private void InstallWatcher()
    {
        nint hwnd = LocateTaskbar();
        if (hwnd == 0) return;
        watcher ??= new TaskbarWatcher(() => PostSafe(() => taskbar?.Signal()));
        if (!watcher.Install(hwnd)) log("TaskbarWatcher: could not install the WinEvent hook.");
    }

    // Conclusões da T-Shell e WinEvents chegam fora da T-Game: um no-op acorda o laço.
    private void WakeGame() => PostSafe(static () => { });

    private void PostSafe(Action action)
    {
        try { game.Post(action); }
        catch (InvalidOperationException) { } // encerrando: nada a acordar
    }

    // ---------------------------------------------------------------- widget (SDD-23)

    private void ScheduleWidget(bool now)
    {
        if (!runtime.App.IsVisible) { widgetAt = null; return; }
        widgetAt = now ? clock.NowTicks : clock.NowTicks + (long)(WidgetIntervalSeconds * clock.TicksPerSecond);
    }

    private void OnWidgetWake(IClock now)
    {
        if (widgetAt is not { } due || now.NowTicks < due || widget is null) return;
        var state = runtime.State;
        int stage = Math.Clamp(state.Stage, 1, runtime.Content.Stages.Length);
        var inputs = new WidgetInputs(state.Credits, runtime.Host.Rate.DisplayedRate, state.Wave,
            runtime.Content.Stages[stage - 1].Waves, runtime.Presence.State, state.PresenceMultiplier,
            BonusSecondsLeft: 0, Pending: card?.BadgeCredits ?? 0, MissionBadge: 0, ActiveTab: null, HoverIndex: -1);
        var model = widgetModel.Update(inputs, TimeSpan.FromSeconds(now.NowTicks / now.TicksPerSecond));
        widget.Interaction.BadgeVisible = model.PendingText != null; // SDD-23: badge "+X" clicável só quando desenhado
        widget.SetModel(model);
        // Coalescência de 500 ms só enquanto a simulação anda; pausado, o texto não muda sozinho (0 wakes, CA-23).
        if (runtime.App.SimulationRunning) ScheduleWidget(now: false);
        else widgetAt = null;
    }

    private void PublishMenu()
    {
        var monitors = MonitorService.Enumerate();
        var labels = monitors.Select((m, i) => $"{i + 1}: {m.DeviceName}{(m.IsPrimary ? " (principal)" : "")}").ToArray();
        int selected = game.Dock.Current is { } current
            ? Math.Max(0, monitors.ToList().FindIndex(m => m.DeviceName == current.Monitor.DeviceName))
            : 0;
        var app = runtime.App;
        Volatile.Write(ref menu, new MenuSnapshot(game.Mode.Mode == OverlayMode.Interactive,
            (app.PauseReasons & Runtime.Timing.PauseReason.User) != 0, !app.IsVisible, labels, selected));
        if (!app.SimulationRunning && app.IsVisible) ScheduleWidget(now: true); // pausa/retomada atualiza o widget uma vez
        else if (app.IsVisible && widgetAt is null) ScheduleWidget(now: true);
    }

    private static HideOnFullscreen ToFullscreenMode(HideOnFullscreenMode mode) => mode switch
    {
        HideOnFullscreenMode.AnyMonitor => HideOnFullscreen.AnyMonitor,
        HideOnFullscreenMode.Never => HideOnFullscreen.Never,
        _ => HideOnFullscreen.SameMonitor,
    };
}
