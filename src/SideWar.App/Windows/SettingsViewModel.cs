using System.ComponentModel;
using System.Runtime.CompilerServices;
using SideWar.Persistence.Settings;
using SideWar.Runtime.Input;

namespace SideWar.App.Windows;

/// <summary>
/// View-model puro da SettingsWindow (ADR-031): campos editáveis de <see cref="AppSettings"/> com validação.
/// Só edita o que a ficha lista; o restante do AppSettings original é preservado em <see cref="ToSettings"/>.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    public static readonly IReadOnlyList<int> PresentFpsChoices = [20, 30, 60];
    public static readonly IReadOnlyList<int> AbsentFpsChoices = [4, 8];

    // Constantes exibidas, não editáveis (SDD-25).
    public const string PresenceInfo = "Ausência após 5 min de inatividade (faixas de 15 e 60 min); card offline a partir de 10 min; teto offline de 12 h.";

    private readonly AppSettings original;
    private readonly Func<AppSettings, bool> save;
    private readonly Dictionary<string, string?> errors = [];

    private bool startWithWindows, autoScale, reducedMotion, saverOnBattery, forceSaver, useUia, peekOnHover, audioEnabled, recordCommands;
    private HideOnFullscreenMode hideOnFullscreen;
    private int presentFps, absentFps, peekOpacity, autoPassiveSeconds, volumePercent;
    private LogLevelSetting logLevel;
    private string toggleInteractive = "", toggleVisibility = "", togglePause = "";
    private string? saveError;

    public SettingsViewModel(AppSettings current, Func<AppSettings, bool> save)
    {
        original = current;
        this.save = save;
        Load(current);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool StartWithWindows { get => startWithWindows; set => Set(ref startWithWindows, value); }
    public HideOnFullscreenMode HideOnFullscreen { get => hideOnFullscreen; set => Set(ref hideOnFullscreen, value); }
    public bool UseUia { get => useUia; set => Set(ref useUia, value); }
    public bool AutoScale { get => autoScale; set => Set(ref autoScale, value); }
    public bool PeekOnHover { get => peekOnHover; set => Set(ref peekOnHover, value); }
    public int PeekOpacity { get => peekOpacity; set { if (Set(ref peekOpacity, value)) Check(nameof(PeekOpacity), value is >= 0 and <= 255 ? null : "Opacidade entre 0 e 255."); } }
    public int AutoPassiveSeconds { get => autoPassiveSeconds; set { if (Set(ref autoPassiveSeconds, value)) Check(nameof(AutoPassiveSeconds), value is >= 1 and <= 300 ? null : "Entre 1 e 300 segundos."); } }
    public int PresentFps { get => presentFps; set { if (Set(ref presentFps, value)) Check(nameof(PresentFps), PresentFpsChoices.Contains(value) ? null : "Use 20, 30 ou 60."); } }
    public int AbsentFps { get => absentFps; set { if (Set(ref absentFps, value)) Check(nameof(AbsentFps), AbsentFpsChoices.Contains(value) ? null : "Use 4 ou 8."); } }
    public bool SaverOnBattery { get => saverOnBattery; set => Set(ref saverOnBattery, value); }
    public bool ForceSaver { get => forceSaver; set => Set(ref forceSaver, value); }
    public bool ReducedMotion { get => reducedMotion; set => Set(ref reducedMotion, value); }
    public bool AudioEnabled { get => audioEnabled; set => Set(ref audioEnabled, value); }
    public int VolumePercent { get => volumePercent; set { if (Set(ref volumePercent, value)) Check(nameof(VolumePercent), value is >= 0 and <= 100 ? null : "Volume entre 0 e 100."); } }
    public bool RecordCommands { get => recordCommands; set => Set(ref recordCommands, value); }
    public LogLevelSetting LogLevel { get => logLevel; set => Set(ref logLevel, value); }
    public string ToggleInteractive { get => toggleInteractive; set { if (Set(ref toggleInteractive, value)) Check(nameof(ToggleInteractive), ValidateHotkey(value)); } }
    public string ToggleVisibility { get => toggleVisibility; set { if (Set(ref toggleVisibility, value)) Check(nameof(ToggleVisibility), ValidateHotkey(value)); } }
    public string TogglePause { get => togglePause; set { if (Set(ref togglePause, value)) Check(nameof(TogglePause), ValidateHotkey(value)); } }

    /// <summary>Erro de gravação da última tentativa de Salvar (null se ok).</summary>
    public string? SaveError { get => saveError; private set { if (saveError != value) { saveError = value; Raise(); } } }

    public bool IsValid => errors.Values.All(e => e is null) && AllHotkeysDistinct;
    public bool AllHotkeysDistinct => HotkeyConflict is null;

    /// <summary>Mensagem se dois atalhos forem a mesma combinação.</summary>
    public string? HotkeyConflict
    {
        get
        {
            string?[] vals = [Canon(toggleInteractive), Canon(toggleVisibility), Canon(togglePause)];
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var v in vals) if (v is not null && !set.Add(v)) return "Dois atalhos usam a mesma combinação.";
            return null;
        }
    }

    public string? GetError(string property) => errors.GetValueOrDefault(property);

    public bool Dirty => !ToSettings().Equals(original);

    /// <summary>Restaura os atalhos para os padrões (RF-15.4 "Restaurar padrão").</summary>
    public void RestoreDefaultHotkeys()
    {
        var d = new HotkeySettings();
        ToggleInteractive = d.ToggleInteractive;
        ToggleVisibility = d.ToggleVisibility;
        TogglePause = d.TogglePause;
    }

    /// <summary>Valida tudo e devolve os settings editados (preservando o que a janela não edita).</summary>
    public AppSettings ToSettings() => original with
    {
        AutoScale = autoScale,
        Dock = original.Dock,
        Opacity = original.Opacity with { Peek = peekOpacity },
        Behavior = original.Behavior with
        {
            HideOnFullscreen = hideOnFullscreen,
            StartWithWindows = startWithWindows,
            PeekOnHover = peekOnHover,
            AutoPassiveSeconds = autoPassiveSeconds,
        },
        Performance = original.Performance with { PresentFps = presentFps, AbsentFps = absentFps, SaverOnBattery = saverOnBattery, ForceSaver = forceSaver },
        Accessibility = new AccessibilitySettings { ReducedMotion = reducedMotion },
        Hotkeys = new HotkeySettings
        {
            ToggleInteractive = Canon(toggleInteractive) ?? toggleInteractive,
            ToggleVisibility = Canon(toggleVisibility) ?? toggleVisibility,
            TogglePause = Canon(togglePause) ?? togglePause,
        },
        Audio = new AudioSettings { Enabled = audioEnabled, Volume = volumePercent / 100.0 },
        Diagnostics = new DiagnosticsSettings { RecordCommands = recordCommands, LogLevel = logLevel },
        Taskbar = original.Taskbar with { UseUia = useUia },
    };

    /// <summary>Salvar: só grava se válido. Devolve true se gravou (a janela fecha); em falha de gravação mantém aberta com <see cref="SaveError"/>.</summary>
    public bool TrySave()
    {
        if (!IsValid) return false;
        if (!save(ToSettings()))
        {
            SaveError = "Não foi possível gravar as configurações.";
            return false;
        }
        SaveError = null;
        return true;
    }

    private static string? Canon(string v) => HotkeyParser.TryParse(v, out var chord, out _) ? HotkeyParser.Format(chord) : null;

    /// <summary>Devolve null se válido, senão a mensagem de erro do <see cref="HotkeyParser"/> (T5.7).</summary>
    internal static string? ValidateHotkey(string? text) => HotkeyParser.TryParse(text, out _, out var error) ? null : error;

    private void Load(AppSettings s)
    {
        startWithWindows = s.Behavior.StartWithWindows;
        hideOnFullscreen = s.Behavior.HideOnFullscreen;
        useUia = s.Taskbar.UseUia;
        autoScale = s.AutoScale;
        peekOnHover = s.Behavior.PeekOnHover;
        peekOpacity = s.Opacity.Peek;
        autoPassiveSeconds = s.Behavior.AutoPassiveSeconds;
        presentFps = s.Performance.PresentFps;
        absentFps = s.Performance.AbsentFps;
        saverOnBattery = s.Performance.SaverOnBattery;
        forceSaver = s.Performance.ForceSaver;
        reducedMotion = s.Accessibility.ReducedMotion;
        audioEnabled = s.Audio.Enabled;
        volumePercent = (int)Math.Round(s.Audio.Volume * 100);
        recordCommands = s.Diagnostics.RecordCommands;
        logLevel = s.Diagnostics.LogLevel;
        toggleInteractive = s.Hotkeys.ToggleInteractive;
        toggleVisibility = s.Hotkeys.ToggleVisibility;
        togglePause = s.Hotkeys.TogglePause;
    }

    private void Check(string property, string? error)
    {
        errors[property] = error;
        Raise(nameof(IsValid));
        Raise(nameof(HotkeyConflict));
        Raise("Item[]");
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        Raise(nameof(Dirty));
        return true;
    }

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
