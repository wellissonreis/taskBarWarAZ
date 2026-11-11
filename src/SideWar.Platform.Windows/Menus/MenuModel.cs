namespace SideWar.Platform.Windows.Menus;

public enum MenuCommandKind
{
    ToggleInteractive,
    TogglePause,
    ToggleVisibility,
    SelectMonitor,
    OpenSettings,
    OpenAbout,
    Exit,
}

/// <summary>A command chosen in the native menu (RF-15.8). <see cref="MonitorIndex"/> is only meaningful for SelectMonitor.</summary>
public readonly record struct MenuCommand(MenuCommandKind Kind, int MonitorIndex = -1)
{
    /// <summary>Settings and About open WPF windows, which restore the foreground themselves on close (RF-15.9).</summary>
    public bool OpensWindow => Kind is MenuCommandKind.OpenSettings or MenuCommandKind.OpenAbout;
}

/// <summary>Snapshot that decides labels, check marks and grayed items of the menu.</summary>
public sealed record MenuState(
    bool InteractiveActive = false,
    bool Paused = false,
    bool OverlayHidden = false,
    IReadOnlyList<string>? MonitorLabels = null,
    int SelectedMonitor = 0);

/// <summary>One entry of the menu as plain data (testable without a window).</summary>
public sealed record MenuEntry(string Text, MenuCommand? Command = null, bool Checked = false, bool Grayed = false,
    bool Separator = false, IReadOnlyList<MenuEntry>? Children = null);

/// <summary>Pure builder of the RF-15.8 menu (pt-BR). While hidden only "Mostrar overlay" acts (RF-15.6).</summary>
public static class MenuModel
{
    public static IReadOnlyList<MenuEntry> Build(MenuState state)
    {
        var monitors = state.MonitorLabels ?? [];
        var children = new List<MenuEntry>();
        for (int i = 0; i < monitors.Count; i++)
            children.Add(new MenuEntry(monitors[i], new MenuCommand(MenuCommandKind.SelectMonitor, i), Checked: i == state.SelectedMonitor));
        return
        [
            new("Modo interativo", new(MenuCommandKind.ToggleInteractive), Checked: state.InteractiveActive, Grayed: state.OverlayHidden),
            new(state.Paused ? "Retomar" : "Pausar", new(MenuCommandKind.TogglePause), Grayed: state.OverlayHidden),
            new(state.OverlayHidden ? "Mostrar overlay" : "Ocultar overlay", new(MenuCommandKind.ToggleVisibility)),
            new("", Separator: true),
            new("Monitor", Grayed: children.Count == 0 || state.OverlayHidden, Children: children),
            new("", Separator: true),
            new("Configurações…", new(MenuCommandKind.OpenSettings)),
            new("Sobre o SideWar…", new(MenuCommandKind.OpenAbout)),
            new("Sair", new(MenuCommandKind.Exit)),
        ];
    }
}
