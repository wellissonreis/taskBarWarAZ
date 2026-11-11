using System.Diagnostics;
using SideWar.Core;
using SideWar.Platform.Windows.Input;
using SideWar.Platform.Windows.Rendering;
using SideWar.Platform.Windows.Taskbar;
using SideWar.Platform.Windows.Threading;
using SideWar.Platform.Windows.Windows;
using SideWar.Rendering.Frame;
using SideWar.Runtime;
using SideWar.Runtime.Diagnostics;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Input;
using SideWar.Runtime.Taskbar;

namespace SideWar.App.Bench;

/// <summary>
/// T-Game host of the E1 benchmark scenarios (SDD-16 RF-16.2): `e1-synthetic` (test frame on the strip + widget),
/// `idle-static` (one static strip frame, no timers), `hidden` (everything created but hidden) and `baseline-wpf`
/// (P-00: nothing at all). Every member except the thread-safe counters runs on T-Game.
/// </summary>
internal sealed class BenchHost : IGameLoopHost, IDisposable
{
    private const int WidgetEveryFrames = 15; // ~2 fps for the widget text
    private readonly string scenario;
    private readonly BenchStats stats;
    private readonly bool noInput;
    private readonly Action<string> log;
    private readonly StopwatchClock clock = new();
    private GameThread game = null!;
    private TestSceneHost? sceneHost;
    private TestSceneRenderer? staticRenderer;
    private readonly SkiaFrameCanvas canvas = new(), widgetCanvas = new();
    private readonly BenchWidgetRenderer widgetRenderer = new();
    private LayeredSurface? surface;
    private WidgetWindow? widget;
    private TaskbarService? taskbar;
    private TaskbarProbe? probe;
    private TaskbarWatcher? watcher;
    private HotkeyService? hotkeys;
    private bool visible, drawStatic, drawWidget, shutdown;
    private long frames, lastWidgetFrame;
    private long queries, updates;

    public BenchHost(string scenario, BenchStats stats, bool noInput, Action<string> log)
    {
        this.scenario = scenario;
        this.stats = stats;
        this.noInput = noInput;
        this.log = log;
    }

    public long TaskbarQueries => Interlocked.Read(ref queries);
    public long TaskbarUpdates => Interlocked.Read(ref updates);
    public bool IsVisible => Volatile.Read(ref visible);

    public void Bind(GameThread owner) => game = owner;

    public long? NextDeadline
    {
        get
        {
            long? next = visible ? sceneHost?.NextDeadline : null;
            if (taskbar?.NextDeadline is { } t) next = next is { } n ? Math.Min(n, t) : t;
            return next;
        }
    }

    /// <summary>Creates the scenario on T-Game (call through <c>GameThread.Post</c> after Ready).</summary>
    public void Setup()
    {
        if (scenario == "baseline-wpf") return;
        if (scenario == "hidden")
        {
            taskbar = CreateTaskbar();
            taskbar.SetVisible(false); // hidden: no query, no deadline (SDD-21)
            return;
        }

        var scene = game.Scene;
        var dock = game.Dock;
        surface = new LayeredSurface(scene.Handle);
        dock.SceneSurface = surface;
        widget = scene.GetOrCreateWidget();
        dock.LayoutApplied += OnLayoutApplied;
        taskbar = CreateTaskbar();
        taskbar.LayoutChanged += layout => dock.SetTaskbar(layout);
        var first = dock.Relayout(); // taskbar still unknown: fallback widget geometry until the first UIA result
        ApplyScale(first);
        game.Mode.ModeChanged += _ => drawWidget = true;
        game.Mode.Surface = surface;
        game.Mode.Reapply();
        if (scenario == "e1-synthetic") sceneHost = new TestSceneHost();
        else staticRenderer = new TestSceneRenderer();
        if (!noInput && scenario == "e1-synthetic") RegisterHotkeys();
        ShowAll();
    }

    public void OnWake(IClock wakeClock)
    {
        if (shutdown) return;
        long start = wakeClock.NowTicks;
        taskbar?.OnWake();
        if (taskbar is not null)
        {
            Interlocked.Exchange(ref queries, taskbar.QueryCount);
            Interlocked.Exchange(ref updates, taskbar.UpdateCount);
        }
        if (visible)
        {
            if (sceneHost is not null)
            {
                long before = sceneHost.Timing.Frames;
                sceneHost.OnWake(wakeClock);
                var timing = sceneHost.Timing;
                if (timing.Frames > before)
                {
                    stats.RecordFrame(timing.ComposeMs, timing.PresentMs);
                    frames = timing.Frames;
                    if (frames - lastWidgetFrame >= WidgetEveryFrames) drawWidget = true;
                }
            }
            else if (drawStatic) DrawStaticFrame(wakeClock);
            if (drawWidget) DrawWidget();
        }
        stats.RecordTick((wakeClock.NowTicks - start) * 1000.0 / wakeClock.TicksPerSecond);
        SideWarMetrics.RecordWake();
    }

    public void OnCommand(IGameCommand command)
    {
        if (command is not HotkeyCommand hotkey) return;
        if (hotkey.Action == HotkeyAction.ToggleInteractive) game.Mode.ToggleInteractive();
        else ToggleVisibility();
    }

    /// <summary>ToggleVisibility (Ctrl+Alt+H / tray). Runs on T-Game.</summary>
    public void ToggleVisibility()
    {
        if (surface is null) return; // hidden/baseline scenarios have no strip
        if (visible) HideAll(); else ShowAll();
    }

    public void Shutdown() => Dispose();

    public void Dispose()
    {
        if (shutdown) return;
        shutdown = true;
        try { hotkeys?.Dispose(); } catch (Exception e) { log(e.Message); }
        try { watcher?.Dispose(); } catch (Exception e) { log(e.Message); }
        try { probe?.Dispose(); } catch (Exception e) { log(e.Message); }
        game.Dock.LayoutApplied -= OnLayoutApplied;
        sceneHost?.Dispose();
        staticRenderer?.Dispose();
        canvas.Dispose();
        widgetCanvas.Dispose();
        widgetRenderer.Dispose();
        surface?.Dispose(); // the widget disposes its own surface with the window
    }

    private TaskbarService CreateTaskbar()
    {
        var monitors = MonitorService.Enumerate();
        var monitor = MonitorResolver.Resolve(monitors, null) ?? throw new InvalidOperationException("No monitor available.");
        probe = new TaskbarProbe();
        var service = new TaskbarService(new TaskbarLocator(), new TimingProbe(probe, stats), clock, monitor, wake: WakeGame);
        return service;
    }

    // Probe completions and WinEvents arrive off T-Game: post a no-op to wake the loop (or the signal itself).
    private void WakeGame() => PostSafe(static () => { });

    private void PostSafe(Action action)
    {
        try { game.Post(action); }
        catch (InvalidOperationException) { } // stopping: nothing left to wake
    }

    private void RegisterHotkeys()
    {
        hotkeys = new HotkeyService(game, game.Scene, game, log);
        hotkeys.Conflict += (_, message) => log(message);
        hotkeys.Register();
    }

    private void ShowAll()
    {
        widget!.Show();
        game.Scene.Show();
        visible = true;
        taskbar!.SetVisible(true);
        InstallWatcher();
        if (sceneHost is not null) sceneHost.Attach(surface!, clock);
        drawStatic = true;
        drawWidget = true;
    }

    private void HideAll()
    {
        visible = false;
        watcher?.Uninstall();
        taskbar!.SetVisible(false);
        game.Scene.Hide(); // releases the widget surface; the strip surface is released here
        surface!.Release();
        canvas.Invalidate();
        widgetCanvas.Invalidate();
    }

    private void InstallWatcher()
    {
        var monitor = game.Dock.Current?.Monitor;
        var target = monitor is { } m ? new TaskbarLocator().Locate(m) : null;
        if (target is not { } t) return;
        watcher ??= new TaskbarWatcher(() => PostSafe(() => taskbar?.Signal()));
        if (!watcher.Install(t.Hwnd)) log("TaskbarWatcher: could not install the WinEvent hook.");
    }

    private void OnLayoutApplied(DockLayoutResult result)
    {
        ApplyScale(result);
        drawStatic = true;
        drawWidget = true;
        canvas.Invalidate();
        widgetCanvas.Invalidate();
    }

    private void ApplyScale(DockLayoutResult result)
    {
        float scale = result.Monitor.Dpi / 96f;
        if (surface is not null) surface.Scale = scale;
        if (widget is not null) widget.Surface.Scale = scale;
    }

    private void DrawStaticFrame(IClock wakeClock)
    {
        drawStatic = false;
        if (surface is null || staticRenderer is null || surface.Size.IsEmpty) return;
        long t0 = wakeClock.NowTicks;
        var frame = surface.BeginFrame();
        staticRenderer.Draw(canvas.Begin(frame), frame.Width, frame.Height, surface.Scale, 0, 0, 0, 0);
        canvas.End();
        long t1 = wakeClock.NowTicks;
        surface.EndFrame(null);
        long t2 = wakeClock.NowTicks;
        double perMs = 1000.0 / wakeClock.TicksPerSecond;
        stats.RecordFrame((t1 - t0) * perMs, (t2 - t1) * perMs);
    }

    private void DrawWidget()
    {
        drawWidget = false;
        lastWidgetFrame = frames;
        if (widget is null || !widget.IsVisible || widget.Surface.Size.IsEmpty) return;
        var w = widget.Surface;
        var frame = w.BeginFrame();
        widgetRenderer.Draw(widgetCanvas.Begin(frame), frame.Width, frame.Height, w.Scale, game.Mode.Mode.ToString(), scenario, frames);
        widgetCanvas.End();
        w.EndFrame(null);
    }
}
