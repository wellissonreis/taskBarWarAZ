namespace SideWar.Persistence.Settings;

public enum HideOnFullscreenMode { SameMonitor, AnyMonitor, Never }

public enum LogLevelSetting { Debug, Info, Warning, Error }

/// <summary>
/// Payload de settings.json (SDD-14 RF-14.4 ajustado por ADR-020/028/031). Valores padrão = padrões do campo.
/// Não inclui o que não é editável (limiares de presença, teto offline, multiplicadores) nem StartWave/2x.
/// </summary>
public sealed record AppSettings
{
    public const string Schema = "sidewar.settings";
    public const int SchemaVersion = 1;

    public DockSettings Dock { get; init; } = new();
    public OpacitySettings Opacity { get; init; } = new();
    public BehaviorSettings Behavior { get; init; } = new();
    public bool AutoScale { get; init; } = true;
    public PerformanceSettings Performance { get; init; } = new();
    public AccessibilitySettings Accessibility { get; init; } = new();
    public HotkeySettings Hotkeys { get; init; } = new();
    public AudioSettings Audio { get; init; } = new();
    public DiagnosticsSettings Diagnostics { get; init; } = new();
    public TaskbarSettings Taskbar { get; init; } = new();

    public static AppSettings Default { get; } = new();
}

public sealed record DockSettings
{
    public string? MonitorDevice { get; init; }
    /// <summary>left, top, right, bottom (px do monitor); null se ausente/ inválido.</summary>
    public IReadOnlyList<int>? BoundsHint { get; init; }
    public bool ReserveSpace { get; init; }
}

public sealed record OpacitySettings { public int Peek { get; init; } = 60; }

public sealed record BehaviorSettings
{
    public HideOnFullscreenMode HideOnFullscreen { get; init; } = HideOnFullscreenMode.SameMonitor;
    public int AutoPassiveSeconds { get; init; } = 8;
    public bool PeekOnHover { get; init; } = true;
    public bool StartWithWindows { get; init; }
}

public sealed record PerformanceSettings
{
    public int PresentFps { get; init; } = 30;
    public int AbsentFps { get; init; } = 8;
    public bool SaverOnBattery { get; init; } = true;
    public bool ForceSaver { get; init; }
}

public sealed record AccessibilitySettings { public bool ReducedMotion { get; init; } }

public sealed record HotkeySettings
{
    public string ToggleInteractive { get; init; } = "Ctrl+Alt+G";
    public string ToggleVisibility { get; init; } = "Ctrl+Alt+H";
    public string TogglePause { get; init; } = "Ctrl+Alt+P";
}

public sealed record AudioSettings
{
    public bool Enabled { get; init; }
    public double Volume { get; init; } = 0.6;
}

public sealed record DiagnosticsSettings
{
    public bool RecordCommands { get; init; }
    public LogLevelSetting LogLevel { get; init; } = LogLevelSetting.Info;
}

public sealed record TaskbarSettings
{
    public bool UseUia { get; init; } = true;

    /// <summary>Towers inside the bar (ADR-034); off by default until the TA.1 spike validates z-order over the taskbar.</summary>
    public bool TowersInBar { get; init; }
}
