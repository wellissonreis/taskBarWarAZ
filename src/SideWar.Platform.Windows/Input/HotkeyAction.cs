using SideWar.Core;

namespace SideWar.Platform.Windows.Input;

/// <summary>Global hotkey actions available in T1.12 (RF-15.6). TogglePause is out of scope.</summary>
public enum HotkeyAction
{
    ToggleInteractive,
    ToggleVisibility,
}

/// <summary>Posted to the game command sink when a registered hotkey fires. The effect belongs to the host.</summary>
public sealed record HotkeyCommand(HotkeyAction Action) : IGameCommand;

public enum HotkeyStatus
{
    NotRegistered,
    Registered,
    Unavailable,
}
