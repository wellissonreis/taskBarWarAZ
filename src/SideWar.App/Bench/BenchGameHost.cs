using SideWar.Core;
using SideWar.Platform.Windows.Rendering;
using SideWar.Platform.Windows.Taskbar;
using SideWar.Platform.Windows.Threading;
using SideWar.Platform.Windows.Windows;
using SideWar.Rendering.Frame;
using SideWar.Rendering.Scene;
using SideWar.Rendering.Sprites;
using SideWar.Rendering.Text;
using SideWar.Runtime;
using SideWar.Runtime.Composition;
using SideWar.Runtime.Diagnostics;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Rendering;
using SideWar.Runtime.Taskbar;
using SkiaSharp;

namespace SideWar.App.Bench;

/// <summary>
/// T-Game host of the SanityBot game scenarios (`presente-onda-media/pesada`, `ausente`, `varias-janelas`; SDD-16 RF-16.2,
/// T4.11). The logical graph is <see cref="BenchScenarioRig"/> (real <c>GameRuntime</c>/<c>GameHost</c> with the presence
/// <c>RenderPolicy</c>, driven by the SanityBot); here only the Windows side is assembled: strip surface and renderer,
/// widget, dock, taskbar (strip over the bar, <c>towersInBar = false</c>, Band host) and the extra windows of
/// <see cref="BenchWindows"/> (panel, banner, supply box, seal). No tray, hotkeys, session, power or fullscreen watcher:
/// it never touches the user's data. Hotspot/panel windows carry placeholder paint (their real renderers need the
/// Arsenal/Campaign/Mission models that are not composed in the App yet). Every member runs on T-Game.
/// </summary>
internal sealed class BenchGameHost : IBenchHost
{
    private const double WidgetIntervalSeconds = 0.5;
    private readonly BenchScenario scenario;
    private readonly BenchStats stats;
    private readonly Action<string> log;
    private readonly StopwatchClock clock = new();
    private readonly SkiaFrameCanvas widgetCanvas = new();
    private readonly BenchWidgetRenderer widgetRenderer = new();
    private readonly SkiaFrameCanvas extraCanvas = new();
    private readonly SKPaint placeholder = new() { Color = new(0x4F, 0xC3, 0xF7, 0xB0) };
    private GameThread game = null!;
    private BenchScenarioRig? rig;
    private LayeredSurface? surface;
    private Typography? typography;
    private SpriteAtlas? atlas;
    private SceneRenderer? renderer;
    private BenchFrameSink? sink;
    private WidgetWindow? widget;
    private PanelWindow? panel;
    private HotspotWindow? banner, supplyBox;
    private SceneSealWindow? seal;
    private TaskbarService? taskbar;
    private TaskbarProbe? probe;
    private TaskbarWatcher? watcher;
    private long? widgetAt;
    private float barHeightDip = SceneGeometry.DefaultBarHeight;
    private long queries, updates;
    private bool shutdown;

    public BenchGameHost(BenchScenario scenario, BenchStats stats, Action<string> log)
    {
        this.scenario = scenario;
        this.stats = stats;
        this.log = log;
    }

    public long TaskbarQueries => Interlocked.Read(ref queries);
    public long TaskbarUpdates => Interlocked.Read(ref updates);

    public void Bind(GameThread owner) => game = owner;

    public long? NextDeadline => rig?.Loop.NextDeadline;

    public void Setup()
    {
        var scene = game.Scene;
        var dock = game.Dock;
        surface = new LayeredSurface(scene.Handle, log: log);
        dock.SceneSurface = surface;
        typography = new Typography();
        atlas = SpriteAtlas.LoadEmbedded();
        renderer = new SceneRenderer(atlas, typography);
        sink = new BenchFrameSink(surface, renderer, stats, clock, () => rig!.Runtime.Presence.State, () => barHeightDip);
        rig = BenchScenarioRig.Create(scenario, clock, sink, e => { log(e.ToString()); });
        rig.Loop.AddBefore(new LoopParticipant(() => taskbar?.NextDeadline, _ => OnTaskbarWake()));
        rig.Loop.AddAfter(new LoopParticipant(() => widgetAt, OnWidgetWake));

        widget = scene.GetOrCreateWidget();
        dock.LayoutApplied += OnLayoutApplied;
        var monitor = MonitorResolver.Resolve(MonitorService.Enumerate(), null) ?? throw new InvalidOperationException("No monitor available.");
        probe = new TaskbarProbe();
        taskbar = new TaskbarService(new TaskbarLocator(), new TimingProbe(probe, stats), clock, monitor, wake: WakeGame);
        taskbar.LayoutChanged += OnTaskbarLayout;
        var first = dock.Relayout(); // taskbar unknown yet: fallback geometry until the first UIA result
        OnLayoutApplied(first);

        // Passive mode: the strip is click-through, so the bench never captures the user's mouse on the scene.
        game.Mode.Surface = surface;
        game.Mode.Reapply();

        CreateExtraWindows(scene);
        ShowAll();
        rig.Start(); // RestoreContext/Show are done above (no effect handler); presence is forced by the driver
    }

    public void OnWake(IClock wakeClock)
    {
        if (shutdown || rig is null) return;
        long start = wakeClock.NowTicks;
        rig.Loop.OnWake(wakeClock);
        stats.RecordTick((wakeClock.NowTicks - start) * 1000.0 / wakeClock.TicksPerSecond);
        SideWarMetrics.RecordWake();
    }

    public void OnCommand(IGameCommand command) => rig?.Loop.OnCommand(command);

    public void Shutdown() => Dispose();

    public void Dispose()
    {
        if (shutdown) return;
        shutdown = true;
        try { rig?.Loop.Shutdown(); } catch (Exception e) { log(e.Message); }
        try { watcher?.Dispose(); } catch (Exception e) { log(e.Message); }
        try { probe?.Dispose(); } catch (Exception e) { log(e.Message); }
        game.Dock.LayoutApplied -= OnLayoutApplied;
        sink?.Dispose();
        extraCanvas.Dispose();
        widgetCanvas.Dispose();
        widgetRenderer.Dispose();
        placeholder.Dispose();
        renderer?.Dispose();
        atlas?.Dispose();
        typography?.Dispose();
        surface?.Dispose(); // the widget and the extra windows are children of the scene window: destroyed with it
    }

    private void CreateExtraWindows(SceneWindow scene)
    {
        var w = scenario.Windows;
        if (w.HasFlag(BenchWindows.Panel))
        {
            panel = scene.GetOrCreatePanel();
            panel.Renderer = (frame, _) => { PaintPlaceholder(frame); };
        }
        if (w.HasFlag(BenchWindows.Banner)) banner = scene.GetOrCreateBanner();
        if (w.HasFlag(BenchWindows.SupplyBox)) supplyBox = scene.GetOrCreateSupplyBox();
        if (w.HasFlag(BenchWindows.Seal)) seal = scene.GetOrCreateSeal();
    }

    private void PaintPlaceholder(OverlayFrame frame)
    {
        var c = extraCanvas.Begin(frame);
        c.Clear(SKColors.Transparent);
        c.DrawRect(0, 0, frame.Width, frame.Height, placeholder);
        extraCanvas.End();
    }

    private void PaintChild(OverlayChildWindow child)
    {
        var s = child.Surface;
        if (s.Size.IsEmpty) return;
        PaintPlaceholder(s.BeginFrame());
        s.EndFrame(null);
    }

    /// <summary>Places the extra windows from the scene rectangle (SDD-22 RF-22.7/22.8, SDD-24 RF-24.2).</summary>
    private void LayoutExtraWindows(DockLayoutResult result)
    {
        uint dpi = result.Monitor.Dpi;
        var sceneRect = result.Scene;
        if (panel is not null) panel.Apply(PanelGeometry.Compute(OverlayKind.Panel, result.Monitor, sceneRect));
        if (banner is not null) banner.Apply(HotspotGeometry.Banner(sceneRect, dpi, HotspotGeometry.Px(220, dpi)));
        if (supplyBox is not null) supplyBox.Apply(HotspotGeometry.SupplyBox(sceneRect, dpi, 400f));
        seal?.Apply(HotspotGeometry.Seal(sceneRect, dpi, HotspotGeometry.Px(120, dpi), HotspotGeometry.Px(20, dpi)));
        if (banner is { IsVisible: true }) PaintChild(banner);
        if (supplyBox is { IsVisible: true }) PaintChild(supplyBox);
        if (seal is { IsVisible: true }) PaintChild(seal);
    }

    private void ShowAll()
    {
        sink!.Present(rig!.Runtime.State, rig.Runtime.Host.PresentationSeconds); // full frame before ShowWindow (RF-07.6)
        widget!.Show();
        game.Scene.Show();
        panel?.Show();
        if (banner is not null) { banner.Show(1); PaintChild(banner); }
        if (supplyBox is not null) { supplyBox.Show(1); PaintChild(supplyBox); }
        if (seal is not null) { seal.Show(); PaintChild(seal); }
        taskbar!.SetVisible(true);
        InstallWatcher();
        widgetAt = clock.NowTicks;
    }

    private void OnLayoutApplied(DockLayoutResult result)
    {
        float scale = result.Monitor.Dpi / 96f;
        if (surface is not null) surface.Scale = scale;
        if (widget is not null) widget.Surface.Scale = scale;
        renderer?.SetDpi((int)result.Monitor.Dpi);
        sink?.Invalidate();
        widgetCanvas.Invalidate();
        taskbar?.SetMonitor(result.Monitor);
        LayoutExtraWindows(result);
        ObserveLayout();
        rig?.Runtime.Host.MarkDirty();
        widgetAt = clock.NowTicks;
    }

    private void OnTaskbarLayout(TaskbarLayout layout)
    {
        barHeightDip = layout.HeightDip > 0 ? layout.HeightDip : SceneGeometry.DefaultBarHeight;
        game.Dock.SetTaskbar(layout);
        ObserveLayout();
    }

    /// <summary>Strip over the bar with towersInBar = false (Band host, PLANO §8.2): LayoutChanged only if the strip changed.</summary>
    private void ObserveLayout()
    {
        if (rig is null || game.Dock.Current is not { } current) return;
        var bar = taskbar?.Current is { } t && (string.IsNullOrEmpty(t.MonitorDevice) || t.MonitorDevice == current.Monitor.DeviceName) ? t : default;
        rig.Runtime.ObserveLayout(current.Monitor, bar, new BuildZoneOptions { UseUia = true, TowersInBar = scenario.TowersInBar });
    }

    private void OnTaskbarWake()
    {
        if (taskbar is null) return;
        taskbar.OnWake();
        Interlocked.Exchange(ref queries, taskbar.QueryCount);
        Interlocked.Exchange(ref updates, taskbar.UpdateCount);
    }

    // The widget text refreshes at most 2x/s (SDD-23), independent of the strip frames.
    private void OnWidgetWake(IClock now)
    {
        if (widgetAt is not { } due || now.NowTicks < due) return;
        widgetAt = now.NowTicks + (long)(WidgetIntervalSeconds * now.TicksPerSecond);
        if (widget is null || !widget.IsVisible || widget.Surface.Size.IsEmpty) return;
        var w = widget.Surface;
        var frame = w.BeginFrame();
        widgetRenderer.Draw(widgetCanvas.Begin(frame), frame.Width, frame.Height, w.Scale, rig!.Runtime.Presence.State.ToString(), scenario.Name,
            rig.Runtime.State.Tick);
        widgetCanvas.End();
        w.EndFrame(null);
    }

    private void InstallWatcher()
    {
        var monitor = game.Dock.Current?.Monitor;
        var target = monitor is { } m ? new TaskbarLocator().Locate(m) : null;
        if (target is not { } t) return;
        watcher ??= new TaskbarWatcher(() => PostSafe(() => taskbar?.Signal()));
        if (!watcher.Install(t.Hwnd)) log("TaskbarWatcher: could not install the WinEvent hook.");
    }

    private void WakeGame() => PostSafe(static () => { });

    private void PostSafe(Action action)
    {
        try { game.Post(action); }
        catch (InvalidOperationException) { }
    }
}
