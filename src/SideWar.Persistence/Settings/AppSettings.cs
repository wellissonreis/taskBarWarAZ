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

    /// <summary>Modo de jogo (Configurações): Td (padrão desde a TD.8) ou Idle (clássico, até a TD.12).</summary>
    public GameMode GameMode { get; init; } = GameMode.Td;

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
    /// <summary>Exibe alertas preditivos; independente da proteção automática.</summary>
    public bool AlertsEnabled { get; init; } = true;
    /// <summary>Pausa diante de alertas críticos.</summary>
    public bool AlertsAutoPause { get; init; } = true;
    /// <summary>Som opcional dos alertas.</summary>
    public bool AlertsSound { get; init; }
    /// <summary>Tipos de alerta (índice do <c>TdAlertType</c> = bit) em que o usuário escolheu "não pausar por este tipo".</summary>
    public int AlertsMutedMask { get; init; }

    public const int MinAutoStartSeconds = 5;
    public const int MaxAutoStartSeconds = 60;
    public const int AutoRestartSeconds = 8;
    public bool AutoRestart { get; init; }

    public bool AutoStartEnabled { get; init; }
    public int AutoStartSeconds { get; init; } = 15;

    public const int MinBandHeightDip = 32;
    public const int MaxBandHeightDip = 96;
    public const int DefaultBandHeightDip = 64;
    public const int SmallBandHeightDip = 40;
    public const int LargeBandHeightDip = 88;
    public const int MinFadePercent = 10;
    public const int MaxFadePercent = 90;

    /// <summary>Altura da faixa TD em DIP (32 a 96; presets Pequeno 40, Médio 64, Grande 88). Aplica NA HORA (TD.28c: recria cena, layout, hit-test e janelas sem reiniciar).</summary>
    public int BandHeightDip { get; init; } = DefaultBandHeightDip;

    /// <summary>HUD recolhido a um chip no canto da faixa (lembrado entre sessões). TD.17: nasce recolhido na 1ª execução (o tutorial ensina a abrir).</summary>
    public bool HudMinimized { get; init; } = true;

    /// <summary>Esmaecer faixa e HUD quando o usuário ativa outro app (ausente: só o esmaecer; o fps não muda, TD.28c).</summary>
    public bool FadeWhenUnfocused { get; init; } = true;

    /// <summary>Opacidade (10 a 90 %) da faixa e do HUD enquanto esmaecidos.</summary>
    public int FadeOpacityPercent { get; init; } = 50;

    /// <summary>
    /// Tutorial da 1ª partida concluído ou pulado (TD.15). Decisão: fica nas configurações (não no perfil TD) por ser preferência de
    /// onboarding do aparelho, no mesmo caminho do <see cref="HudMinimized"/> (sem migração de schema do perfil nem mudança do ContentHash).
    /// </summary>
    public bool TutorialDone { get; init; }

    /// <summary>Dicas de 1ª vez já mostradas (ids <c>tower:bolt</c>, <c>enemy:runner</c>…), separadas por ';'. String única para a igualdade de records.</summary>
    public string TipsSeen { get; init; } = "";

    /// <summary>Pergunta-bônus Verdadeiro/Falso no Preparo (TD.17, RF-28.23); ligada por padrão.</summary>
    public bool BonusQuestions { get; init; } = true;

    /// <summary>TD.28b: ondas contínuas (corrida infinita, sem Preparo entre ondas e fases). Desligado = modo clássico (Preparo entre ondas, uma fase por partida). Vale a partir da próxima corrida.</summary>
    public bool ContinuousWaves { get; init; } = true;

    /// <summary>TD.28b: painel rápido de torres (botão ≡ do chip) fica aberto o tempo todo junto ao chip; fecha ao desligar ou ao minimizar pelo atalho.</summary>
    public bool TowerPanelPinned { get; init; }

    public const int MinGameSpeed = 1;
    public const int MaxGameSpeed = 3;

    /// <summary>TD.28c: velocidade do jogo (1x, 2x ou 3x): o host executa 1/2/3 Steps por tick de 30 Hz (determinístico: o replay não muda). Não muda com a pausa da modal.</summary>
    public int GameSpeed { get; init; } = MinGameSpeed;

    /// <summary>Ids das afirmações do banco já sorteadas (<c>q01;q02</c>…): não repetem até esgotar o banco (TD.17).</summary>
    public string QuestionsSeen { get; init; } = "";

    /// <summary>TD.33d (RF-33.20): mostra a renda "+X/s" (média de ~10 s de jogo) abaixo do contador de créditos. Desligado por padrão.</summary>
    public bool ShowIncome { get; init; }

    /// <summary>TD.33d (RF-33.21): modo discreto, sem brilhos nem contagem animada; "+N" só nos ganhos grandes. Desligado por padrão.</summary>
    public bool DiscreetMode { get; init; }
}
