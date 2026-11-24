using System.Text;
using System.Text.Json;
using SideWar.Persistence.Settings;
using Xunit;

namespace SideWar.Persistence.Tests;

/// <summary>TD.6: gameMode (oculto, padrão idle), auto-início (padrão desligado, 5–60 s) e atalho Iniciar onda.</summary>
public class SettingsTdTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Alert_options_migrate_and_roundtrip_independently()
    {
        var old = ReadPayload("{\"td\":{\"autoRestart\":true}}").Td;
        Assert.True(old.AlertsEnabled);
        Assert.True(old.AlertsAutoPause);
        Assert.False(old.AlertsSound);
        Assert.True(old.AutoRestart);

        var fs = new FakeFileSystem();
        var store = new SettingsStore(fs, "/data", utcNow: () => Now);
        Assert.True(store.Save(new AppSettings { Td = new TdSettings { AlertsEnabled = false, AlertsAutoPause = true, AlertsSound = true } }));
        var result = store.Load().Settings.Td;
        Assert.False(result.AlertsEnabled);
        Assert.True(result.AlertsAutoPause);
        Assert.True(result.AlertsSound);
    }

    [Fact]
    public void Fresh_install_defaults_never_auto_start_and_show_the_tutorial()
    {
        var fresh = new SettingsStore(new FakeFileSystem(), "/data", utcNow: () => Now).Load();
        Assert.Equal(SettingsSource.Defaults, fresh.Source);
        AssertFirstRun(fresh.Settings.Td);
        AssertFirstRun(ReadPayload("{}").Td);               // arquivo sem seção td
        AssertFirstRun(ReadPayload("{\"td\":{}}").Td);       // seção vazia
        AssertFirstRun(ReadPayload("{\"td\":{\"autoStartEnabled\":\"sim\",\"autoRestart\":1,\"tutorialDone\":\"x\"}}").Td); // tipos inválidos caem no padrão
    }

    [Fact]
    public void Values_already_saved_by_the_user_are_preserved()
    {
        var td = ReadPayload("{\"td\":{\"autoStartEnabled\":true,\"autoRestart\":true,\"tutorialDone\":true}}").Td;
        Assert.True(td.AutoStartEnabled);
        Assert.True(td.AutoRestart);
        Assert.True(td.TutorialDone);
    }

    private static void AssertFirstRun(TdSettings td)
    {
        Assert.False(td.AutoStartEnabled);
        Assert.False(td.AutoRestart);
        Assert.False(td.TutorialDone);
        Assert.True(td.HudMinimized);
        Assert.Equal("", td.TipsSeen);
    }

    [Fact]
    public void Corner_scale_migrates_validates_and_roundtrips()
    {
        Assert.Equal(125, ReadPayload("{\"td\":{}}").Td.CornerUiScalePercent);
        Assert.Equal(130, ReadPayload("{\"td\":{\"cornerUiScalePercent\":130}}").Td.CornerUiScalePercent);
        Assert.Equal(100, ReadPayload("{\"td\":{\"cornerUiScalePercent\":99}}").Td.CornerUiScalePercent);
        Assert.Equal(250, ReadPayload("{\"td\":{\"cornerUiScalePercent\":251}}").Td.CornerUiScalePercent);
        Assert.Equal(125, ReadPayload("{\"td\":{\"cornerUiScalePercent\":null}}").Td.CornerUiScalePercent);
        var fs = new FakeFileSystem();
        var store = new SettingsStore(fs, "/data", utcNow: () => Now);
        Assert.True(store.Save(new AppSettings { Td = new TdSettings { CornerUiScalePercent = 237, BandHeightDip = 40 } }));
        Assert.Equal(237, store.Load().Settings.Td.CornerUiScalePercent);
        Assert.Equal(40, store.Load().Settings.Td.BandHeightDip);
    }

    private static AppSettings ReadPayload(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return SettingsSerializer.Read(doc.RootElement);
    }

    [Fact]
    public void AutoRestart_defaults_off_and_roundtrips()
    {
        Assert.False(ReadPayload("{\"td\":{}}").Td.AutoRestart);
        var fs = new FakeFileSystem();
        var store = new SettingsStore(fs, "/data", utcNow: () => Now);
        Assert.True(store.Save(new AppSettings { Td = new TdSettings { AutoRestart = true } }));
        Assert.True(store.Load().Settings.Td.AutoRestart);
    }

    [Fact]
    public void Defaults_keep_idle_and_autostart_off()
    {
        var s = AppSettings.Default;
        Assert.Equal(GameMode.Td, s.GameMode);
        Assert.False(s.Td.AutoStartEnabled);
        Assert.InRange(s.Td.AutoStartSeconds, TdSettings.MinAutoStartSeconds, TdSettings.MaxAutoStartSeconds);
        Assert.Equal("Ctrl+Alt+Space", s.Hotkeys.StartWave);
        Assert.Equal(GameMode.Td, ReadPayload("{}").GameMode);
    }

    [Fact]
    public void RoundTrip_keeps_td_fields()
    {
        var fs = new FakeFileSystem();
        var st = new SettingsStore(fs, "/data", utcNow: () => Now);
        var s = new AppSettings
        {
            GameMode = GameMode.Td,
            Td = new TdSettings { AutoStartEnabled = true, AutoStartSeconds = 30 },
            Hotkeys = new HotkeySettings { StartWave = "Ctrl+Alt+W" },
        };
        Assert.True(st.Save(s));
        var r = st.Load().Settings;
        Assert.Equal(GameMode.Td, r.GameMode);
        Assert.True(r.Td.AutoStartEnabled);
        Assert.Equal(30, r.Td.AutoStartSeconds);
        Assert.Equal("Ctrl+Alt+W", r.Hotkeys.StartWave);
    }

    [Fact]
    public void Extra_speeds_default_off_and_roundtrip_with_mask_max_speed_and_cycle_hotkey()
    {
        Assert.False(AppSettings.Default.Td.ExtraSpeeds);
        Assert.Equal(0b1000_1010, AppSettings.Default.Td.ExtraSpeedMask);
        Assert.Equal("Ctrl+Alt+V", AppSettings.Default.Hotkeys.CycleSpeed);
        var fs = new FakeFileSystem();
        var st = new SettingsStore(fs, "/data", utcNow: () => Now);
        var s = new AppSettings { Td = new TdSettings { ExtraSpeeds = true, ExtraSpeedMask = 0b0110_0001, GameSpeed = 100 }, Hotkeys = new HotkeySettings { CycleSpeed = "Ctrl+Alt+K" } };
        Assert.True(st.Save(s));
        var r = st.Load().Settings;
        Assert.True(r.Td.ExtraSpeeds);
        Assert.Equal(0b0110_0001, r.Td.ExtraSpeedMask);
        Assert.Equal(100, r.Td.GameSpeed);
        Assert.Equal("Ctrl+Alt+K", r.Hotkeys.CycleSpeed);
    }

    [Fact]
    public void Continuous_waves_default_on_and_tower_panel_pinned_default_off_roundtrip_and_old_files_get_defaults()
    {
        Assert.True(AppSettings.Default.Td.ContinuousWaves);
        Assert.False(AppSettings.Default.Td.TowerPanelPinned);
        Assert.Equal("Ctrl+Alt+H", AppSettings.Default.Hotkeys.ToggleVisibility); // atalho de minimizar tudo (TD.28b)
        var old = ReadPayload("{\"td\":{\"autoStartEnabled\":true}}");
        Assert.True(old.Td.ContinuousWaves);
        Assert.False(old.Td.TowerPanelPinned);

        var fs = new FakeFileSystem();
        var st = new SettingsStore(fs, "/data", utcNow: () => Now);
        Assert.True(st.Save(new AppSettings { Td = new TdSettings { ContinuousWaves = false, TowerPanelPinned = true } }));
        var r = st.Load().Settings;
        Assert.False(r.Td.ContinuousWaves);
        Assert.True(r.Td.TowerPanelPinned);
    }

    [Fact]
    public void Towers_in_bar_stays_off_and_legacy_collect_crate_hotkey_in_old_files_is_ignored()
    {
        Assert.False(AppSettings.Default.Taskbar.TowersInBar); // TD.16b: modo "dentro da barra" segue desabilitado
        var r = ReadPayload("{\"hotkeys\":{\"CollectTdCrate\":\"Ctrl+Alt+K\"}}");
        Assert.Equal(AppSettings.Default.Hotkeys, r.Hotkeys); // TD.30a: a tecla da caixa foi removida; arquivo antigo carrega sem erro
    }

    [Fact]
    public void Defaults_for_band_size_hud_and_focus_fade()
    {
        var td = AppSettings.Default.Td;
        Assert.Equal(64, td.BandHeightDip);
        Assert.True(td.HudMinimized); // TD.17: nasce recolhido
        Assert.True(td.FadeWhenUnfocused);
        Assert.Equal(50, td.FadeOpacityPercent);
        var old = ReadPayload("{\"td\":{\"autoStartEnabled\":true}}").Td; // JSON antigo: campos ausentes = padrão
        Assert.Equal(64, old.BandHeightDip);
        Assert.True(old.FadeWhenUnfocused);
    }

    [Fact]
    public void Onboarding_defaults_and_roundtrip_keep_tutorial_and_tips()
    {
        var td = AppSettings.Default.Td;
        Assert.False(td.TutorialDone);
        Assert.Equal("", td.TipsSeen);
        var old = ReadPayload("{\"td\":{\"autoStartEnabled\":true}}").Td; // JSON antigo: campos ausentes = padrão
        Assert.False(old.TutorialDone);
        Assert.Equal("", old.TipsSeen);
        var fs = new FakeFileSystem();
        var st = new SettingsStore(fs, "/data", utcNow: () => Now);
        Assert.True(st.Save(new AppSettings { Td = new TdSettings { TutorialDone = true, TipsSeen = "tower:bolt;enemy:runner" } }));
        var r = st.Load().Settings.Td;
        Assert.True(r.TutorialDone);
        Assert.Equal("tower:bolt;enemy:runner", r.TipsSeen);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip_keeps_band_height_hud_state_and_fade(bool minimized)
    {
        var fs = new FakeFileSystem();
        var st = new SettingsStore(fs, "/data", utcNow: () => Now);
        Assert.True(st.Save(new AppSettings { Td = new TdSettings { BandHeightDip = 88, HudMinimized = minimized, FadeWhenUnfocused = false, FadeOpacityPercent = 35 } }));
        var td = st.Load().Settings.Td;
        Assert.Equal(88, td.BandHeightDip);
        Assert.Equal(minimized, td.HudMinimized);
        Assert.False(td.FadeWhenUnfocused);
        Assert.Equal(35, td.FadeOpacityPercent);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(97)]
    public void Invalid_band_height_and_fade_fall_back_to_default(int value)
    {
        var td = ReadPayload("{\"td\":{\"bandHeightDip\":" + value + ",\"fadeOpacityPercent\":" + (value + 100) + "}}").Td;
        Assert.Equal(64, td.BandHeightDip);
        Assert.Equal(50, td.FadeOpacityPercent);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(61)]
    [InlineData(-1)]
    public void Invalid_autostart_seconds_fall_back_to_default(int seconds)
    {
        var s = ReadPayload("{\"td\":{\"autoStartEnabled\":true,\"autoStartSeconds\":" + seconds + "}}");
        Assert.True(s.Td.AutoStartEnabled);
        Assert.Equal(AppSettings.Default.Td.AutoStartSeconds, s.Td.AutoStartSeconds);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(60)]
    public void Autostart_bounds_are_accepted(int seconds) =>
        Assert.Equal(seconds, ReadPayload("{\"td\":{\"autoStartSeconds\":" + seconds + "}}").Td.AutoStartSeconds);

    [Fact]
    public void Unknown_game_mode_and_invalid_hotkey_fall_back()
    {
        var s = ReadPayload("""{"gameMode":"Banana","hotkeys":{"StartTdWave":"???"}}""");
        Assert.Equal(GameMode.Td, s.GameMode);
        Assert.Equal("Ctrl+Alt+Space", s.Hotkeys.StartWave);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Notice_size_defaults_and_round_trips(int size)
    {
        Assert.Equal(1, ReadPayload("{\"td\":{}}").Td.NoticeSize);
        Assert.Equal(1, ReadPayload("{\"td\":{\"noticeSize\":4}}").Td.NoticeSize);
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms)) SettingsSerializer.Write(w, new AppSettings { Td = new TdSettings { NoticeSize = size } });
        using var doc = JsonDocument.Parse(ms.ToArray());
        Assert.Equal(size, SettingsSerializer.Read(doc.RootElement).Td.NoticeSize);
    }

    [Fact] // TD.33d
    public void Money_options_default_off_survive_old_files_and_round_trip()
    {
        Assert.False(AppSettings.Default.Td.ShowIncome);
        Assert.False(AppSettings.Default.Td.DiscreetMode);
        var old = ReadPayload("{\"td\":{\"autoStartEnabled\":true}}").Td; // arquivo antigo: campos ausentes = padrão
        Assert.False(old.ShowIncome);
        Assert.False(old.DiscreetMode);
        var on = new AppSettings { Td = new TdSettings { ShowIncome = true, DiscreetMode = true } };
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms)) SettingsSerializer.Write(w, on);
        using var doc = JsonDocument.Parse(ms.ToArray());
        Assert.True(doc.RootElement.GetProperty("td").GetProperty("showIncome").GetBoolean());
        var back = SettingsSerializer.Read(doc.RootElement);
        Assert.True(back.Td.ShowIncome);
        Assert.True(back.Td.DiscreetMode);
    }

    [Fact] // TD.40b2
    public void Alert_muted_mask_defaults_to_zero_survives_old_files_and_round_trips()
    {
        Assert.Equal(0, ReadPayload("{\"td\":{\"alertsEnabled\":true}}").Td.AlertsMutedMask);
        var on = new AppSettings { Td = new TdSettings { AlertsMutedMask = 0b1010 } };
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms)) SettingsSerializer.Write(w, on);
        using var doc = JsonDocument.Parse(ms.ToArray());
        Assert.Equal(0b1010, SettingsSerializer.Read(doc.RootElement).Td.AlertsMutedMask);
    }
}
