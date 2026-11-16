namespace SideWar.Persistence.Settings;

public enum HideOnFullscreenMode { SameMonitor, AnyMonitor, Never }

public enum LogLevelSetting { Debug, Info, Warning, Error }

/// <summary>Jogo hospedado pelo App. <c>Idle</c> é o padrão; <c>Td</c> é experimental/oculto até o render TD (TD.7).</summary>
public enum GameMode { Idle, Td }

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

    /// <summary>Setting oculto/experimental (sem UI): seleciona o host TD no lugar do idle. Padrão Idle.</summary>
    public GameMode GameMode { get; init; } = GameMode.Idle;

    public TdSettings Td { get; init; } = new();

    public static AppSettings Default { get; } = new();
}

public sealed record DockSettings
{
    public string? MonitorDevice { get; init; }
    /// <summary>left, top, right, bottom (px do monitor); null se ausente/ inválido.</summary>
    public IReadOnlyList<int>? BoundsHint { get; init; }
    public bool ReserveSpace { get; init; }
}

/// <summary>
/// Opacidade da faixa (0-255). Por padrão nunca esmaece sozinha: <see cref="Peek"/> só vale com Behavior.PeekOnHover
/// e <see cref="Absent"/> só vale com <see cref="FadeWhenAbsent"/>.
/// </summary>
public sealed record OpacitySettings
{
    public int Peek { get; init; } = 60;
    public bool FadeWhenAbsent { get; init; }
    public int Absent { get; init; } = 128;
}

public sealed record BehaviorSettings
{
    public HideOnFullscreenMode HideOnFullscreen { get; init; } = HideOnFullscreenMode.SameMonitor;
    public int AutoPassiveSeconds { get; init; } = 8;
    public bool PeekOnHover { get; init; }
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
    /// <summary>Iniciar onda / chamar a próxima (só registrado no modo TD).</summary>
    public string StartWave { get; init; } = "Ctrl+Alt+Space";
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

/// <summary>Opções da partida TD (SDD-28 RF-28.3). Auto-início desligado por padrão; contagem de 5 a 60 s.</summary>
public sealed record TdSettings
{
    public const int MinAutoStartSeconds = 5;
    public const int MaxAutoStartSeconds = 60;

    public bool AutoStartEnabled { get; init; }
    public int AutoStartSeconds { get; init; } = 15;
}
