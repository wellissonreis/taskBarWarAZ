using SideWar.Platform.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SideWar.Platform.Windows.Windows;

public enum WidgetMenuCommand
{
    None = 0,
    ToggleInteractive,
    TogglePause,
    HideOverlay,
    Settings,
    About,
    Exit,
    SelectMonitor,
}

/// <summary>Estado que decide marcas e rótulos do menu (SDD-15 RF-15.8).</summary>
public sealed record WidgetMenuState(bool Interactive, bool Paused, IReadOnlyList<string> Monitors, int CurrentMonitor);

/// <summary>Escolha do usuário; <see cref="MonitorIndex"/> só vale para <see cref="WidgetMenuCommand.SelectMonitor"/>.</summary>
public readonly record struct WidgetMenuResult(WidgetMenuCommand Command, int MonitorIndex = -1);

/// <summary>Porta do menu de clique direito; a implementação nativa unifica com o menu da bandeja (T5.8) na integração.</summary>
public interface IWidgetMenu
{
    /// <summary>Mostra o menu no ponto de tela; devolve a escolha ou null se cancelado. Bloqueia até fechar.</summary>
    WidgetMenuResult? Show(nint owner, int screenX, int screenY, WidgetMenuState state);
}

/// <summary>
/// Menu nativo mínimo local (SDD-15 RF-15.8, ADR-010): <c>TrackPopupMenuEx</c> com o ritual de foreground e
/// <c>ForegroundRestorer</c> embutido. Itens: Modo interativo (✓), Pausar/Retomar, Ocultar overlay, Monitor ▸,
/// Configurações…, Sobre o SideWar…, Sair. Só na T-Game.
/// </summary>
public sealed class NativeWidgetMenu : IWidgetMenu
{
    private const uint MonitorBase = 100;

    public unsafe WidgetMenuResult? Show(nint owner, int screenX, int screenY, WidgetMenuState state)
    {
        HWND hwnd = new(owner);
        HWND previous = PInvoke.GetForegroundWindow();
        var menu = PInvoke.CreatePopupMenu();
        if (menu.IsNull) throw Win32Error.LastError("CreatePopupMenu");
        try
        {
            Build(menu, state);
            // O Win32 exige o dono em primeiro plano para o menu fechar ao clicar fora (RF-15.8).
            PInvoke.SetForegroundWindow(hwnd);
            var chosen = (uint)PInvoke.TrackPopupMenuEx(menu,
                (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY),
                screenX, screenY, hwnd, null).Value;
            PInvoke.PostMessage(hwnd, PInvoke.WM_NULL, default, default);
            var result = Decode(chosen);
            // Comandos que abrem janela WPF ficam com o foco; os demais devolvem o foreground ao app anterior.
            if (result is not { Command: WidgetMenuCommand.Settings or WidgetMenuCommand.About })
                RestoreForeground(previous);
            return result;
        }
        finally
        {
            PInvoke.DestroyMenu(menu); // Destrói também o submenu Monitor.
        }
    }

    private static unsafe void Build(HMENU menu, WidgetMenuState s)
    {
        Append(menu, (uint)WidgetMenuCommand.ToggleInteractive, "Modo interativo", s.Interactive ? MENU_ITEM_FLAGS.MF_CHECKED : 0);
        Append(menu, (uint)WidgetMenuCommand.TogglePause, s.Paused ? "Retomar" : "Pausar");
        Append(menu, (uint)WidgetMenuCommand.HideOverlay, "Ocultar overlay");
        Separator(menu);
        var monitors = PInvoke.CreatePopupMenu();
        for (int i = 0; i < s.Monitors.Count; i++)
            Append(monitors, MonitorBase + (uint)i, s.Monitors[i], i == s.CurrentMonitor ? MENU_ITEM_FLAGS.MF_CHECKED : 0);
        if (s.Monitors.Count == 0) Append(monitors, 0, "(nenhum)", MENU_ITEM_FLAGS.MF_GRAYED);
        // O submenu passa a pertencer ao pai e é destruído com ele.
        if (!AppendRaw(menu, MENU_ITEM_FLAGS.MF_POPUP, (nuint)monitors.Value, "Monitor")) throw Win32Error.LastError("AppendMenu(popup)");
        Separator(menu);
        Append(menu, (uint)WidgetMenuCommand.Settings, "Configurações…");
        Append(menu, (uint)WidgetMenuCommand.About, "Sobre o SideWar…");
        Append(menu, (uint)WidgetMenuCommand.Exit, "Sair");
    }

    private static unsafe bool AppendRaw(HMENU menu, MENU_ITEM_FLAGS flags, nuint id, string? text)
    {
        fixed (char* p = text) return PInvoke.AppendMenu(menu, flags, id, p);
    }

    private static unsafe void Append(HMENU menu, uint id, string text, MENU_ITEM_FLAGS flags = 0)
    {
        if (!AppendRaw(menu, MENU_ITEM_FLAGS.MF_STRING | flags, id, text)) throw Win32Error.LastError("AppendMenu");
    }

    private static unsafe void Separator(HMENU menu)
    {
        if (!AppendRaw(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, null)) throw Win32Error.LastError("AppendMenu(separator)");
    }

    private static WidgetMenuResult? Decode(uint id)
    {
        if (id == 0) return null;
        if (id >= MonitorBase) return new WidgetMenuResult(WidgetMenuCommand.SelectMonitor, (int)(id - MonitorBase));
        return Enum.IsDefined((WidgetMenuCommand)id) ? new WidgetMenuResult((WidgetMenuCommand)id) : null;
    }

    private static void RestoreForeground(HWND previous)
    {
        if (previous.IsNull || !PInvoke.IsWindow(previous)) return;
        if (PInvoke.GetForegroundWindow() != previous) PInvoke.SetForegroundWindow(previous);
    }
}
