namespace SideWar.Runtime.Fullscreen;

/// <summary>Port the tracker uses to re-read the system (SDD-06 RF-06.4). Called only on T-Game.</summary>
public interface IWindowProbe
{
    /// <summary>Current foreground HWND, or 0 when there is none.</summary>
    nint GetForeground();

    bool IsWindow(nint hwnd);

    /// <summary>Snapshot of <paramref name="hwnd"/>; a dead window returns a snapshot with <c>Hwnd == 0</c>.</summary>
    WindowSnapshot Capture(nint hwnd);

    Quns QueryQuns();

    /// <summary>True when <paramref name="upper"/> is above <paramref name="lower"/> in the z-order.</summary>
    bool IsAbove(nint upper, nint lower);
}
