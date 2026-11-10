using System.Runtime.InteropServices;
using SideWar.Platform.Windows.Interop;
using Windows.Win32.UI.WindowsAndMessaging;
using Xunit;

namespace SideWar.Platform.Windows.Tests;

public sealed class Win32BaseTests
{
    [Theory]
    [InlineData(false, 0x08080088u)]
    [InlineData(true, 0x080800A8u)]
    public void OverlayStylesMatchNativeContract(bool clickThrough, uint expected)
    {
        Assert.Equal(expected, (uint)WindowStyles.ForOverlay(clickThrough));
        Assert.Equal(0x80000000u, (uint)WindowStyles.Popup);
        Assert.Equal(0u, (uint)(WindowStyles.Popup & WINDOW_STYLE.WS_VISIBLE));
        Assert.Equal(0u, (uint)(WindowStyles.ForOverlay(clickThrough) & WINDOW_EX_STYLE.WS_EX_APPWINDOW));
    }

    [Theory]
    [InlineData(0L, -1L)]
    [InlineData(-1L, -1L)]
    [InlineData(long.MinValue, -1L)]
    [InlineData(1L, -1L)]
    [InlineData(10_000L, -10_000L)]
    [InlineData(long.MaxValue, -long.MaxValue)]
    public void TimerDeadlineIsAlwaysRelativeAndDoesNotOverflow(long ticks, long expected)
    {
        Assert.Equal(expected, WaitableTimerDeadline.FromDelay(TimeSpan.FromTicks(ticks)));
    }

    [Fact]
    public void ErrorRetainsCapturedCodeAndOperation()
    {
        int previous = Marshal.GetLastPInvokeError();
        try
        {
            Marshal.SetLastPInvokeError(5);
            var error = Win32Error.LastError("RegisterClassExW");
            Marshal.SetLastPInvokeError(6);
            Assert.Equal(5, error.NativeErrorCode);
            Assert.Contains("RegisterClassExW", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Marshal.SetLastPInvokeError(previous);
        }
    }
}
