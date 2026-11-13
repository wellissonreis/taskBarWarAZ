using System.Text.Json;
using System.Text.Json.Serialization;
using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Taskbar;

/// <summary>SDD-21 RF-21.9 file model. There is deliberately no Name property: app buttons get a synthetic label.</summary>
public sealed record DumpNode(string ClassName, string ParentClassName, string AutomationId,
    string ControlType, PixelRect RectPx, bool HasNotificationAreaAncestor = false, string? Label = null);

public sealed record DumpMonitor(string DeviceName, PixelRect Bounds, PixelRect WorkArea, bool IsPrimary);

public sealed record DumpLayout(string MonitorDevice, TaskbarEdge Edge, bool AutoHide, DipRect TaskbarRect,
    float HeightDip, DipRect[] AppButtons, DipRect? WidgetsButton, float LeftFreeX0, float LeftFreeX1,
    float TrayLeftX, TaskbarAlignment Alignment, TaskbarSource Source, uint Dpi, BlockedSpan[]? Blocked = null)
{
    public static DumpLayout From(TaskbarLayout l) => new(l.MonitorDevice, l.Edge, l.AutoHide, l.TaskbarRect,
        l.HeightDip, l.AppButtons.Select(b => b.Rect).ToArray(), l.WidgetsButton, l.LeftFreeZone.X0,
        l.LeftFreeZone.X1, l.TrayLeftX, l.Alignment, l.Source, l.Dpi, l.Blocked.ToArray());

    public TaskbarLayout ToLayout() => new(MonitorDevice, Edge, AutoHide, TaskbarRect, HeightDip,
        Array.AsReadOnly(AppButtons.Select((r, i) => new AppButton(i, r)).ToArray()), WidgetsButton,
        (LeftFreeX0, LeftFreeX1), Array.AsReadOnly(Blocked ?? []), TrayLeftX, Alignment, Source, Dpi);
}

/// <summary>BuildZoneMap result recorded by --dump-taskbar (RF-21.9, ADR-034); default options (useUia and towersInBar on).</summary>
public sealed record DumpBuildZones(TowerHost Host, FreeZone[] Free, int HostTopDip, int HostHeightDip)
{
    public static DumpBuildZones From(BuildZones z) => new(z.Host, z.Free.ToArray(), z.HostTopDip, z.HostHeightDip);
}

public sealed record TaskbarDump(int Schema, int WindowsBuild, uint Dpi, DumpMonitor Monitor, TaskbarEdge Edge,
    bool AutoHide, PixelRect TaskbarRectPx, DumpNode[] Nodes, DumpLayout Layout, DumpBuildZones? BuildZones = null)
{
    public const int CurrentSchema = 1;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Classifies the Button nodes (UseUia=true; the dump ignores settings) and records the layout.</summary>
    public static TaskbarDump Create(int windowsBuild, MonitorInfo monitor, TaskbarEdge edge, bool autoHide,
        PixelRect taskbarRectPx, IReadOnlyList<DumpNode> nodes)
    {
        var context = new TaskbarContext(monitor, taskbarRectPx, edge, autoHide);
        var labeled = new DumpNode[nodes.Count];
        var n = 0;
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i] with { Label = null };
            if (node.ControlType == "Button" && IsApp(node)) node = node with { Label = $"app-{n++}" };
            labeled[i] = node;
        }
        var layout = TaskbarClassifier.Classify(ToUiaNodes(labeled), context);
        return new(CurrentSchema, windowsBuild, monitor.Dpi, new(monitor.DeviceName, monitor.Bounds,
            monitor.WorkArea, monitor.IsPrimary), edge, autoHide, taskbarRectPx, labeled, DumpLayout.From(layout),
            DumpBuildZones.From(BuildZoneMap.From(layout, (int)Math.Round(monitor.Bounds.Width * 96.0 / monitor.Dpi),
                new BuildZoneOptions { TowersInBar = true })));
    }

    public static UiaNode[] ToUiaNodes(IEnumerable<DumpNode> nodes) => nodes
        .Where(x => x.ControlType == "Button")
        .Select(x => new UiaNode(x.ClassName, x.ParentClassName, x.AutomationId, x.RectPx,
            x.HasNotificationAreaAncestor)).ToArray();

    public TaskbarContext ToContext() => new(new(Monitor.DeviceName, Monitor.Bounds, Monitor.WorkArea, Dpi,
        Monitor.IsPrimary, 0), TaskbarRectPx, Edge, AutoHide);

    /// <summary>Round trip (CA-21.9): classify the recorded nodes again.</summary>
    public TaskbarLayout Reclassify() => TaskbarClassifier.Classify(ToUiaNodes(Nodes), ToContext());

    public string ToJson() => JsonSerializer.Serialize(this, Options);
    public static TaskbarDump FromJson(string json) =>
        JsonSerializer.Deserialize<TaskbarDump>(json, Options) ?? throw new JsonException("Empty dump.");

    private static bool IsApp(DumpNode node) =>
        !TaskbarClasses.SystemButtonIds.Contains(node.AutomationId) &&
        !node.ClassName.StartsWith(TaskbarClasses.TrayClassPrefix, StringComparison.Ordinal) &&
        !node.HasNotificationAreaAncestor &&
        (node.ClassName.Contains(TaskbarClasses.AppClassFragment, StringComparison.Ordinal) ||
         node.ParentClassName == TaskbarClasses.AppParentClass);
}
