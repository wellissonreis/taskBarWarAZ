using System.Diagnostics;
using System.Runtime.InteropServices;
using SideWar.Platform.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SideWar.Platform.Windows.Tray;

/// <summary>
/// Minimal notification-area icon (RF-15.7, partial). Must be created and disposed on a thread that
/// pumps messages (T-UI in production). Left click raises <see cref="ToggleVisibilityRequested"/>,
/// right click raises <see cref="MenuRequested"/> (the native menu is a later ficha); TaskbarCreated re-adds the icon.
/// The icon is the stock application icon: the embedded .ico is out of scope.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string ClassName = "SideWar.TrayHost";
    private const uint CallbackMessage = PInvoke.WM_APP + 1;
    private const int MaxTooltip = 127;
    private static readonly TimeSpan TooltipInterval = TimeSpan.FromSeconds(1);
    private static readonly WNDPROC Procedure = WindowProcedure;

    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private readonly HINSTANCE module;
    private readonly uint taskbarCreated;
    private readonly Func<long> timestamp;
    private GCHandle self;
    private ushort atom;
    private HWND handle;
    private string tooltip;
    private string? pendingTooltip;
    private long lastTooltipTicks;
    private bool added;
    private bool disposed;

    public unsafe TrayIcon(string tooltip = "SideWar", Func<long>? timestamp = null)
    {
        this.tooltip = Clamp(tooltip);
        this.timestamp = timestamp ?? Stopwatch.GetTimestamp;
        lastTooltipTicks = this.timestamp();
        module = new HINSTANCE(PInvoke.GetModuleHandle((PCWSTR)null).Value);
        try
        {
            fixed (char* name = "TaskbarCreated")
                taskbarCreated = PInvoke.RegisterWindowMessage(name);
            fixed (char* className = ClassName + "." + Environment.ProcessId + "." + ownerThread)
            {
                var cls = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    lpfnWndProc = Procedure,
                    hInstance = module,
                    lpszClassName = className,
                };
                atom = PInvoke.RegisterClassEx(cls);
                if (atom == 0) throw Win32Error.LastError("RegisterClassEx(tray)");
                self = GCHandle.Alloc(this);
                // Top-level hidden window (not message-only): broadcasts such as TaskbarCreated don't reach message-only windows.
                handle = PInvoke.CreateWindowEx(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE,
                    className, null, WINDOW_STYLE.WS_POPUP, 0, 0, 0, 0, default, default, module,
                    (void*)GCHandle.ToIntPtr(self));
                if (handle.IsNull) throw Win32Error.LastError("CreateWindowEx(tray)");
            }
            Add();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public event Action? ToggleVisibilityRequested;
    public event Action? MenuRequested;

    public bool IsAdded => added;
    public string Tooltip => tooltip;
    public int TooltipUpdates { get; private set; }
    public int RecreateCount { get; private set; }
    public unsafe nint WindowHandle => (nint)handle.Value;

    /// <summary>Updates the tooltip at most once per second and only if the text changed; the latest rejected text is retried on the next call.</summary>
    public bool SetTooltip(string text)
    {
        CheckAccess();
        text = Clamp(text);
        if (text == tooltip) { pendingTooltip = null; return false; }
        long now = timestamp();
        if (now - lastTooltipTicks < TooltipInterval.TotalSeconds * Stopwatch.Frequency)
        {
            pendingTooltip = text;
            return false;
        }
        tooltip = text;
        pendingTooltip = null;
        lastTooltipTicks = now;
        if (added) Notify(NOTIFY_ICON_MESSAGE.NIM_MODIFY);
        TooltipUpdates++;
        return true;
    }

    /// <summary>Applies a tooltip held back by the 1 Hz limit once its interval elapsed.</summary>
    public bool FlushTooltip() => pendingTooltip is { } text && SetTooltip(text);

    private static string Clamp(string text) => text.Length > MaxTooltip ? text[..MaxTooltip] : text;

    private void CheckAccess()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("TrayIcon requires its creating thread.");
    }

    private void Add()
    {
        Notify(NOTIFY_ICON_MESSAGE.NIM_ADD);
        added = true;
    }

    private void Recreate()
    {
        try { Notify(NOTIFY_ICON_MESSAGE.NIM_DELETE); } catch (Exception) { /* Explorer already dropped it. */ }
        added = false;
        Add();
        RecreateCount++;
    }

    private unsafe void Notify(NOTIFY_ICON_MESSAGE message)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = handle,
            uID = 1,
            uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP,
            uCallbackMessage = CallbackMessage,
            hIcon = PInvoke.LoadIcon(default, PInvoke.IDI_APPLICATION),
        };
        tooltip.AsSpan().CopyTo(data.szTip.AsSpan());
        if (!PInvoke.Shell_NotifyIcon(message, in data))
            throw Win32Error.LastError($"Shell_NotifyIcon({message})");
    }

    public unsafe void Dispose()
    {
        if (disposed) return;
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("TrayIcon requires its creating thread.");
        disposed = true;
        if (added && !handle.IsNull)
        {
            var data = new NOTIFYICONDATAW { cbSize = (uint)sizeof(NOTIFYICONDATAW), hWnd = handle, uID = 1 };
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, in data);
            added = false;
        }
        if (!handle.IsNull) PInvoke.DestroyWindow(handle);
        handle = default;
        if (self.IsAllocated) self.Free();
        if (atom != 0)
        {
            fixed (char* name = ClassName + "." + Environment.ProcessId + "." + ownerThread)
                PInvoke.UnregisterClass(name, module);
            atom = 0;
        }
    }

    private static unsafe LRESULT WindowProcedure(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        TrayIcon? icon = null;
        try
        {
            if (message == PInvoke.WM_NCCREATE)
            {
                var data = (CREATESTRUCTW*)lParam.Value;
                WindowLong.Set(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA, (nint)data->lpCreateParams);
            }
            var pointer = WindowLong.Get(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA);
            if (pointer != 0) icon = (TrayIcon?)GCHandle.FromIntPtr(pointer).Target;
            if (icon is not null && !icon.disposed)
            {
                if (message == CallbackMessage)
                {
                    switch ((uint)(lParam.Value & 0xFFFF))
                    {
                        case PInvoke.WM_LBUTTONUP: icon.ToggleVisibilityRequested?.Invoke(); break;
                        case PInvoke.WM_RBUTTONUP: icon.MenuRequested?.Invoke(); break;
                    }
                    return new LRESULT(0);
                }
                if (icon.taskbarCreated != 0 && message == icon.taskbarCreated)
                {
                    icon.Recreate(); // Explorer restarted: recreate the icon.
                    return new LRESULT(0);
                }
            }
            if (message == PInvoke.WM_NCDESTROY)
                WindowLong.Set(hwnd, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA, 0);
        }
        catch (Exception error)
        {
            Trace.WriteLine(error);
        }
        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }
}
