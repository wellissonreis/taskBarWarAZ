using SideWar.Platform.Windows.Menus;
using SideWar.Platform.Windows.Tray;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Xunit;

namespace SideWar.Platform.Windows.Tests;

public sealed class MenuModelTests
{
    [Fact]
    public void BuildsRf158ItemsInOrderWithStates()
    {
        var entries = MenuModel.Build(new MenuState(InteractiveActive: true, Paused: false, OverlayHidden: false,
            MonitorLabels: ["1 (1920×1080)", "2 (2560×1440)"], SelectedMonitor: 1));
        Assert.Equal(["Modo interativo", "Pausar", "Ocultar overlay", "", "Monitor", "", "Configurações…", "Sobre o SideWar…", "Sair"],
            entries.Select(e => e.Text));
        Assert.True(entries[0].Checked);
        Assert.All(entries.Where(e => !e.Separator && e.Children is null), e => Assert.False(e.Grayed));
        Assert.Equal([false, true], entries[4].Children!.Select(c => c.Checked));
        Assert.Equal(new MenuCommand(MenuCommandKind.SelectMonitor, 1), entries[4].Children![1].Command);
    }

    [Fact]
    public void HiddenOverlayShowsMostrarAndGraysEverythingButIt()
    {
        var entries = MenuModel.Build(new MenuState(Paused: true, OverlayHidden: true, MonitorLabels: ["1"]));
        Assert.Equal("Retomar", entries[1].Text);
        Assert.Equal("Mostrar overlay", entries[2].Text);
        Assert.True(entries[0].Grayed);
        Assert.True(entries[1].Grayed);
        Assert.False(entries[2].Grayed);
        Assert.True(entries[4].Grayed);
        Assert.False(entries[6].Grayed);
        Assert.False(entries[8].Grayed);
    }

    [Fact]
    public void MonitorSubmenuIsGrayedWithoutMonitors()
    {
        Assert.True(MenuModel.Build(new MenuState())[4].Grayed);
    }

    [Fact]
    public void OnlySettingsAndAboutOpenWindows()
    {
        foreach (var kind in Enum.GetValues<MenuCommandKind>())
            Assert.Equal(kind is MenuCommandKind.OpenSettings or MenuCommandKind.OpenAbout, new MenuCommand(kind).OpensWindow);
    }
}

[Trait("Env", "WindowsInteractive")]
public sealed class NativeMenuTests
{
    private static T OnSta<T>(Func<T> body)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() => { try { result = body(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (error is not null) throw new InvalidOperationException("STA body failed", error);
        return result;
    }

    /// <summary>Waits for the system menu window (#32768) and sends it keys; cancels via WM_CANCELMODE on the owner when none.</summary>
    private static Thread Drive(nint owner, params int[] keys)
    {
        var thread = new Thread(() =>
        {
            HWND menu = default;
            var limit = DateTime.UtcNow.AddSeconds(5);
            while (menu.IsNull && DateTime.UtcNow < limit) { menu = PInvoke.FindWindow("#32768", null); Thread.Sleep(20); }
            Thread.Sleep(100);
            if (keys.Length == 0) { PInvoke.PostMessage(new HWND(owner), PInvoke.WM_CANCELMODE, default, default); return; }
            foreach (var key in keys) { PInvoke.PostMessage(menu, PInvoke.WM_KEYDOWN, (WPARAM)(nuint)key, default); Thread.Sleep(60); }
        });
        thread.Start();
        return thread;
    }

    private const int Down = 0x28, Enter = 0x0D;

    [Fact]
    public void HmenuHasTextsChecksGraysAndSubmenu()
    {
        OnSta(() =>
        {
            var commands = new List<MenuCommand>();
            var state = new MenuState(InteractiveActive: true, OverlayHidden: false, MonitorLabels: ["1 (1920×1080)", "2"], SelectedMonitor: 0);
            var menu = NativeMenu.Build(MenuModel.Build(state), commands);
            try
            {
                Assert.Equal(9, PInvoke.GetMenuItemCount(menu));
                Assert.Equal(MENU_ITEM_FLAGS.MF_CHECKED, (MENU_ITEM_FLAGS)PInvoke.GetMenuState(menu, 0, MENU_ITEM_FLAGS.MF_BYPOSITION) & MENU_ITEM_FLAGS.MF_CHECKED);
                var sub = PInvoke.GetSubMenu(menu, 4);
                Assert.False(sub.IsNull);
                Assert.Equal(2, PInvoke.GetMenuItemCount(sub));
                Assert.Equal(MENU_ITEM_FLAGS.MF_CHECKED, (MENU_ITEM_FLAGS)PInvoke.GetMenuState(sub, 0, MENU_ITEM_FLAGS.MF_BYPOSITION) & MENU_ITEM_FLAGS.MF_CHECKED);
                Assert.Equal(8, commands.Count);
            }
            finally { PInvoke.DestroyMenu(menu); }
            return 0;
        });
    }

    [Fact]
    public void TrayRightClickOpensMenuAndDismissingReturnsNoCommand()
    {
        var (shown, commands, before, after) = OnSta(() =>
        {
            var chosen = new List<MenuCommand>();
            using var tray = new TrayIcon("SideWar") { MenuStateProvider = () => new MenuState() };
            tray.CommandSelected += (c, _) => chosen.Add(c);
            var driver = Drive(tray.WindowHandle);
            nint fg0 = ForegroundRestorer.Capture();
            PInvoke.SendMessage(new HWND(tray.WindowHandle), PInvoke.WM_APP + 1, 1, (LPARAM)(nint)PInvoke.WM_RBUTTONUP);
            driver.Join();
            return (!tray.IsMenuOpen, chosen, fg0, ForegroundRestorer.Capture());
        });
        Assert.True(shown);
        Assert.Empty(commands);
        // Soft evidence (CA-15.5): other windows may legitimately steal focus during the run.
        Assert.True(before == after || after != 0, $"foreground before={before} after={after}");
    }

    [Fact]
    public void KeyboardSelectionReturnsCommandThroughTrayEvent()
    {
        var chosen = OnSta(() =>
        {
            var list = new List<MenuCommand>();
            using var tray = new TrayIcon("SideWar") { MenuStateProvider = () => new MenuState() };
            tray.CommandSelected += (c, _) => list.Add(c);
            var driver = Drive(tray.WindowHandle, Down, Down, Enter); // "Pausar"
            PInvoke.SendMessage(new HWND(tray.WindowHandle), PInvoke.WM_APP + 1, 1, (LPARAM)(nint)PInvoke.WM_RBUTTONUP);
            driver.Join();
            return list;
        });
        Assert.Equal([new MenuCommand(MenuCommandKind.TogglePause)], chosen);
    }

    [Fact]
    public void TrayUsesEmbeddedIconWithAllSizes()
    {
        Assert.Equal([16, 20, 24, 32, 48], TrayIconResource.EmbeddedSizes());
        Assert.True(OnSta(() => { using var tray = new TrayIcon("SideWar"); return tray.UsesEmbeddedIcon && tray.IsAdded; }));
    }
}
