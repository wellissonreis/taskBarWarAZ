using SideWar.App.Windows;
using SideWar.Persistence.Settings;
using SideWar.Runtime.Input;
using Xunit;

namespace SideWar.App.SmokeTests;

public class SettingsViewModelTests
{
    private static SettingsViewModel Make(out List<AppSettings> saved, bool ok = true)
    {
        var list = new List<AppSettings>();
        saved = list;
        return new SettingsViewModel(AppSettings.Default, s => { list.Add(s); return ok; });
    }

    [Fact]
    public void Unchanged_roundtrips_to_original_and_is_not_dirty()
    {
        var vm = Make(out _);
        Assert.Equal(AppSettings.Default, vm.ToSettings());
        Assert.False(vm.Dirty);
        Assert.True(vm.IsValid);
    }

    [Fact]
    public void Edits_flow_into_settings()
    {
        var vm = Make(out var saved);
        vm.PresentFps = 60; vm.AbsentFps = 4; vm.AutoScale = false; vm.StartWithWindows = true;
        vm.ReducedMotion = true; vm.HideOnFullscreen = HideOnFullscreenMode.Never; vm.TogglePause = "alt+ctrl+k";
        Assert.True(vm.Dirty);
        Assert.True(vm.TrySave());
        var s = Assert.Single(saved);
        Assert.Equal(60, s.Performance.PresentFps);
        Assert.Equal(4, s.Performance.AbsentFps);
        Assert.False(s.AutoScale);
        Assert.True(s.Behavior.StartWithWindows);
        Assert.True(s.Accessibility.ReducedMotion);
        Assert.Equal(HideOnFullscreenMode.Never, s.Behavior.HideOnFullscreen);
        Assert.Equal("Ctrl+Alt+K", s.Hotkeys.TogglePause);
    }

    [Theory]
    [InlineData(25)] [InlineData(0)] [InlineData(120)]
    public void Invalid_present_fps_blocks_save(int fps)
    {
        var vm = Make(out var saved);
        vm.PresentFps = fps;
        Assert.False(vm.IsValid);
        Assert.NotNull(vm.GetError(nameof(vm.PresentFps)));
        Assert.False(vm.TrySave());
        Assert.Empty(saved);
    }

    [Fact]
    public void Invalid_absent_fps_blocks_save()
    {
        var vm = Make(out _);
        vm.AbsentFps = 30;
        Assert.False(vm.TrySave());
    }

    [Theory]
    [InlineData("")] [InlineData("G")] [InlineData("Ctrl+")] [InlineData("Foo+G")] [InlineData("Ctrl+Ctrl+G")] [InlineData("Ctrl+Alt")] [InlineData("Ctrl+Alt+G!")]
    public void Invalid_hotkey_text(string text)
    {
        Assert.NotNull(SettingsViewModel.ValidateHotkey(text));
        var vm = Make(out _);
        vm.ToggleVisibility = text;
        Assert.False(vm.IsValid);
    }

    [Fact]
    public void Hotkey_formats_accepted_by_serializer_rules()
    {
        Assert.Null(SettingsViewModel.ValidateHotkey("Ctrl+Alt+Shift+Win+F5"));
        Assert.True(HotkeyParser.TryParse("alt+ctrl+g", out var chord, out _));
        Assert.Equal("Ctrl+Alt+G", HotkeyParser.Format(chord));
    }

    [Fact]
    public void Duplicate_hotkeys_block_save_and_restore_defaults_fixes()
    {
        var vm = Make(out _);
        vm.TogglePause = "Ctrl+Alt+G";
        Assert.NotNull(vm.HotkeyConflict);
        Assert.False(vm.IsValid && vm.AllHotkeysDistinct);
        Assert.False(vm.TrySave());
        vm.RestoreDefaultHotkeys();
        Assert.Null(vm.HotkeyConflict);
        Assert.True(vm.TrySave());
    }

    [Fact]
    public void Failed_write_keeps_error_and_returns_false()
    {
        var vm = Make(out _, ok: false);
        Assert.False(vm.TrySave());
        Assert.NotNull(vm.SaveError);
    }

    [Fact]
    public void Non_edited_fields_are_preserved()
    {
        var orig = AppSettings.Default with { Dock = new DockSettings { MonitorDevice = "X", ReserveSpace = true } };
        var vm = new SettingsViewModel(orig, _ => true);
        vm.PresentFps = 20;
        Assert.Equal("X", vm.ToSettings().Dock.MonitorDevice);
        Assert.True(vm.ToSettings().Dock.ReserveSpace);
    }

    [Fact]
    public void Saved_settings_pass_serializer_roundtrip()
    {
        var vm = Make(out var saved);
        vm.PresentFps = 20; vm.ToggleInteractive = "ctrl+shift+F9";
        Assert.True(vm.TrySave());
        Assert.Equal("Ctrl+Shift+F9", saved[0].Hotkeys.ToggleInteractive);
    }

    [Fact]
    public void About_info_has_version_and_ofl_licenses()
    {
        var info = AboutInfo.Create(@"C:\data");
        Assert.False(string.IsNullOrWhiteSpace(info.Version));
        Assert.Equal(2, info.Licenses.Count);
        Assert.All(info.Licenses, l => Assert.Contains("Open Font License", l.Text));
        Assert.Contains("Silkscreen", info.AssetsAttribution);
    }
}
