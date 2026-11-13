using System.Diagnostics;
using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Taskbar;

public static class TaskbarClassifier
{
    public static TaskbarLayout Classify(UiaNode[] nodes, TaskbarContext context)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentOutOfRangeException.ThrowIfZero(context.Monitor.Dpi);
        var monitor = context.Monitor;
        var bar = ToDip(context.TaskbarRectPx, monitor);
        var height = context.Edge is TaskbarEdge.Left or TaskbarEdge.Right ? bar.W : bar.H;
        TaskbarLayout Fallback() => new(monitor.DeviceName, context.Edge, context.AutoHide,
            bar, height, Array.Empty<AppButton>(), null, default, Array.Empty<BlockedSpan>(), bar.X + bar.W - 200,
            TaskbarAlignment.Center, TaskbarSource.Fallback, monitor.Dpi);
        if (!context.UseUia || context.AutoHide || context.Edge != TaskbarEdge.Bottom)
            return Fallback();

        var apps = new List<DipRect>();
        var system = new List<(string Id, DipRect Rect)>();
        float? tray = null;
        foreach (var node in nodes)
        {
            var r = node.Rect;
            var b = context.TaskbarRectPx;
            if ((long)r.Right - r.Left < 8 || (long)r.Bottom - r.Top < 8 ||
                r.Left < b.Left || r.Top < b.Top || r.Right > b.Right || r.Bottom > b.Bottom)
                continue;
            var dip = ToDip(r, monitor);
            if (TaskbarClasses.SystemButtonIds.Contains(node.AutomationId))
                system.Add((node.AutomationId, dip));
            else if (node.ClassName.StartsWith(TaskbarClasses.TrayClassPrefix, StringComparison.Ordinal) ||
                     node.HasNotificationAreaAncestor)
                tray = Math.Min(tray ?? float.MaxValue, dip.X);
            else if (node.ClassName.Contains(TaskbarClasses.AppClassFragment, StringComparison.Ordinal) ||
                     node.ParentClassName == TaskbarClasses.AppParentClass)
                apps.Add(dip);
        }
        var starts = system.Where(x => x.Id == "StartButton").ToArray();
        if (starts.Length == 0 || apps.Count == 0)
            return Fallback();
        DipRect? widgets = system.Where(x => x.Id == "WidgetsButton")
            .Select(x => (DipRect?)x.Rect).FirstOrDefault();
        var corner = widgets is { } w && w.X - bar.X <= 16;
        var edgeL = corner ? widgets!.Value.X + widgets.Value.W : bar.X;
        var firstLeft = system.Where(x => !(corner && x.Id == "WidgetsButton"))
            .Select(x => x.Rect.X).Concat(apps.Select(x => x.X)).Min();
        if (tray is null)
            Debug.WriteLine("Taskbar: no notification area; using right minus 200 DIP.");
        var trayLeft = tray ?? bar.X + bar.W - 200;
        var buttons = apps.OrderBy(x => x.X).ThenBy(x => x.Y)
            .Select((r, i) => new AppButton(i, r)).ToArray();
        return new(monitor.DeviceName, context.Edge, context.AutoHide, bar, height,
            Array.AsReadOnly(buttons), widgets, (edgeL, Math.Max(edgeL, firstLeft)),
            BuildBlocked(bar, corner ? widgets : null, system, apps, trayLeft),
            trayLeft,
            starts[0].Rect.X - edgeL <= 16 ? TaskbarAlignment.Left : TaskbarAlignment.Center,
            TaskbarSource.Uia, monitor.Dpi);
    }

    /// <summary>RF-21.5 table: raw spans, sorted by X, then merged when touching/overlapping (first reason wins).</summary>
    private static System.Collections.ObjectModel.ReadOnlyCollection<BlockedSpan> BuildBlocked(DipRect bar, DipRect? cornerWidget,
        List<(string Id, DipRect Rect)> system, List<DipRect> apps, float trayLeft)
    {
        const float m = TaskbarClasses.IconMarginDip;
        var left = bar.X;
        var right = bar.X + bar.W;
        var raw = new List<BlockedSpan>();
        if (cornerWidget is { } w)
            raw.Add(new(left, w.X + w.W + m, BlockReason.WindowsWidget));
        else
            raw.Add(new(left, left + m, BlockReason.Edge));
        foreach (var (id, r) in system)
            if (!(cornerWidget is not null && id == "WidgetsButton"))
                raw.Add(new(r.X - m, r.X + r.W + m, BlockReason.StartSearchApps));
        foreach (var r in apps)
            raw.Add(new(r.X - m, r.X + r.W + m, BlockReason.StartSearchApps));
        raw.Add(new(trayLeft - TaskbarClasses.TrayMarginDip, right, BlockReason.TrayClock));

        var merged = new List<BlockedSpan>();
        foreach (var s in raw.Select(s => s with { X0 = Math.Max(s.X0, left), X1 = Math.Min(s.X1, right) })
                     .Where(s => s.X1 > s.X0).OrderBy(s => s.X0).ThenBy(s => (int)s.Reason))
        {
            if (merged.Count > 0 && s.X0 <= merged[^1].X1)
                merged[^1] = merged[^1] with { X1 = Math.Max(merged[^1].X1, s.X1) };
            else
                merged.Add(s);
        }
        return Array.AsReadOnly(merged.ToArray());
    }

    private static DipRect ToDip(PixelRect rect, MonitorInfo monitor)
    {
        var scale = 96.0 / monitor.Dpi;
        return new((float)(((double)rect.Left - monitor.Bounds.Left) * scale),
            (float)(((double)rect.Top - monitor.Bounds.Top) * scale),
            (float)(((double)rect.Right - rect.Left) * scale),
            (float)(((double)rect.Bottom - rect.Top) * scale));
    }
}
