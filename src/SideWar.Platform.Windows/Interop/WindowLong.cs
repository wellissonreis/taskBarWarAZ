using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SideWar.Platform.Windows.Interop;

/// <summary>Distinguishes valid zero values from errors in window-long APIs.</summary>
internal static class WindowLong
{
    internal static nint Get(HWND window, WINDOW_LONG_PTR_INDEX index)
    {
        Marshal.SetLastPInvokeError(0);
        nint value = PInvoke.GetWindowLongPtr(window, index);
        if (value == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            throw Win32Error.LastError(nameof(PInvoke.GetWindowLongPtr));
        }

        return value;
    }

    internal static nint Set(HWND window, WINDOW_LONG_PTR_INDEX index, nint value)
    {
        Marshal.SetLastPInvokeError(0);
        nint previous = PInvoke.SetWindowLongPtr(window, index, value);
        if (previous == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            throw Win32Error.LastError(nameof(PInvoke.SetWindowLongPtr));
        }

        return previous;
    }
}
