using SideWar.Core;

namespace SideWar.App.Composition;

internal enum AppCommandKind
{
    ToggleInteractive,
    ToggleVisibility,
    TogglePause,
    /// <summary>"Iniciar onda" (modo TD): StartWave em Preparo, CallNextWave em Onda.</summary>
    StartWave,
    /// <summary>"Mostrar HUD" / "Recolher HUD" do menu da bandeja (TD.14).</summary>
    ToggleHud,
    /// <summary>"Como jogar" da bandeja (TD.15).</summary>
    HowToPlay,
    SelectMonitor,
    /// <summary>Segunda instância (SDD-07 RF-07.8): remove <c>HiddenReason.User</c> e entra no modo Interativo.</summary>
    ShowAndInteract,
    /// <summary>ESC temporário do modo construção/popover TD.</summary>
    CancelBuild,
    /// <summary>TD.16b: coleta a caixa-bomba (atalho global ou bandeja).</summary>
    CollectCrate,
}

/// <summary>Comando do App (bandeja, instância única) postado à T-Game; interceptado antes da simulação.</summary>
internal sealed record AppCommand(AppCommandKind Kind, int MonitorIndex = -1) : IGameCommand;

/// <summary>Pedido da T-Game para a T-UI (janelas WPF e encerramento).</summary>
internal enum UiRequest { OpenSettings, OpenAbout, Exit, SaveHudState, SaveOnboarding, SaveTdOptions }

/// <summary>Estado do menu (bandeja/widget) publicado pela T-Game como snapshot imutável (SDD-01 RF-01.4 regra 3).</summary>
internal sealed record MenuSnapshot(bool Interactive, bool Paused, bool Hidden, IReadOnlyList<string> Monitors, int SelectedMonitor,
    bool TdMode = false, string? StartWaveHotkey = null, bool HudMinimized = false)
{
    public static MenuSnapshot Initial { get; } = new(false, false, true, [], 0);
}
