using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Taskbar;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Accessibility;

namespace SideWar.Platform.Windows.Taskbar;

/// <summary>
/// SDD-21 RF-21.3. Read-only UI Automation on the dedicated MTA thread "SideWar.Shell", created on demand and
/// IsBackground. Only ClassName, AutomationId, ControlType and BoundingRectangle (plus the cached parent) are
/// read: never Name, never any control pattern, never focus. Blocks on a semaphore: no timer, no polling.
/// The IUIAutomation instance and elements are created and released per query.
/// </summary>
public sealed class TaskbarProbe : ITaskbarProbe, IDisposable
{
    public const string ThreadName = "SideWar.Shell";
    private const int ButtonId = 50000, PaneId = 50033, GroupId = 50026;

    private readonly object _gate = new();
    private readonly ConcurrentQueue<Action> _work = new();
    private readonly SemaphoreSlim _signal = new(0);
    private Thread? _thread;
    private bool _disposed;

    /// <summary>Name of the thread that ran the last query (CA-21.8 assertion).</summary>
    public string? LastQueryThreadName { get; private set; }
    public int QueriesRun { get; private set; }

    public Task<UiaNode[]> QueryAsync(nint taskbarHwnd) =>
        Run(() => ReadAll(taskbarHwnd).Where(x => x.ControlType == "Button")
            .Select(x => new UiaNode(x.ClassName, x.ParentClassName, x.AutomationId, x.RectPx,
                x.HasNotificationAreaAncestor)).ToArray());

    /// <summary>All Buttons plus Pane/Group elements, for --dump-taskbar (RF-21.9).</summary>
    public Task<DumpNode[]> DumpAsync(nint taskbarHwnd) => Run(() => ReadAll(taskbarHwnd));

    private Task<T> Run<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _thread ??= StartThread();
            _work.Enqueue(() =>
            {
                try { tcs.SetResult(func()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
        }
        _signal.Release();
        return tcs.Task;
    }

    private Thread StartThread()
    {
        var thread = new Thread(Loop) { Name = ThreadName, IsBackground = true };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return thread;
    }

    private void Loop()
    {
        while (true)
        {
            _signal.Wait();
            if (_work.TryDequeue(out var item)) item();
            else if (_disposed) return;
        }
    }

    private DumpNode[] ReadAll(nint taskbarHwnd)
    {
        // CA-21.8: every UIA call must happen on SideWar.Shell.
        if (Thread.CurrentThread.Name != ThreadName)
            throw new InvalidOperationException("UIA outside the SideWar.Shell thread.");
        LastQueryThreadName = Thread.CurrentThread.Name;
        QueriesRun++;

        object? uia = null, cache = null, all = null, root = null, found = null;
        try
        {
            var automation = (IUIAutomation)new CUIAutomation8();
            uia = automation;
            var request = automation.CreateCacheRequest();
            cache = request;
            request.AddProperty(UIA_PROPERTY_ID.UIA_ClassNamePropertyId);
            request.AddProperty(UIA_PROPERTY_ID.UIA_AutomationIdPropertyId);
            request.AddProperty(UIA_PROPERTY_ID.UIA_ControlTypePropertyId);
            request.AddProperty(UIA_PROPERTY_ID.UIA_BoundingRectanglePropertyId);
            request.TreeScope = TreeScope.TreeScope_Element | TreeScope.TreeScope_Children;
            var condition = automation.CreateTrueCondition();
            all = condition;
            var rootElement = automation.ElementFromHandle((HWND)taskbarHwnd);
            root = rootElement;
            var array = rootElement.FindAllBuildCache(TreeScope.TreeScope_Descendants, condition, request);
            found = array;
            var length = array.Length;
            var rows = new List<DumpNode>(length);
            var parents = new Dictionary<(string, string, PixelRect), string>();
            for (var i = 0; i < length; i++)
            {
                var element = array.GetElement(i);
                try
                {
                    var cls = Str(element.CachedClassName);
                    RecordChildren(element, cls, parents);
                    var control = (int)element.CachedControlType switch { ButtonId => "Button", PaneId => "Pane", GroupId => "Group", _ => null };
                    if (control is null) continue;
                    var r = element.CachedBoundingRectangle;
                    rows.Add(new(cls, "", Str(element.CachedAutomationId), control, new(r.left, r.top, r.right, r.bottom)));
                }
                finally { Marshal.ReleaseComObject(element); }
            }
            for (var i = 0; i < rows.Count; i++)
                if (parents.TryGetValue((rows[i].ClassName, rows[i].AutomationId, rows[i].RectPx), out var parentClass))
                    rows[i] = rows[i] with { ParentClassName = parentClass };
            return Resolve(rows);
        }
        finally
        {
            foreach (var com in new[] { found, root, all, cache, uia })
                if (com is not null) Marshal.ReleaseComObject(com);
        }
    }

    /// <summary>The cache request only supports Element|Children, so the parent class is derived from the cached
    /// children of each element (keyed by class, AutomationId and rectangle; no extra cross-process trips).</summary>
    private static void RecordChildren(IUIAutomationElement element, string parentClass,
        Dictionary<(string, string, PixelRect), string> parents)
    {
        var children = element.GetCachedChildren();
        if (children is null) return;
        try
        {
            for (var i = 0; i < children.Length; i++)
            {
                var child = children.GetElement(i);
                try
                {
                    var r = child.CachedBoundingRectangle;
                    parents[(Str(child.CachedClassName), Str(child.CachedAutomationId), new(r.left, r.top, r.right, r.bottom))] = parentClass;
                }
                finally { Marshal.ReleaseComObject(child); }
            }
        }
        finally { Marshal.ReleaseComObject(children); }
    }

    private static unsafe string Str(BSTR value)
    {
        try { return value.ToString() ?? ""; }
        finally { Marshal.FreeBSTR((nint)value.Value); }
    }

    /// <summary>A Button is in the notification area when a SystemTray.* Pane/Group contains its rectangle.</summary>
    private static DumpNode[] Resolve(List<DumpNode> rows)
    {
        var trays = rows.Where(x => x.ControlType != "Button" &&
            x.ClassName.StartsWith(TaskbarClasses.TrayClassPrefix, StringComparison.Ordinal)).Select(x => x.RectPx).ToArray();
        return rows.Select(x => x.ControlType == "Button" && trays.Any(t => Contains(t, x.RectPx))
            ? x with { HasNotificationAreaAncestor = true } : x).ToArray();
    }

    private static bool Contains(PixelRect outer, PixelRect inner) =>
        inner.Left >= outer.Left && inner.Top >= outer.Top && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    /// <summary>The thread is a background thread; Dispose just lets it finish queued work and exit.</summary>
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        _signal.Release();
    }
}
