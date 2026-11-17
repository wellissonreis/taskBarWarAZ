using System.Text;
using System.Text.Json;
using SideWar.Persistence.Settings;
using Xunit;

namespace SideWar.Persistence.Tests;

/// <summary>TD.6: gameMode (oculto, padrão idle), auto-início (padrão desligado, 5–60 s) e atalho Iniciar onda.</summary>
public class SettingsTdTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static AppSettings ReadPayload(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return SettingsSerializer.Read(doc.RootElement);
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
    public void Defaults_for_band_size_hud_and_focus_fade()
    {
        var td = AppSettings.Default.Td;
        Assert.Equal(64, td.BandHeightDip);
        Assert.False(td.HudMinimized);
        Assert.True(td.FadeWhenUnfocused);
        Assert.Equal(50, td.FadeOpacityPercent);
        var old = ReadPayload("{\"td\":{\"autoStartEnabled\":true}}").Td; // JSON antigo: campos ausentes = padrão
        Assert.Equal(64, old.BandHeightDip);
        Assert.True(old.FadeWhenUnfocused);
    }

    [Fact]
    public void RoundTrip_keeps_band_height_hud_state_and_fade()
    {
        var fs = new FakeFileSystem();
        var st = new SettingsStore(fs, "/data", utcNow: () => Now);
        Assert.True(st.Save(new AppSettings { Td = new TdSettings { BandHeightDip = 88, HudMinimized = true, FadeWhenUnfocused = false, FadeOpacityPercent = 35 } }));
        var td = st.Load().Settings.Td;
        Assert.Equal(88, td.BandHeightDip);
        Assert.True(td.HudMinimized);
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
}
