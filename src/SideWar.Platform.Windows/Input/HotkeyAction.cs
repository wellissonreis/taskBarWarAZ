using SideWar.Core;

namespace SideWar.Platform.Windows.Input;

/// <summary>Global hotkey actions (RF-15.6). The effect of each action belongs to the host.</summary>
public enum HotkeyAction
{
    ToggleInteractive,
    ToggleVisibility,
    TogglePause,
    /// <summary>Iniciar onda / chamar a próxima (modo TD); só é registrada quando o host informa o atalho.</summary>
    StartWave,
    /// <summary>ESC temporário durante o modo construção/popover TD (RegisterHotKey só enquanto ativo, RF-28.20).</summary>
    CancelBuild,
    /// <summary>Cicla as velocidades habilitadas (modo TD); só registrada quando o host informa o atalho.</summary>
    CycleSpeed,
}

/// <summary>Posted to the game command sink when a registered hotkey fires. The effect belongs to the host.</summary>
public sealed record HotkeyCommand(HotkeyAction Action) : IGameCommand;

public enum HotkeyStatus
{
    NotRegistered,
    Registered,
    Unavailable,
}
