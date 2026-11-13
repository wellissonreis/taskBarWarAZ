using SideWar.Core.Simulation;
using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Taskbar;

/// <summary>Result of an observation: what the scene window must be and what the simulation was told.</summary>
public readonly record struct SceneLayoutUpdate(PixelRect Scene, TowerHost Host, StripLayout Layout, bool SceneChanged, bool LayoutPosted);

/// <summary>
/// Pure service: compares the previous scene/layout against the current monitor, bar and DPI and posts
/// <see cref="LayoutChanged"/> (only when the StripLayout differs) to the host queue, which applies it between ticks
/// (ADR-026, SDD-11 RF-11.3). Never reads the system; the Windows side feeds it (DockController / TaskbarService events).
/// Not thread-safe: call on the game thread.
/// </summary>
public sealed class SceneLayoutWatcher(IGameCommandSink sink, StripLayout? initial = null)
{
    private readonly IGameCommandSink _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    private StripLayout? _layout = initial;
    private PixelRect? _scene;

    public StripLayout? CurrentLayout => _layout;

    /// <summary>Returns <c>null</c> when nothing changed (same monitor geometry, DPI, bar and options).</summary>
    public SceneLayoutUpdate? Observe(MonitorInfo monitor, TaskbarLayout bar, BuildZoneOptions options)
    {
        var widthDip = StripLayoutFactory.WidthDip(monitor.WorkArea.Width, monitor.Dpi);
        var zones = BuildZoneMap.From(bar, widthDip, options);
        var layout = StripLayoutFactory.Create(zones, widthDip);
        var scene = SceneLayout.Compute(monitor, bar, zones.Host);

        var sceneChanged = _scene != scene;
        var layoutChanged = _layout is null || !Same(_layout, layout);
        if (!sceneChanged && !layoutChanged) return null;

        _scene = scene;
        if (layoutChanged)
        {
            _layout = layout;
            _sink.Post(new LayoutChanged(layout));
        }
        return new SceneLayoutUpdate(scene, zones.Host, layout, sceneChanged, layoutChanged);
    }

    private static bool Same(StripLayout a, StripLayout b) =>
        a.Width == b.Width && a.SpawnX == b.SpawnX && a.LeakX == b.LeakX && a.CoreX == b.CoreX &&
        a.PortalX == b.PortalX && a.Host == b.Host && a.FreeZones.SequenceEqual(b.FreeZones);
}
