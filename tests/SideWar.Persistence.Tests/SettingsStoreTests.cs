using Xunit;
using System.Text;
using System.Text.Json;
using SideWar.Persistence.Settings;

namespace SideWar.Persistence.Tests;

public class SettingsStoreTests
{
    private const string D = "/data";
    private const string F = "/data/settings.json";
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private static (FakeFileSystem fs, SettingsStore st) Make()
    {
        var fs = new FakeFileSystem();
        return (fs, new SettingsStore(fs, D, utcNow: () => Now));
    }

    private static byte[] Envelope(string payloadJson, string schema = "sidewar.settings", int version = 1, string? hash = null)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        hash ??= JsonEnvelope.ComputeHash(doc.RootElement);
        return Encoding.UTF8.GetBytes($$"""{"schema":"{{schema}}","schemaVersion":{{version}},"appVersion":"0.1.0","savedAtUtc":"2026-11-10T12:00:00Z","contentHash":"{{hash}}","payload":{{payloadJson}}}""");
    }

    [Fact]
    public void Load_NoFiles_ReturnsDefaults()
    {
        var (_, st) = Make();
        var r = st.Load();
        Assert.Equal(SettingsSource.Defaults, r.Source);
        Assert.Equal(AppSettings.Default, r.Settings);
        Assert.Equal(30, r.Settings.Performance.PresentFps);
        Assert.Equal(8, r.Settings.Performance.AbsentFps);
        Assert.True(r.Settings.AutoScale);
        Assert.True(r.Settings.Taskbar.UseUia);
    }

    [Fact]
    public void SaveLoad_RoundTrip_AllFields()
    {
        var (_, st) = Make();
        var s = new AppSettings
        {
            Dock = new DockSettings { MonitorDevice = "\\\\.\\DISPLAY2", BoundsHint = [0, 0, 1920, 1080], ReserveSpace = true },
            Opacity = new OpacitySettings { Peek = 90 },
            Behavior = new BehaviorSettings { HideOnFullscreen = HideOnFullscreenMode.Never, AutoPassiveSeconds = 12, PeekOnHover = false, StartWithWindows = true },
            AutoScale = false,
            Performance = new PerformanceSettings { PresentFps = 60, AbsentFps = 4, SaverOnBattery = false, ForceSaver = true },
            Accessibility = new AccessibilitySettings { ReducedMotion = true },
            Hotkeys = new HotkeySettings { ToggleInteractive = "Ctrl+Shift+I", ToggleVisibility = "Ctrl+Alt+V", TogglePause = "Ctrl+Alt+Q" },
            Audio = new AudioSettings { Enabled = true, Volume = 0.25 },
            Diagnostics = new DiagnosticsSettings { RecordCommands = true, LogLevel = LogLevelSetting.Debug },
            Taskbar = new TaskbarSettings { UseUia = false },
        };
        Assert.True(st.Save(s));
        var r = st.Load();
        Assert.Equal(SettingsSource.File, r.Source);
        Assert.Equal(s.Dock.MonitorDevice, r.Settings.Dock.MonitorDevice);
        Assert.Equal(s.Dock.BoundsHint, r.Settings.Dock.BoundsHint);
        Assert.Equal(s with { Dock = r.Settings.Dock }, r.Settings);
    }

    [Fact]
    public void Save_WritesEnvelope_WithSchemaVersionHashAndNoRemovedFields()
    {
        var (fs, st) = Make();
        st.Save(AppSettings.Default);
        using var doc = JsonDocument.Parse(fs.Files[F]);
        var root = doc.RootElement;
        Assert.Equal("sidewar.settings", root.GetProperty("schema").GetString());
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("2026-11-10T12:00:00Z", root.GetProperty("savedAtUtc").GetString());
        Assert.Equal(JsonEnvelope.ComputeHash(root.GetProperty("payload")), root.GetProperty("contentHash").GetString());
        var text = Encoding.UTF8.GetString(fs.Files[F]);
        foreach (var removed in new[] { "StartWave", "activeFps", "pauseWhenHidden", "\"background\"", "marginDip", "azureModule" })
            Assert.DoesNotContain(removed, text);
    }

    [Fact]
    public void T14_2_InvalidField_FallsBackToDefault_KeepsOthers()
    {
        var (fs, st) = Make();
        fs.Files[F] = Envelope("""
            {"performance":{"presentFps":45,"absentFps":4,"saverOnBattery":"yes"},
             "behavior":{"hideOnFullscreen":"Bogus","startWithWindows":true},
             "audio":{"volume":7},"opacity":{"peek":-3},
             "hotkeys":{"TogglePause":"???","ToggleVisibility":"Ctrl+Alt+K"},
             "accessibility":{"reducedMotion":true},"autoScale":false,"dock":{"boundsHint":[1,2,3]}}
            """);
        var r = st.Load();
        Assert.Equal(SettingsSource.File, r.Source);
        var s = r.Settings;
        Assert.Equal(30, s.Performance.PresentFps);
        Assert.Equal(4, s.Performance.AbsentFps);
        Assert.True(s.Performance.SaverOnBattery);
        Assert.Equal(HideOnFullscreenMode.SameMonitor, s.Behavior.HideOnFullscreen);
        Assert.True(s.Behavior.StartWithWindows);
        Assert.Equal(0.6, s.Audio.Volume);
        Assert.Equal(60, s.Opacity.Peek);
        Assert.Equal("Ctrl+Alt+P", s.Hotkeys.TogglePause);
        Assert.Equal("Ctrl+Alt+K", s.Hotkeys.ToggleVisibility);
        Assert.True(s.Accessibility.ReducedMotion);
        Assert.False(s.AutoScale);
        Assert.Null(s.Dock.BoundsHint);
        Assert.DoesNotContain(fs.Files.Keys, k => k.Contains("corrupt"));
    }

    [Fact]
    public void Load_AcceptsOnlyAllowedFps()
    {
        foreach (var (p, a) in new[] { (20, 4), (30, 8), (60, 8) })
        {
            var (fs, st) = Make();
            fs.Files[F] = Envelope("{\"performance\":{\"presentFps\":" + p + ",\"absentFps\":" + a + "}}");
            var s = st.Load().Settings;
            Assert.Equal(p, s.Performance.PresentFps);
            Assert.Equal(a, s.Performance.AbsentFps);
        }
    }

    public static TheoryData<string> CorruptCases() => new()
    {
        "truncated", "invalid", "future", "hash", "schema", "oversize", "nopayload",
    };

    private static byte[] CorruptBytes(string kind)
    {
        var ok = Encoding.UTF8.GetString(Envelope("""{"autoScale":false}"""));
        return kind switch
        {
            "truncated" => Encoding.UTF8.GetBytes(ok[..(ok.Length / 2)]),
            "invalid" => "not json at all"u8.ToArray(),
            "future" => Envelope("""{"autoScale":false}""", version: 2),
            "hash" => Envelope("""{"autoScale":false}""", hash: new string('0', 64)),
            "schema" => Envelope("""{"autoScale":false}""", schema: "sidewar.profile"),
            "oversize" => new byte[JsonEnvelope.MaxReadBytes + 1],
            _ => """{"schema":"sidewar.settings","schemaVersion":1,"contentHash":"x"}"""u8.ToArray(),
        };
    }

    [Theory]
    [MemberData(nameof(CorruptCases))]
    public void T14_3_Corrupt_MovedToCorrupt_AppStartsWithDefaults(string kind)
    {
        var (fs, st) = Make();
        fs.Files[F] = CorruptBytes(kind);
        var r = st.Load();
        Assert.Equal(SettingsSource.Defaults, r.Source);
        Assert.Equal(AppSettings.Default, r.Settings);
        Assert.False(fs.Files.ContainsKey(F));
        Assert.Contains("/data/corrupt/settings-20261110120000.json", fs.Files.Keys);
    }

    [Theory]
    [MemberData(nameof(CorruptCases))]
    public void T14_3_Corrupt_FallsBackToBak_WhenValid(string kind)
    {
        var (fs, st) = Make();
        fs.Files[F] = CorruptBytes(kind);
        fs.Files[F + ".bak"] = Envelope("""{"autoScale":false}""");
        var r = st.Load();
        Assert.Equal(SettingsSource.Backup, r.Source);
        Assert.False(r.Settings.AutoScale);
        Assert.Contains("/data/corrupt/settings-20261110120000.json", fs.Files.Keys);
    }

    [Fact]
    public void CorruptMainAndBak_BothQuarantined_Defaults()
    {
        var (fs, st) = Make();
        fs.Files[F] = CorruptBytes("invalid");
        fs.Files[F + ".bak"] = CorruptBytes("truncated");
        Assert.Equal(SettingsSource.Defaults, st.Load().Source);
        Assert.Equal(2, fs.Files.Keys.Count(k => k.StartsWith("/data/corrupt/", StringComparison.Ordinal)));
        Assert.Contains("/data/corrupt/settings.json.bak-20261110120000.json", fs.Files.Keys);
    }

    [Fact]
    public void MissingMain_ValidBak_IsUsed()
    {
        var (fs, st) = Make();
        fs.Files[F + ".bak"] = Envelope("""{"autoScale":false}""");
        var r = st.Load();
        Assert.Equal(SettingsSource.Backup, r.Source);
        Assert.False(r.Settings.AutoScale);
    }

    [Fact]
    public void Quarantine_KeepsAtMost10_DeletingOldest()
    {
        var (fs, st) = Make();
        for (var i = 0; i < 12; i++)
            fs.Files[$"/data/corrupt/settings-2026010100{i:D4}.json"] = [1];
        fs.Files[F] = CorruptBytes("invalid");
        st.Load();
        var kept = fs.Files.Keys.Where(k => k.StartsWith("/data/corrupt/", StringComparison.Ordinal)).ToList();
        Assert.Equal(10, kept.Count);
        Assert.Contains("/data/corrupt/settings-20261110120000.json", kept);
        Assert.DoesNotContain("/data/corrupt/settings-20260101000000.json", kept);
    }

    [Fact]
    public void Quarantine_SameSecondCollision_GetsSuffix()
    {
        var (fs, st) = Make();
        fs.Files["/data/corrupt/settings-20261110120000.json"] = [1];
        fs.Files[F] = CorruptBytes("invalid");
        st.Load();
        Assert.Contains("/data/corrupt/settings-20261110120000-1.json", fs.Files.Keys);
    }

    [Fact]
    public void Load_IoError_ReturnsDefaults_WithoutThrowing()
    {
        var (fs, st) = Make();
        fs.Files[F] = Envelope("{}");
        fs.Fault = (_, _) => { };
        var bad = new FaultyRead(fs);
        var st2 = new SettingsStore(bad, D);
        Assert.Equal(SettingsSource.Defaults, st2.Load().Source);
    }

    private sealed class FaultyRead(FakeFileSystem inner) : SideWar.Persistence.IO.IFileSystem
    {
        public bool FileExists(string p) => inner.FileExists(p);
        public void CreateDirectory(string p) => inner.CreateDirectory(p);
        public void WriteAllBytesDurable(string p, ReadOnlySpan<byte> d) => inner.WriteAllBytesDurable(p, d);
        public void Replace(string a, string b, string c) => inner.Replace(a, b, c);
        public void Move(string a, string b, bool o) => inner.Move(a, b, o);
        public void Copy(string a, string b, bool o) => inner.Copy(a, b, o);
        public void Delete(string p) => inner.Delete(p);
        public long GetLength(string p) => inner.GetLength(p);
        public byte[] ReadAllBytes(string p) => throw new IOException("locked");
        public IReadOnlyList<string> EnumerateFiles(string d) => inner.EnumerateFiles(d);
    }

    [Fact]
    public void Save_SecondSave_CreatesBak_AndFailureReturnsFalse()
    {
        var (fs, st) = Make();
        Assert.True(st.Save(AppSettings.Default));
        Assert.True(st.Save(AppSettings.Default with { AutoScale = false }));
        Assert.True(fs.Files.ContainsKey(F + ".bak"));
        fs.Fault = (op, _) => { if (op == "write") throw new IOException(); };
        var failing = new SettingsStore(fs, D, writer: new SideWar.Persistence.IO.AtomicFileWriter(fs, sleep: _ => { }));
        Assert.False(failing.Save(AppSettings.Default));
    }

    [Fact]
    public void RealFileSystem_EndToEnd_CorruptThenBak()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sw-t24s-" + Guid.NewGuid().ToString("N"));
        try
        {
            var st = new SettingsStore(SideWar.Persistence.IO.RealFileSystem.Instance, dir);
            Assert.True(st.Save(AppSettings.Default));
            Assert.True(st.Save(AppSettings.Default with { AutoScale = false }));
            File.WriteAllText(Path.Combine(dir, "settings.json"), "{ truncated");
            var r = st.Load();
            Assert.Equal(SettingsSource.Backup, r.Source);
            Assert.True(r.Settings.AutoScale);
            Assert.Single(Directory.GetFiles(Path.Combine(dir, "corrupt")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
