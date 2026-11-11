using System.Runtime.InteropServices;
using SideWar.Platform.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SideWar.Platform.Windows.Menus;

/// <summary>Restores the foreground window that was active before a menu/dialog (RF-15.8/15.9).</summary>
public static class ForegroundRestorer
{
    public static unsafe nint Capture() => (nint)PInvoke.GetForegroundWindow().Value;

    /// <summary>Allowed because this process is the foreground one while its menu/window is closing.</summary>
    public static unsafe bool Restore(nint previous)
    {
        var window = new HWND((void*)previous);
        return previous != 0 && PInvoke.IsWindow(window) && PInvoke.SetForegroundWindow(window);
    }
}

public readonly record struct MenuResult(MenuCommand? Command, nint PreviousForeground);

/// <summary>Native popup menu via TrackPopupMenuEx (ADR-010). Call on the thread that owns <c>owner</c>.</summary>
public static class NativeMenu
{
    private const uint FirstId = 1000;

    /// <summary>
    /// Shows the menu at (x, y) and returns the chosen command (null if dismissed). The foreground is restored here
    /// unless the command opens a WPF window (that window restores <see cref="MenuResult.PreviousForeground"/> on close).
    /// </summary>
    public static unsafe MenuResult Show(nint owner, int x, int y, MenuState state)
    {
        var hwnd = new HWND((void*)owner);
        var commands = new List<MenuCommand>();
        nint previous = ForegroundRestorer.Capture();
        HMENU menu = Build(MenuModel.Build(state), commands);
        MenuCommand? chosen = null;
        try
        {
            PInvoke.SetForegroundWindow(hwnd); // required by Win32 so the menu closes when clicking elsewhere
            var id = (uint)PInvoke.TrackPopupMenuEx(menu,
                (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY),
                x, y, hwnd, null).Value;
            PInvoke.PostMessage(hwnd, PInvoke.WM_NULL, default, default);
            if (id >= FirstId && id - FirstId < commands.Count) chosen = commands[(int)(id - FirstId)];
        }
        finally { PInvoke.DestroyMenu(menu); }

        if (chosen is not { OpensWindow: true }) ForegroundRestorer.Restore(previous);
        return new MenuResult(chosen, previous);
    }

    /// <summary>Builds the HMENU (caller destroys it); command ids are FirstId + index into <paramref name="commands"/>.</summary>
    internal static unsafe HMENU Build(IReadOnlyList<MenuEntry> entries, List<MenuCommand> commands)
    {
        HMENU menu = PInvoke.CreatePopupMenu();
        if (menu.IsNull) throw Win32Error.LastError("CreatePopupMenu");
        try
        {
            foreach (var entry in entries)
            {
                MENU_ITEM_FLAGS flags = MENU_ITEM_FLAGS.MF_STRING;
                if (entry.Grayed) flags |= MENU_ITEM_FLAGS.MF_GRAYED;
                if (entry.Checked) flags |= MENU_ITEM_FLAGS.MF_CHECKED;
                if (entry.Separator)
                {
                    PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (PCWSTR)null);
                }
                else if (entry.Children is { } children)
                {
                    HMENU sub = Build(children, commands);
                    bool added;
                    fixed (char* text = entry.Text)
                        added = PInvoke.AppendMenu(menu, flags | MENU_ITEM_FLAGS.MF_POPUP, (nuint)(nint)sub.Value, text);
                    if (!added)
                    {
                        PInvoke.DestroyMenu(sub);
                        throw Win32Error.LastError("AppendMenu(popup)");
                    }
                }
                else
                {
                    uint id = FirstId + (uint)commands.Count;
                    commands.Add(entry.Command ?? throw new InvalidOperationException($"Menu entry '{entry.Text}' has no command."));
                    bool added;
                    fixed (char* text = entry.Text)
                        added = PInvoke.AppendMenu(menu, flags, id, text);
                    if (!added) throw Win32Error.LastError("AppendMenu");
                }
            }
        }
        catch
        {
            PInvoke.DestroyMenu(menu);
            throw;
        }
        return menu;
    }
}
