using System.Diagnostics;
using SideWar.Core;
using SideWar.Core.Missions;
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
using SideWar.Rendering.Panel;
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
using SideWar.Runtime.Td;
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
    private const ulong MissionSeed = 0x5EED_5A1D_E7A2UL; // sorteio diário = f(seed, data), independente do Rng da partida (SDD-27 RF-27.11)

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
    private MissionService? missions;
    private LiveEventsCoordinator? live;
    private LiveEventsPresenter? livePresenter;
    private PanelCoordinator? panel;
    private PanelPresenter? panelPresenter;
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
    private readonly TdComposition? td;
    private TdBandRenderer? tdRenderer;
    private TdFrameSink? tdSink;
    private TdRenderDriver? tdDriver;
    private TdUiPresenter? tdUi;
    private TdFocusState? tdFocus;
    private int tdBandHeight = TdBand.DefaultHeightDip; // TD.14: lido das configurações ao iniciar (mudar vale ao reiniciar o SideWar)
    private SceneLayoutWatcher? tdWatcher;
    private int crateSeen;

    public GameComposition(GameRuntime runtime, CompositeLoopHost loop, SceneFrameSink sink, SessionPersistence persistence,
        AppSettings settings, Action<string> log, Action<Exception> report, Action<UiRequest> ui,
        string? tdDataDirectory = null, string version = "0.1.0")
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
        if (tdDataDirectory is not null)
        {
            // gameMode = td (experimental, TD.6): o host TD usa a mesma AppStateMachine (ocultar/fullscreen/sessão pausam) e o idle
            // fica sem partida (NoRun) até o render TD (TD.7) trocar o jogo padrão.
            runtime.Host.RunState = Runtime.Hosting.RunState.NoRun;
            tdBandHeight = Math.Clamp(settings.Td.BandHeightDip, TdBand.MinHeightDip, TdBand.MaxHeightDip);
            tdFocus = new TdFocusState();
            tdFocus.Configure(settings.Td.FadeWhenUnfocused, settings.Td.FadeOpacityPercent);
            td = TdComposition.Create(tdDataDirectory, version, runtime.App, clock, settings, log, presence: tdFocus);
            loop.AddBefore(td.Host);
        }
        loop.AddBefore(runtime); // ReleaseCaches 60 s após o Hide
        loop.AddBefore(new LoopParticipant(() => taskbar?.NextDeadline, _ => OnTaskbarWake()));
        loop.AddBefore(new LoopParticipant(() => fullscreen?.NextDeadline, _ => fullscreen?.OnWake()));
        loop.AddBefore(new LoopParticipant(() => reenumerateAt, OnReenumerate));
        loop.AddAfter(new LoopParticipant(() => widgetAt, OnWidgetWake));
    }

    /// <summary>Estado do menu da bandeja (lido na T-UI; publicado pela T-Game).</summary>
    public MenuSnapshot Menu => Volatile.Read(ref menu);

    /// <summary>HUD TD recolhido (lido pela T-UI ao gravar as configurações).</summary>
    public bool HudMinimized => tdUi?.HudMinimized ?? settings.Td.HudMinimized;

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
        if (td is null)
        {
            sink.Attach(surface, sceneRenderer);
            res.Started("frame-sink", sink.Dispose);
        }
        else
        {
            // TD.7: a cena é a faixa de 32 DIP desenhada pelo TdBandRenderer (render por evento: 0 fps em Preparo/pausa, 30 fps em Onda).
            // O sink idle fica sem superfície (não desenha) e o idle está em NoRun.
            tdRenderer = new TdBandRenderer(typography, atlas);
            tdRenderer.SetBandHeight(tdBandHeight);
            tdSink = new TdFrameSink(surface, tdRenderer);
            tdDriver = new TdRenderDriver(td.Host, runtime.App, clock, tdSink);
            tdWatcher = new SceneLayoutWatcher(td.Host, null, layout => new SideWar.Core.Td.LayoutChanged(layout));
            loop.AddAfter(tdDriver); // depois do TdGameHost: um frame por tick novo, ou por evento/comando
            loop.AddAfter(new LoopParticipant(() => null, _ => SyncCrateMode()));
            res.Started("td-frame-sink", () =>
            {
                tdSink.Dispose();
                tdRenderer.Dispose();
                sink.Dispose();
            });
        }

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

        // TD: sem card offline, missões, eventos ao vivo e painel idle (o jogo é a partida TD).
        if (td is null)
        {
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

            // 2c. Missões, eventos ao vivo (banner, caixa, card do chefe) e painel das 4 abas (T6.11b).
            missions = new MissionService(runtime.Content, MissionSeed);
            persistence.BindMissions(missions); // profile.missions → dia salvo
            livePresenter = new LiveEventsPresenter(game, runtime, typography, icons, sceneRenderer, () => Animation,
                () => offline.Question is not null, () => ScheduleWidget(now: true));
            live = new LiveEventsCoordinator(runtime, missions, livePresenter, clock, log: log, missionsChanged: () => ScheduleWidget(now: true));
            livePresenter.Attach(live);
            runtime.Host.AddHook(live); // Consume/AddPresentSeconds por tick
            loop.AddAfter(live); // depois do GameHost: vira o dia, aplica coletas entre ticks, remonta a visão a ~1 Hz
            loop.AddAfter(livePresenter); // anel/piscar a ≤ 15 Hz só visível e com a simulação andando
            res.Started("live-events", livePresenter.Dispose);
            panelPresenter = new PanelPresenter(game, runtime, typography, icons, () => sceneRenderer.SpriteCache);
            panel = new PanelCoordinator(runtime, missions, panelPresenter, new PanelTextMetricsSkia(typography), clock, () => ScheduleWidget(now: true));
            panelPresenter.Attach(panel);
            loop.AddAfter(panel); // 1 Hz de dados, 5 s do MIGRAR, 30 s do modo construção
            res.Started("panel", panelPresenter.Dispose);
            live.Start(); // Sync(hoje): sorteia/restaura o dia e coleta a virada pendente
        }

        // 3. Dock (SDD-05): monitor salvo, geometria da cena e do widget.
        var dock = game.Dock;
        dock.SceneSurface = surface;
        dock.TdBandHeightDip = tdBandHeight;
        dock.TdMode = td is not null; // TD.7: Band de 32 DIP, sem o widget idle
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
        if (td is not null) {
            // TD.14: a janela está em DIP reais; o hit-test e o Core trabalham no espaço de desenho de 32 DIP (÷ k).
            float k = TdBand.Scale(tdBandHeight);
            game.Mode.TdHitTest = (x, y) => TdHitTester.Test(td.Host.Match, x / k, y / k);
        }
        game.Mode.TargetClicked += OnTargetClicked;
        game.Mode.ModeChanged += OnModeChanged;
        game.Mode.SetPeekEnabled(settings.Behavior.PeekOnHover);
        ApplyOpacityLevels(settings.Opacity);
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
            // Só no modo TD (no idle a tecla não é registrada).
            [HotkeyAction.StartWave] = settings.Hotkeys.StartWave,
        }.Where(kv => td is not null || kv.Key != HotkeyAction.StartWave).ToDictionary());
        hotkeys.Conflict += (_, message) => log(message);
        hotkeys.Register();
        res.Started("hotkeys", hotkeys.Dispose); // UnregisterHotKey

        if (td is not null && tdDriver is not null)
        {
            // TD.8: HUD, popover, modo construção (ESC temporário) e Interativo ao passar o mouse sobre a faixa.
            tdUi = new TdUiPresenter(game, runtime, td, tdDriver, typography!, clock, ui, () => hotkeys, tdFocus!, tdBandHeight,
                settings.Td.HudMinimized, PublishMenu, atlas);
            game.Mode.EnableTdHover(1.5);
            tdUi.Setup();
            loop.AddAfter(new LoopParticipant(() => tdUi.NextDeadline, _ => tdUi.OnWake()));
            res.Started("td-ui", () => tdUi.Dispose());
        }

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
        // TD.14: ativar OUTRO app = ausente (esmaece, 8 fps, caixa-bomba sem presença); janela do SideWar ativada = presente.
        if (tdFocus is not null)
            fullscreen.ForegroundChanged += foreign => { if (foreign) tdFocus.OnForeignActivated(); else tdFocus.OnInteraction(); };
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

    /// <summary>Padrão: nunca esmaece. Ausente só reduz a opacidade se o usuário ligou FadeWhenAbsent.</summary>
    private void ApplyOpacityLevels(OpacitySettings o) =>
        game.Mode.SetOpacityLevels((byte)o.Peek, o.FadeWhenAbsent ? (byte)o.Absent : OverlayModeController.PresentAlpha);

    /// <summary>Novas configurações salvas pela janela WPF (T-UI → T-Game).</summary>
    public void ApplySettings(AppSettings value)
    {
        settings = value;
        td?.ApplySettings(value);
        tdFocus?.Configure(value.Td.FadeWhenUnfocused, value.Td.FadeOpacityPercent);
        if (td is not null && Math.Clamp(value.Td.BandHeightDip, TdBand.MinHeightDip, TdBand.MaxHeightDip) != tdBandHeight)
            log($"td.bandHeightDip {value.Td.BandHeightDip} salvo; vale ao reiniciar o SideWar (atual {tdBandHeight}).");
        runtime.ApplySettings(value.Performance.PresentFps, value.Performance.AbsentFps, value.Accessibility.ReducedMotion, value.AutoScale);
        game.Mode.SetPeekEnabled(value.Behavior.PeekOnHover);
        ApplyOpacityLevels(value.Opacity);
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
        if (tdDriver is not null) tdDriver.ForceDraw();
        else sink.Present(runtime.State, runtime.Host.PresentationSeconds);
        if (td is null) game.Scene.Widget?.Show();
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
        tdUi?.OnSceneHidden();
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
        tdSink?.Invalidate();
        tdDriver?.MarkDirty();
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
                    HotkeyAction.StartWave => AppCommandKind.StartWave,
                    HotkeyAction.CancelBuild => AppCommandKind.CancelBuild,
                    _ => AppCommandKind.TogglePause,
                });
                return true;
            case AppCommand app:
                Execute(app.Kind, app.MonitorIndex);
                return true;
            case IGameCommand tdCommand when td is not null && tdCommand.GetType().Namespace == "SideWar.Core.Td":
                td.Host.OnCommand(tdCommand);
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
            case AppCommandKind.StartWave: td?.Host.RequestWave(); break;
            case AppCommandKind.ToggleHud: tdUi?.ToggleHud(); break;
            case AppCommandKind.CancelBuild: tdUi?.Controller.Cancel(); break;
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

    // Painel das 4 abas (SDD-24, T6.11b): o widget alterna a aba; a torre da cena abre o Arsenal na linha dela (ADR-034).
    private void OnPanelCommand(PanelCommand command) => panel?.Execute(command);

    private void OnTargetClicked(HitTarget target)
    {
        runtime.NotifyInteraction();
        if (target.Kind == HitKind.SceneTower) panel?.OpenArsenal(target.TowerId);
        else if (target.Kind == HitKind.TdCrate) td?.Host.RequestCollectCrate(); // TD.7: clique na caixa-bomba (a UI completa é a TD.8)
        else if (target.Kind == HitKind.TdTower) tdDriver?.SetSelected(target.TowerId);
    }

    /// <summary>
    /// A caixa-bomba só recebe clique no modo Interativo (a cena é passante por padrão, RF-28.14): quando uma caixa nova cai, a cena
    /// passa a Interativo (o retorno automático ao Passivo vale como no resto). Decisão da TD.7 até a TD.8 tratar o modo na UI.
    /// </summary>
    private void SyncCrateMode()
    {
        var match = td!.Host.Match;
        int id = match.CrateActive ? match.CrateId : 0;
        if (id == crateSeen) return;
        crateSeen = id;
        if (id != 0 && runtime.App.IsVisible) game.Mode.SetMode(OverlayMode.Interactive);
    }

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
        tdRenderer?.SetDpi(dpi);
        tdSink?.Invalidate();
        tdDriver?.MarkDirty();
        tdUi?.Relayout();
        widget?.SetDpi(dpi);
        cardPresenter?.Relayout();
        livePresenter?.Relayout();
        panelPresenter?.Relayout();
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
        var options = new BuildZoneOptions { UseUia = settings.Taskbar.UseUia, TowersInBar = settings.Taskbar.TowersInBar, TdBandHeightDip = tdBandHeight };
        if (tdWatcher is not null)
        {
            // TD.7 (RF-28.18 após TA.1): faixa de 32 DIP, LayoutChanged ao TdMatchState pelo TdGameHost, ao iniciar e a cada mudança de barra/DPI/monitor.
            if (tdWatcher.Observe(current.Monitor, bar, options with { TdBand = true }) is not null) tdDriver?.MarkDirty();
            return;
        }
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
        if (td is not null) { widgetAt = null; return; } // TD: sem o widget idle (HUD compacto é a TD.8)
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
            BonusSecondsLeft: live?.WidgetChipSeconds ?? 0, Pending: card?.BadgeCredits ?? 0, MissionBadge: missions?.Badge ?? 0,
            ActiveTab: panel?.ActiveTab, HoverIndex: -1);
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
            (app.PauseReasons & Runtime.Timing.PauseReason.User) != 0, !app.IsVisible, labels, selected,
            td is not null, td is null ? null : settings.Hotkeys.StartWave, HudMinimized));
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
