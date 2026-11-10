namespace SideWar.Runtime.Lifecycle;

/// <summary>SDD-07 RF-07.1. The overlay is visible if and only if no flag is set.</summary>
[Flags]
public enum HiddenReason
{
    None = 0,
    User = 1,
    Fullscreen = 2,
    SessionLocked = 4,
    RemoteDisconnected = 8,
    DisplayOff = 16,
    Suspended = 32,
    Starting = 64,
}
