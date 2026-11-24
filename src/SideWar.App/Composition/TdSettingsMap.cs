using SideWar.Persistence.Settings;
using SideWar.Runtime.Td;

namespace SideWar.App.Composition;

/// <summary>Estado de "Iniciar com o Windows" consultado na T-UI (só existe empacotado).</summary>
internal readonly record struct StartupState(bool Visible, bool Available, bool Enabled, string? Message);

/// <summary>Ponte entre o <c>AppSettings</c> (Persistência) e o <see cref="TdSettingsData"/> do menu Configurações (Runtime), nos mesmos campos de antes.</summary>
internal static class TdSettingsMap
{
    public static TdSettingsData From(AppSettings s, StartupState startup) => new()
    {
        GameMode = s.GameMode == GameMode.Td ? 1 : 0,
        StartWithWindows = startup.Visible ? startup.Enabled : s.Behavior.StartWithWindows,
        StartupVisible = startup.Visible, StartupAvailable = startup.Available, StartupMessage = startup.Message,
        HideOnFullscreen = (int)s.Behavior.HideOnFullscreen,
        UseUia = s.Taskbar.UseUia, AutoScale = s.AutoScale,
        BonusQuestions = s.Td.BonusQuestions, ShowIncome = s.Td.ShowIncome, DiscreetMode = s.Td.DiscreetMode,
        AlertsEnabled = s.Td.AlertsEnabled, AlertsAutoPause = s.Td.AlertsAutoPause, AlertsSound = s.Td.AlertsSound, AlertsMutedMask = s.Td.AlertsMutedMask,
        ContinuousWaves = s.Td.ContinuousWaves, TowerPanelPinned = s.Td.TowerPanelPinned,
        AutoStart = s.Td.AutoStartEnabled, AutoStartMode = s.Td.AutoStartMode, AutoRestart = s.Td.AutoRestart, AutoStartSeconds = s.Td.AutoStartSeconds,
        BandHeight = s.Td.BandHeightDip, CornerUiScale = s.Td.CornerUiScalePercent, GameSpeed = s.Td.GameSpeed, ExtraSpeeds = s.Td.ExtraSpeeds, ShadowOnAbsent = s.Td.ShadowOnAbsent, ShowTypeInfo = s.Td.ShowTypeInfo, SpeedMask = s.Td.ExtraSpeedMask, NoticeSize = s.Td.NoticeSize,
        Sound = s.Audio.Enabled, Volume = (int)Math.Round(s.Audio.Volume * 100),
        PeekOnHover = s.Behavior.PeekOnHover, PeekOpacity = s.Opacity.Peek, FadeWhenAbsent = s.Opacity.FadeWhenAbsent, AbsentOpacity = s.Opacity.Absent,
        FadeWhenUnfocused = s.Td.FadeWhenUnfocused, FadePercent = s.Td.FadeOpacityPercent,
        PresentFps = s.Performance.PresentFps, AbsentFps = s.Performance.AbsentFps, SaverOnBattery = s.Performance.SaverOnBattery, ForceSaver = s.Performance.ForceSaver,
        ReducedMotion = s.Accessibility.ReducedMotion,
        RecordCommands = s.Diagnostics.RecordCommands, LogLevel = (int)s.Diagnostics.LogLevel,
        ToggleInteractive = s.Hotkeys.ToggleInteractive, ToggleVisibility = s.Hotkeys.ToggleVisibility, TogglePause = s.Hotkeys.TogglePause, StartWave = s.Hotkeys.StartWave, CycleSpeed = s.Hotkeys.CycleSpeed, ToggleShadow = s.Hotkeys.ToggleShadow,
    };

    /// <summary>Aplica os campos editados sobre as configurações atuais (o que o menu não edita é preservado).</summary>
    public static AppSettings Apply(AppSettings s, TdSettingsData d) => s with
    {
        GameMode = d.GameMode == 1 ? GameMode.Td : GameMode.Idle,
        AutoScale = d.AutoScale,
        Opacity = s.Opacity with { Peek = Math.Clamp(d.PeekOpacity, 0, 255), FadeWhenAbsent = d.FadeWhenAbsent, Absent = Math.Clamp(d.AbsentOpacity, 0, 255) },
        Behavior = s.Behavior with { HideOnFullscreen = (HideOnFullscreenMode)Math.Clamp(d.HideOnFullscreen, 0, 2), PeekOnHover = d.PeekOnHover },
        Performance = s.Performance with { PresentFps = d.PresentFps, AbsentFps = d.AbsentFps, SaverOnBattery = d.SaverOnBattery, ForceSaver = d.ForceSaver },
        Accessibility = new AccessibilitySettings { ReducedMotion = d.ReducedMotion },
        Hotkeys = new HotkeySettings { ToggleInteractive = d.ToggleInteractive, ToggleVisibility = d.ToggleVisibility, TogglePause = d.TogglePause, StartWave = d.StartWave, CycleSpeed = d.CycleSpeed, ToggleShadow = d.ToggleShadow },
        Audio = new AudioSettings { Enabled = d.Sound, Volume = Math.Clamp(d.Volume, 0, 100) / 100.0 },
        Diagnostics = new DiagnosticsSettings { RecordCommands = d.RecordCommands, LogLevel = (LogLevelSetting)Math.Clamp(d.LogLevel, 0, 3) },
        Taskbar = s.Taskbar with { UseUia = d.UseUia },
        Td = s.Td with
        {
            AutoStartMode = d.AutoStartMode, AutoRestart = d.AutoRestart, AutoStartSeconds = Math.Clamp(d.AutoStartSeconds, TdSettings.MinAutoStartSeconds, TdSettings.MaxAutoStartSeconds),
            BandHeightDip = Math.Clamp(d.BandHeight, TdSettings.MinBandHeightDip, TdSettings.MaxBandHeightDip),
            CornerUiScalePercent = Math.Clamp(d.CornerUiScale, TdSettings.MinCornerUiScalePercent, TdSettings.MaxCornerUiScalePercent),
            FadeWhenUnfocused = d.FadeWhenUnfocused, FadeOpacityPercent = Math.Clamp(d.FadePercent, TdSettings.MinFadePercent, TdSettings.MaxFadePercent),
            BonusQuestions = d.BonusQuestions, ContinuousWaves = d.ContinuousWaves, TowerPanelPinned = d.TowerPanelPinned,
            GameSpeed = Math.Clamp(d.GameSpeed, TdSettings.MinGameSpeed, TdSettings.MaxGameSpeed), ExtraSpeeds = d.ExtraSpeeds, ShadowOnAbsent = d.ShadowOnAbsent, ShowTypeInfo = d.ShowTypeInfo, ExtraSpeedMask = d.SpeedMask & 0xFF, ShowIncome = d.ShowIncome, DiscreetMode = d.DiscreetMode, NoticeSize = Math.Clamp(d.NoticeSize, 0, 3),
            AlertsEnabled = d.AlertsEnabled, AlertsAutoPause = d.AlertsAutoPause, AlertsSound = d.AlertsSound, AlertsMutedMask = d.AlertsMutedMask,
        },
    };
}
