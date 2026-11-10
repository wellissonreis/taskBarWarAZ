using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SideWar.Platform.Windows.Taskbar;

public enum TaskbarTriggerKind { None, Signal, ExplorerRestarted }

/// <summary>
/// SDD-21 RF-21.6 window-message triggers. Pure classification: the SceneWindow WndProc (T1.4/T1.5, not touched here)
/// calls it during integration and forwards the result to TaskbarService.Signal / TaskbarWatcher reinstall.
/// </summary>
public static class TaskbarTriggers
{
    private const uint WmDisplayChange = 0x007E, WmDpiChanged = 0x02E0, WmSettingChange = 0x001A;
    private const uint SpiSetWorkArea = 0x002F;
    public const string TraySettings = "TraySettings";

    private static readonly Lazy<uint> TaskbarCreated = new(() => PInvoke.RegisterWindowMessage("TaskbarCreated"));

    /// <summary>The registered "TaskbarCreated" message id; call once at startup on any thread.</summary>
    public static uint TaskbarCreatedMessage => TaskbarCreated.Value;

    public static TaskbarTriggerKind Classify(uint message, nint wParam, nint lParam, uint taskbarCreated) =>
        Classify(message, wParam, message == WmSettingChange ? ReadString(lParam) : null, taskbarCreated);

    /// <summary>lParamText is the string lParam points to for WM_SETTINGCHANGE (null if none).</summary>
    public static TaskbarTriggerKind Classify(uint message, nint wParam, string? lParamText, uint taskbarCreated)
    {
        if (taskbarCreated != 0 && message == taskbarCreated) return TaskbarTriggerKind.ExplorerRestarted;
        return message switch
        {
            WmDisplayChange or WmDpiChanged => TaskbarTriggerKind.Signal,
            WmSettingChange when (uint)wParam == SpiSetWorkArea ||
                string.Equals(lParamText, TraySettings, StringComparison.Ordinal) => TaskbarTriggerKind.Signal,
            _ => TaskbarTriggerKind.None,
        };
    }

    private static string? ReadString(nint pointer)
    {
        if (pointer == 0) return null;
        try { return Marshal.PtrToStringUni(pointer); }
        catch (Exception) { return null; }
    }
}
