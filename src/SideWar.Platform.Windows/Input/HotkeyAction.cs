using SideWar.Core;

namespace SideWar.Platform.Windows.Input;

/// <summary>Global hotkey actions (RF-15.6). The effect of each action belongs to the host.</summary>
public enum HotkeyAction
{
    ToggleInteractive,
    ToggleVisibility,
    TogglePause,
}

/// <summary>Posted to the game command sink when a registered hotkey fires. The effect belongs to the host.</summary>
public sealed record HotkeyCommand(HotkeyAction Action) : IGameCommand;

public enum HotkeyStatus
{
    NotRegistered,
    Registered,
    Unavailable,
}
