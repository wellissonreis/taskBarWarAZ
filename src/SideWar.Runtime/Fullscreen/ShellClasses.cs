using System.Collections.Frozen;

namespace SideWar.Runtime.Fullscreen;

/// <summary>Window classes SDD-06 R5/R6 treat specially. Hypotheses until confirmed by real Windows 11 captures (E2).</summary>
public static class ShellClasses
{
    /// <summary>Transient shell surfaces (Start, Win+Tab, Alt+Tab, tray overflow, taskbar): never change the state.</summary>
    public static FrozenSet<string> ShellTransient { get; } = new[]
    {
        "Windows.UI.Core.CoreWindow",            // Start menu, search, notification center (UWP shell hosts)
        "XamlExplorerHostIslandWindow",          // Windows 11 XAML island hosts (Start, Task View)
        "MultitaskingViewFrame",                 // Win+Tab / Alt+Tab Task View
        "ForegroundStaging",                     // transient staging window during Task View/Alt+Tab
        "TaskListThumbnailWnd",                  // taskbar thumbnail preview
        "Shell_TrayWnd",                         // primary taskbar
        "Shell_SecondaryTrayWnd",                // taskbar on other monitors
        "NotifyIconOverflowWindow",              // legacy tray overflow flyout
        "TopLevelWindowForOverflowXamlIsland",   // Windows 11 tray overflow flyout
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Desktop windows: focus on the wallpaper means no fullscreen content is in front.</summary>
    public static FrozenSet<string> Desktop { get; } = new[]
    {
        "Progman",   // desktop shell window
        "WorkerW",   // desktop wallpaper host
    }.ToFrozenSet(StringComparer.Ordinal);
}
