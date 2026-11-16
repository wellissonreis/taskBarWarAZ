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
