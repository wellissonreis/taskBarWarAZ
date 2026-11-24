using System.Text.Json;

namespace SideWar.Persistence.Settings;

/// <summary>Serialização manual (sem reflexão) e validação por campo: campo inválido volta ao padrão daquele campo.</summary>
public static class SettingsSerializer
{
    public static readonly int[] AllowedPresentFps = [20, 30, 60];
    public static readonly int[] AllowedAbsentFps = [4, 8];

    public static void Write(Utf8JsonWriter w, AppSettings s)
    {
        w.WriteStartObject();
        w.WriteStartObject("dock");
        if (s.Dock.MonitorDevice is { } dev) w.WriteString("monitorDevice", dev);
        if (s.Dock.BoundsHint is { Count: 4 } b)
        {
            w.WriteStartArray("boundsHint");
            foreach (var v in b) w.WriteNumberValue(v);
            w.WriteEndArray();
        }
        w.WriteBoolean("reserveSpace", s.Dock.ReserveSpace);
        w.WriteEndObject();
        w.WriteStartObject("opacity");
        w.WriteNumber("peek", s.Opacity.Peek);
        w.WriteBoolean("fadeWhenAbsent", s.Opacity.FadeWhenAbsent);
        w.WriteNumber("absent", s.Opacity.Absent);
        w.WriteEndObject();
        w.WriteStartObject("behavior");
        w.WriteString("hideOnFullscreen", s.Behavior.HideOnFullscreen.ToString());
        w.WriteNumber("autoPassiveSeconds", s.Behavior.AutoPassiveSeconds);
        w.WriteBoolean("peekOnHover", s.Behavior.PeekOnHover);
        w.WriteBoolean("startWithWindows", s.Behavior.StartWithWindows);
        w.WriteEndObject();
        w.WriteBoolean("autoScale", s.AutoScale);
        w.WriteStartObject("performance");
        w.WriteNumber("presentFps", s.Performance.PresentFps);
        w.WriteNumber("absentFps", s.Performance.AbsentFps);
        w.WriteBoolean("saverOnBattery", s.Performance.SaverOnBattery);
        w.WriteBoolean("forceSaver", s.Performance.ForceSaver);
        w.WriteEndObject();
        w.WriteStartObject("accessibility");
        w.WriteBoolean("reducedMotion", s.Accessibility.ReducedMotion);
        w.WriteEndObject();
        w.WriteStartObject("hotkeys");
        w.WriteString("ToggleInteractive", s.Hotkeys.ToggleInteractive);
        w.WriteString("ToggleVisibility", s.Hotkeys.ToggleVisibility);
        w.WriteString("TogglePause", s.Hotkeys.TogglePause);
        w.WriteString("StartTdWave", s.Hotkeys.StartWave);
        w.WriteEndObject();
        w.WriteStartObject("audio");
        w.WriteBoolean("enabled", s.Audio.Enabled);
        w.WriteNumber("volume", s.Audio.Volume);
        w.WriteEndObject();
        w.WriteStartObject("diagnostics");
        w.WriteBoolean("recordCommands", s.Diagnostics.RecordCommands);
        w.WriteString("logLevel", s.Diagnostics.LogLevel.ToString());
        w.WriteEndObject();
        w.WriteStartObject("taskbar");
        w.WriteBoolean("useUia", s.Taskbar.UseUia);
        w.WriteBoolean("towersInBar", s.Taskbar.TowersInBar);
        w.WriteEndObject();
        w.WriteString("gameMode", s.GameMode.ToString());
        w.WriteStartObject("td");
        w.WriteBoolean("alertsEnabled", s.Td.AlertsEnabled);
        w.WriteBoolean("alertsAutoPause", s.Td.AlertsAutoPause);
        w.WriteBoolean("alertsSound", s.Td.AlertsSound);
        w.WriteNumber("alertsMutedMask", s.Td.AlertsMutedMask);
        w.WriteBoolean("autoStartEnabled", s.Td.AutoStartEnabled);
        w.WriteBoolean("autoRestart", s.Td.AutoRestart);
        w.WriteNumber("autoStartSeconds", s.Td.AutoStartSeconds);
        w.WriteNumber("bandHeightDip", s.Td.BandHeightDip);
        w.WriteBoolean("hudMinimized", s.Td.HudMinimized);
        w.WriteNumber("cornerUiScalePercent", s.Td.CornerUiScalePercent);
        w.WriteBoolean("fadeWhenUnfocused", s.Td.FadeWhenUnfocused);
        w.WriteNumber("fadeOpacityPercent", s.Td.FadeOpacityPercent);
        w.WriteBoolean("tutorialDone", s.Td.TutorialDone);
        w.WriteString("tipsSeen", s.Td.TipsSeen);
        w.WriteBoolean("bonusQuestions", s.Td.BonusQuestions);
        w.WriteBoolean("continuousWaves", s.Td.ContinuousWaves);
        w.WriteBoolean("towerPanelPinned", s.Td.TowerPanelPinned);
        w.WriteNumber("gameSpeed", s.Td.GameSpeed);
        w.WriteString("questionsSeen", s.Td.QuestionsSeen);
        w.WriteBoolean("showIncome", s.Td.ShowIncome);
        w.WriteBoolean("discreetMode", s.Td.DiscreetMode);
        w.WriteNumber("noticeSize", s.Td.NoticeSize);
        w.WriteEndObject();
        w.WriteEndObject();
    }

    /// <summary>Lê o payload; campos ausentes ou inválidos assumem o padrão do campo. Nunca lança por conteúdo.</summary>
    public static AppSettings Read(JsonElement p)
    {
        var d = AppSettings.Default;
        var dock = Obj(p, "dock");
        var opacity = Obj(p, "opacity");
        var behavior = Obj(p, "behavior");
        var perf = Obj(p, "performance");
        var acc = Obj(p, "accessibility");
        var hot = Obj(p, "hotkeys");
        var audio = Obj(p, "audio");
        var diag = Obj(p, "diagnostics");
        var taskbar = Obj(p, "taskbar");
        var td = Obj(p, "td");
        return new AppSettings
        {
            Dock = new DockSettings
            {
                MonitorDevice = Str(dock, "monitorDevice", 256) ?? d.Dock.MonitorDevice,
                BoundsHint = Bounds(dock, "boundsHint"),
                ReserveSpace = Bool(dock, "reserveSpace", d.Dock.ReserveSpace),
            },
            Opacity = new OpacitySettings
            {
                Peek = IntRange(opacity, "peek", 0, 255, d.Opacity.Peek),
                FadeWhenAbsent = Bool(opacity, "fadeWhenAbsent", d.Opacity.FadeWhenAbsent),
                Absent = IntRange(opacity, "absent", 0, 255, d.Opacity.Absent),
            },
            Behavior = new BehaviorSettings
            {
                HideOnFullscreen = EnumVal(behavior, "hideOnFullscreen", d.Behavior.HideOnFullscreen),
                AutoPassiveSeconds = IntRange(behavior, "autoPassiveSeconds", 1, 600, d.Behavior.AutoPassiveSeconds),
                PeekOnHover = Bool(behavior, "peekOnHover", d.Behavior.PeekOnHover),
                StartWithWindows = Bool(behavior, "startWithWindows", d.Behavior.StartWithWindows),
            },
            AutoScale = Bool(p, "autoScale", d.AutoScale),
            Performance = new PerformanceSettings
            {
                PresentFps = IntOneOf(perf, "presentFps", AllowedPresentFps, d.Performance.PresentFps),
                AbsentFps = IntOneOf(perf, "absentFps", AllowedAbsentFps, d.Performance.AbsentFps),
                SaverOnBattery = Bool(perf, "saverOnBattery", d.Performance.SaverOnBattery),
                ForceSaver = Bool(perf, "forceSaver", d.Performance.ForceSaver),
            },
            Accessibility = new AccessibilitySettings { ReducedMotion = Bool(acc, "reducedMotion", d.Accessibility.ReducedMotion) },
            Hotkeys = new HotkeySettings
            {
                ToggleInteractive = Hotkey(hot, "ToggleInteractive", d.Hotkeys.ToggleInteractive),
                ToggleVisibility = Hotkey(hot, "ToggleVisibility", d.Hotkeys.ToggleVisibility),
                TogglePause = Hotkey(hot, "TogglePause", d.Hotkeys.TogglePause),
                StartWave = Hotkey(hot, "StartTdWave", d.Hotkeys.StartWave),
            },
            Audio = new AudioSettings
            {
                Enabled = Bool(audio, "enabled", d.Audio.Enabled),
                Volume = Volume(audio, "volume", d.Audio.Volume),
            },
            Diagnostics = new DiagnosticsSettings
            {
                RecordCommands = Bool(diag, "recordCommands", d.Diagnostics.RecordCommands),
                LogLevel = EnumVal(diag, "logLevel", d.Diagnostics.LogLevel),
            },
            Taskbar = new TaskbarSettings { UseUia = Bool(taskbar, "useUia", d.Taskbar.UseUia), TowersInBar = Bool(taskbar, "towersInBar", d.Taskbar.TowersInBar) },
            GameMode = EnumVal(p, "gameMode", d.GameMode),
            Td = new TdSettings
            {
                AlertsEnabled = Bool(td, "alertsEnabled", d.Td.AlertsEnabled),
                AlertsAutoPause = Bool(td, "alertsAutoPause", d.Td.AlertsAutoPause),
                AlertsSound = Bool(td, "alertsSound", d.Td.AlertsSound),
                AlertsMutedMask = IntRange(td, "alertsMutedMask", 0, 0x1FFF, d.Td.AlertsMutedMask),
                AutoStartEnabled = Bool(td, "autoStartEnabled", d.Td.AutoStartEnabled),
                AutoRestart = Bool(td, "autoRestart", d.Td.AutoRestart),
                AutoStartSeconds = IntRange(td, "autoStartSeconds", TdSettings.MinAutoStartSeconds, TdSettings.MaxAutoStartSeconds, d.Td.AutoStartSeconds),
                BandHeightDip = IntRange(td, "bandHeightDip", TdSettings.MinBandHeightDip, TdSettings.MaxBandHeightDip, d.Td.BandHeightDip),
                HudMinimized = Bool(td, "hudMinimized", d.Td.HudMinimized),
                CornerUiScalePercent = IntClamped(td, "cornerUiScalePercent", TdSettings.MinCornerUiScalePercent, TdSettings.MaxCornerUiScalePercent, d.Td.CornerUiScalePercent),
                FadeWhenUnfocused = Bool(td, "fadeWhenUnfocused", d.Td.FadeWhenUnfocused),
                FadeOpacityPercent = IntRange(td, "fadeOpacityPercent", TdSettings.MinFadePercent, TdSettings.MaxFadePercent, d.Td.FadeOpacityPercent),
                TutorialDone = Bool(td, "tutorialDone", d.Td.TutorialDone),
                TipsSeen = Str(td, "tipsSeen", 1024) ?? d.Td.TipsSeen,
                BonusQuestions = Bool(td, "bonusQuestions", d.Td.BonusQuestions),
                ContinuousWaves = Bool(td, "continuousWaves", d.Td.ContinuousWaves),
                TowerPanelPinned = Bool(td, "towerPanelPinned", d.Td.TowerPanelPinned),
                GameSpeed = IntRange(td, "gameSpeed", TdSettings.MinGameSpeed, TdSettings.MaxGameSpeed, d.Td.GameSpeed),
                QuestionsSeen = Str(td, "questionsSeen", 1024) ?? d.Td.QuestionsSeen,
                ShowIncome = Bool(td, "showIncome", d.Td.ShowIncome),
                DiscreetMode = Bool(td, "discreetMode", d.Td.DiscreetMode),
                NoticeSize = IntRange(td, "noticeSize", 0, 3, d.Td.NoticeSize),
            },
        };
    }

    private static JsonElement? Obj(JsonElement? parent, string name) =>
        parent is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Object ? e : null;

    private static JsonElement? Prop(JsonElement? parent, string name) =>
        parent is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(name, out var e) ? e : null;

    private static bool Bool(JsonElement? parent, string name, bool def) =>
        Prop(parent, name) is { } e && e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : def;

    private static int IntRange(JsonElement? parent, string name, int min, int max, int def) =>
        Prop(parent, name) is { ValueKind: JsonValueKind.Number } e && e.TryGetInt32(out var v) && v >= min && v <= max ? v : def;

    private static int IntClamped(JsonElement? parent, string name, int min, int max, int def) =>
        Prop(parent, name) is { ValueKind: JsonValueKind.Number } e && e.TryGetInt32(out var v) ? Math.Clamp(v, min, max) : def;

    private static int IntOneOf(JsonElement? parent, string name, int[] allowed, int def) =>
        Prop(parent, name) is { ValueKind: JsonValueKind.Number } e && e.TryGetInt32(out var v) && Array.IndexOf(allowed, v) >= 0 ? v : def;

    private static double Volume(JsonElement? parent, string name, double def) =>
        Prop(parent, name) is { ValueKind: JsonValueKind.Number } e && e.TryGetDouble(out var v) && v is >= 0 and <= 1 ? v : def;

    private static string? Str(JsonElement? parent, string name, int maxLen) =>
        Prop(parent, name) is { ValueKind: JsonValueKind.String } e && e.GetString() is { Length: > 0 } s && s.Length <= maxLen ? s : null;

    private static T EnumVal<T>(JsonElement? parent, string name, T def) where T : struct, Enum =>
        Prop(parent, name) is { ValueKind: JsonValueKind.String } e && Enum.TryParse<T>(e.GetString(), ignoreCase: false, out var v)
            && Enum.IsDefined(v) ? v : def;

    private static int[]? Bounds(JsonElement? parent, string name)
    {
        if (Prop(parent, name) is not { ValueKind: JsonValueKind.Array } a || a.GetArrayLength() != 4) return null;
        var r = new int[4];
        var i = 0;
        foreach (var x in a.EnumerateArray())
        {
            if (x.ValueKind != JsonValueKind.Number || !x.TryGetInt32(out r[i++])) return null;
        }
        return r[2] > r[0] && r[3] > r[1] ? r : null;
    }

    /// <summary>Formato "Mod+Mod+Tecla": 2 a 5 tokens alfanuméricos, até 64 caracteres. A resolução real do atalho é da plataforma.</summary>
    internal static bool IsValidHotkey(string s)
    {
        if (s.Length is 0 or > 64) return false;
        var parts = s.Split('+');
        if (parts.Length is < 2 or > 5) return false;
        foreach (var t in parts)
        {
            if (t.Length == 0) return false;
            foreach (var c in t) if (!char.IsAsciiLetterOrDigit(c)) return false;
        }
        return true;
    }

    private static string Hotkey(JsonElement? parent, string name, string def) =>
        Prop(parent, name) is { ValueKind: JsonValueKind.String } e && e.GetString() is { } s && IsValidHotkey(s) ? s : def;
}
