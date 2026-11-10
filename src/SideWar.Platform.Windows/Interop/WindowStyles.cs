using Windows.Win32.UI.WindowsAndMessaging;

namespace SideWar.Platform.Windows.Interop;

/// <summary>Shared styles for the layered window family (SDD-03 RF-03.9).</summary>
internal static class WindowStyles
{
    internal const WINDOW_STYLE Popup = WINDOW_STYLE.WS_POPUP;

    internal const WINDOW_EX_STYLE Overlay =
        WINDOW_EX_STYLE.WS_EX_LAYERED |
        WINDOW_EX_STYLE.WS_EX_TOOLWINDOW |
        WINDOW_EX_STYLE.WS_EX_NOACTIVATE |
        WINDOW_EX_STYLE.WS_EX_TOPMOST;

    // Only the scene in passive mode and the absent seal use click-through.
    internal static WINDOW_EX_STYLE ForOverlay(bool clickThrough) =>
        clickThrough ? Overlay | WINDOW_EX_STYLE.WS_EX_TRANSPARENT : Overlay;
}
