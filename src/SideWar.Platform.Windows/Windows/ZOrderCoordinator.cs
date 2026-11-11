using System.Diagnostics;
using SideWar.Platform.Windows.Interop;
using SideWar.Platform.Windows.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SideWar.Platform.Windows.Windows;

/// <summary>Single point for topmost reaffirmation. Foreground watchers enqueue Request on T-Game.</summary>
public sealed class ZOrderCoordinator
{
    private readonly GameThread owner;
    private long? deadline;
    internal ZOrderCoordinator(GameThread owner) => this.owner = owner;
    internal long? NextDeadline => deadline;
    public long ReassertionCount { get; private set; }

    public void Request(bool shellForeground = false)
    {
        owner.VerifyAccess();
        if (!owner.Scene.IsVisible) return;
        var due = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * (shellForeground ? 0.1 : 1));
        // Coalesce a burst without postponing its first deadline; shell events can accelerate it.
        deadline = deadline is { } previous ? Math.Min(previous, due) : due;
    }

    internal void OnWake(long now)
    {
        if (deadline is { } due && now >= due) ReassertNow();
    }

    internal void Cancel() => deadline = null;

    internal void ReassertNow()
    {
        owner.VerifyAccess();
        deadline = null;
        if (!owner.Scene.IsVisible) return;
        // Future owned windows are appended in RF-03.9 order here, never in their own classes.
        if (!PInvoke.SetWindowPos(new HWND(owner.Scene.Handle), HWND.HWND_TOPMOST, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE |
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER))
            throw Win32Error.LastError("SetWindowPos(topmost)");
        // RF-23.10 order: scene, then widget (panel and cards join here later), so upper ones stay above.
        owner.Scene.Widget?.Reassert();
        owner.Scene.ReassertChildren(); // hotspots and seal (T4.9), above the widget
        foreach (var overlay in owner.Scene.Overlays()) overlay.Reassert(); // panel < offline card < question card (RF-24.1)
        ReassertionCount++;
    }
}
