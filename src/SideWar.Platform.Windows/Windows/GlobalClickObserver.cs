using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SideWar.Platform.Windows.Windows;

/// <summary>Observa botões globais sem consumir ou modificar os eventos.</summary>
public sealed class GlobalClickObserver : IDisposable
{
    private readonly HookProc callback;
    private nint hook;
    public GlobalClickObserver(Action<int, int> clicked)
    {
        callback = (code, message, data) =>
        {
            if (code >= 0 && ((long)message is 0x0201 or 0x0204 or 0x0207 or 0x020B))
            {
                var point = Marshal.PtrToStructure<Point>(data);
                clicked(point.X, point.Y);
            }
            return CallNextHookEx(0, code, message, data);
        };
        hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
        if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose()
    {
        if (hook != 0) { UnhookWindowsHookEx(hook); hook = 0; }
        GC.KeepAlive(callback);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    private delegate nint HookProc(int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
