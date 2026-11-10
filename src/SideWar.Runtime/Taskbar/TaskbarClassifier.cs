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
            bar, height, Array.Empty<AppButton>(), null, default, bar.X + bar.W - 200,
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
        var buttons = apps.OrderBy(x => x.X).ThenBy(x => x.Y)
            .Select((r, i) => new AppButton(i, r)).ToArray();
        return new(monitor.DeviceName, context.Edge, context.AutoHide, bar, height,
            Array.AsReadOnly(buttons), widgets, (edgeL, Math.Max(edgeL, firstLeft)),
            tray ?? bar.X + bar.W - 200,
            starts[0].Rect.X - edgeL <= 16 ? TaskbarAlignment.Left : TaskbarAlignment.Center,
            TaskbarSource.Uia, monitor.Dpi);
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
