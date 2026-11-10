using SideWar.Runtime.Fullscreen;
using Windows.Win32;
using Windows.Win32.UI.Shell;

namespace SideWar.Platform.Windows.Fullscreen;

/// <summary>SHQueryUserNotificationState mapped to <see cref="Quns"/> (SDD-06 RF-06.2).</summary>
internal static class QunsReader
{
    internal static Quns Read()
    {
        if (PInvoke.SHQueryUserNotificationState(out var state).Failed) return Quns.Unknown;
        return state switch
        {
            QUERY_USER_NOTIFICATION_STATE.QUNS_NOT_PRESENT => Quns.NotPresent,
            QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY => Quns.Busy,
            QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN => Quns.D3DFullscreen,
            QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE => Quns.PresentationMode,
            QUERY_USER_NOTIFICATION_STATE.QUNS_ACCEPTS_NOTIFICATIONS => Quns.AcceptsNotifications,
            QUERY_USER_NOTIFICATION_STATE.QUNS_QUIET_TIME => Quns.QuietTime,
            QUERY_USER_NOTIFICATION_STATE.QUNS_APP => Quns.App,
            _ => Quns.Unknown,
        };
    }
}
