using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;

namespace SideWar.Platform.Windows.Fullscreen;

/// <summary>
/// An installed WinEvent hook together with the native delegate that backs it. The delegate is a field, so it stays
/// reachable (not collected) for as long as the hook exists; it is released only after the hook is removed.
/// A callback on a collected delegate is a FailFast in the CLR.
/// </summary>
internal sealed class WinEventHook : IDisposable
{
    private readonly WINEVENTPROC callback; // rooted for the lifetime of the hook
    private UnhookWinEventSafeHandle? handle;

    private WinEventHook(WINEVENTPROC callback, UnhookWinEventSafeHandle handle)
    {
        this.callback = callback;
        this.handle = handle;
    }

    internal bool IsInstalled => handle is { IsClosed: false };

    internal static WinEventHook Install(uint min, uint max, WINEVENTPROC callback, uint pid, uint thread, bool skipOwn)
    {
        uint flags = PInvoke.WINEVENT_OUTOFCONTEXT | (skipOwn ? PInvoke.WINEVENT_SKIPOWNPROCESS : 0);
        var handle = PInvoke.SetWinEventHook(min, max, default, callback, pid, thread, flags);
        if (handle.IsInvalid)
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "SetWinEventHook failed");
        return new WinEventHook(callback, handle);
    }

    public void Dispose()
    {
        var h = handle;
        handle = null;
        h?.Dispose();
        GC.KeepAlive(callback); // keeps the delegate alive until after the unhook
    }
}
