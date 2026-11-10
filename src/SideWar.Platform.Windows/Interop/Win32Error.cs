using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SideWar.Platform.Windows.Interop;

internal static class Win32Error
{
    /// <summary>Call immediately after a failed API that documents GetLastError.</summary>
    internal static Win32Exception LastError(string operation)
    {
        int error = Marshal.GetLastPInvokeError();
        return new Win32Exception(error, $"{operation} failed (Win32 error {error}).");
    }
}
