using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SideWar.App.Windows;
using SideWar.Persistence.Settings;
using Xunit;

namespace SideWar.App.SmokeTests;

[Trait("Env", "WindowsInteractive")]
public class SettingsWindowTests
{
    private static void Sta(Action a)
    {
        Exception? ex = null;
        var t = new Thread(() => { try { a(); } catch (Exception e) { ex = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (ex is not null) throw new InvalidOperationException("STA failure", ex);
    }

    [Fact]
    public void Settings_window_saves_and_calls_onClosed_once()
    {
        Sta(() =>
        {
            AppSettings? saved = null; var closed = 0;
            var vm = new SettingsViewModel(AppSettings.Default, s => { saved = s; return true; });
            var w = new SettingsWindow(vm, () => closed++);
            vm.PresentFps = 60;
            w.Show();
            Assert.True(vm.TrySave());
            w.Close();
            Assert.Equal(60, saved!.Performance.PresentFps);
            Assert.Equal(1, closed);
        });
    }

    [Fact]
    public void Cancel_does_not_save_and_invalid_disables_save()
    {
        Sta(() =>
        {
            var saves = 0; var closed = 0;
            var vm = new SettingsViewModel(AppSettings.Default, _ => { saves++; return true; });
            var w = new SettingsWindow(vm, () => closed++);
            w.Show();
            vm.ToggleVisibility = "x";
            Assert.False(vm.IsValid);
            w.Close();
            Assert.Equal(0, saves);
            Assert.Equal(1, closed);
        });
    }

    [Fact]
    public void General_settings_scroll_with_wheel_and_reach_last_options()
    {
        Sta(() =>
        {
            var w = new SettingsWindow(new SettingsViewModel(AppSettings.Default, _ => true));
            w.Height = 300;
            w.Show();
            w.UpdateLayout();
            var scroll = (ScrollViewer)w.FindName("GeneralScroll");
            Assert.True(scroll.ScrollableHeight > 0);
            scroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120)
                { RoutedEvent = UIElement.MouseWheelEvent });
            w.UpdateLayout();
            Assert.True(scroll.VerticalOffset > 0);
            scroll.ScrollToEnd();
            w.UpdateLayout();
            Assert.Equal(scroll.ScrollableHeight, scroll.VerticalOffset, 1);
            w.Close();
        });
    }

    [Fact]
    public void About_window_lists_licenses_and_calls_onClosed()
    {
        Sta(() =>
        {
            var closed = 0;
            var w = new AboutWindow(AboutInfo.Create(@"C:\d"), () => closed++);
            w.Show();
            w.Close();
            Assert.Equal(1, closed);
        });
    }
}
