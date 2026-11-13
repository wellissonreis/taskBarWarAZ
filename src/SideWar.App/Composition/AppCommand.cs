using SideWar.Core;

namespace SideWar.App.Composition;

internal enum AppCommandKind
{
    ToggleInteractive,
    ToggleVisibility,
    TogglePause,
    SelectMonitor,
    /// <summary>Segunda instância (SDD-07 RF-07.8): remove <c>HiddenReason.User</c> e entra no modo Interativo.</summary>
    ShowAndInteract,
}

/// <summary>Comando do App (bandeja, instância única) postado à T-Game; interceptado antes da simulação.</summary>
internal sealed record AppCommand(AppCommandKind Kind, int MonitorIndex = -1) : IGameCommand;

/// <summary>Pedido da T-Game para a T-UI (janelas WPF e encerramento).</summary>
internal enum UiRequest { OpenSettings, OpenAbout, Exit }

/// <summary>Estado do menu (bandeja/widget) publicado pela T-Game como snapshot imutável (SDD-01 RF-01.4 regra 3).</summary>
internal sealed record MenuSnapshot(bool Interactive, bool Paused, bool Hidden, IReadOnlyList<string> Monitors, int SelectedMonitor)
{
    public static MenuSnapshot Initial { get; } = new(false, false, true, [], 0);
}
