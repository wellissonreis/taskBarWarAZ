using System.Globalization;
using SideWar.Core.Td;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Input;

namespace SideWar.Runtime.Td;

/// <summary>Geometria da vista Configurações (DIP da modal 720×480), compartilhada pelo controlador e pelo renderer.</summary>
public static class TdSettingsLayout
{
    public const float SidebarX = 16f, SidebarW = 150f, ContentX = 182f, ContentW = 518f, Top = 100f, RowH = 44f, ItemH = 40f;
    public const int VisibleRows = 8;
    /// <summary>Área dos controles (à direita do rótulo) e da trilha do slider.</summary>
    public const float CtrlX = 440f, CtrlW = 252f, TrackX = 490f, TrackW = 202f;
    public static readonly string[] Sections = ["Geral", "Aparência", "Desempenho", "Atalhos", "Acessibilidade", "Diagnóstico"];
}

public sealed partial class TdUiController
{
    private enum CfgKind { Toggle, Seg, Slider, Hotkey, Info, Group, Button }

    private sealed record CfgRow(CfgKind Kind, string Key, string Label, string? Tip = null, (string Text, int Value)[]? Options = null, int Min = 0, int Max = 0, int Step = 1, string Suffix = "", bool Enabled = true);

    private int _settingsSection, _settingsScroll;
    private string? _captureKey, _hotkeyError;

    /// <summary>True enquanto a vista Configurações espera uma combinação de teclas para um atalho.</summary>
    public bool CapturingHotkey => _captureKey is not null;

    /// <summary>Abre o menu já na vista Configurações (engrenagem, bandeja e atalhos).</summary>
    public void OpenSettingsView(int section = 0)
    {
        _settingsSection = Math.Clamp(section, 0, TdSettingsLayout.Sections.Length - 1);
        _settingsScroll = 0;
        _captureKey = null; _hotkeyError = null;
        SetMode(TdUiMode.Settings);
        _host.RefreshStartup();
    }

    private List<CfgRow> SettingsRows(TdSettingsData d)
    {
        var r = new List<CfgRow>();
        void Toggle(string key, string label, string? tip = null, bool enabled = true) => r.Add(new(CfgKind.Toggle, key, label, tip, Enabled: enabled));
        void Seg(string key, string label, (string, int)[] o, string? tip = null) => r.Add(new(CfgKind.Seg, key, label, tip, o));
        void Slider(string key, string label, int min, int max, string suffix, bool enabled = true, string? tip = null) => r.Add(new(CfgKind.Slider, key, label, tip, Min: min, Max: max, Suffix: suffix, Enabled: enabled));
        void Info(string text) => r.Add(new(CfgKind.Info, "", text));
        void Group(string text) => r.Add(new(CfgKind.Group, "", text));
        switch (_settingsSection)
        {
            case 0:
                Seg("mode", "Modo de jogo (ao reiniciar)", [("Tower Defense", 1), ("Clássico", 0)], "Vale ao reiniciar o SideWar.");
                if (d.StartupVisible)
                {
                    Toggle("startup", "Iniciar com o Windows", "Abre o SideWar ao entrar no Windows.", d.StartupAvailable);
                    if (!string.IsNullOrEmpty(d.StartupMessage)) Info(d.StartupMessage);
                }
                Seg("hide", "Ocultar em tela cheia", [("Este monitor", 0), ("Qualquer", 1), ("Nunca", 2)], "Quando ocultar a faixa ao detectar um app em tela cheia.");
                Toggle("uia", "UI Automation (barra de tarefas)", "Usa UI Automation para localizar a barra de tarefas.");
                Toggle("autoscale", "Auto-escala");
                Group("TOWER DEFENSE");
                Seg("difficulty", "Dificuldade (próx. corrida)", [("Casual", 0), ("Difícil", 1), ("Extremo", 2)], "Inimigos resistem mais às torres contra as quais são fortes.");
                Toggle("questions", "Pergunta-bônus entre ondas");
                Toggle("income", "Mostrar renda (+X/s)", "Mostra a renda de créditos abaixo do contador.");
                Toggle("typeinfo", "Mostrar tipo e efetividade", "Barra de vida, ícone de tipo e número de dano forte/fraco sobre os inimigos.");
                Toggle("discreet", "Modo discreto", "Sem brilhos nem contagem animada; +N só nos ganhos grandes.");
                Toggle("continuous", "Ondas contínuas (próx. corrida)", "Corrida infinita, sem Preparo entre ondas. Vale na próxima corrida.");
                Toggle("pinned", "Painel de torres sempre aberto", "Mantém o painel de torres aberto junto ao chip.");
                Seg("autostart", "Auto-início", [("Desligado", 0), ("Contagem", 1), ("Imediato", 2)]);
                Slider("autoseconds", "Contagem do auto-início", 5, 60, "s", d.AutoStartMode == AutoStartMode.Countdown);
                Toggle("autorestart", "Recomeçar automaticamente");
                Seg("band", "Tamanho da faixa", [("Pequeno", 40), ("Médio", 64), ("Grande", 88)], "Altura da faixa Tower Defense; aplica na hora.");
                Slider("bandfine", "Altura da faixa (DIP)", 32, 96, "");
                Seg("corner", "Tamanho da interface do canto", [("100%", 100), ("150%", 150), ("200%", 200)]);
                Slider("cornerfine", "Escala do canto", 100, 250, "%");
                Seg("speed", "Velocidade do jogo", [("1x", 1), ("2x", 2), ("3x", 3)]);
                Toggle("xspeeds", "Velocidades extras", "Acrescenta 4x, 5x, 8x e 10x ao seletor (padrão: só 1x/2x/3x).");
                if (d.ExtraSpeeds)
                {
                    var on = new List<(string, int)>();
                    for (int i = 0; i < TdSpeed.Extras.Length; i++) if ((d.SpeedMask >> i & 1) != 0) on.Add((TdSpeed.Label(TdSpeed.Extras[i]), TdSpeed.Extras[i]));
                    if (on.Count > 0) Seg("speedx", "Velocidade extra", [.. on], "Escolha a velocidade do jogo.");
                    for (int i = 0; i < TdSpeed.Extras.Length; i++) Toggle("xs:" + i, "Mostrar " + TdSpeed.Label(TdSpeed.Extras[i]), "Escolha quais velocidades extras aparecem no seletor e no atalho.");
                }
                Seg("notice", "Tamanho dos avisos", [("Desl.", 0), ("Peq.", 1), ("Méd.", 2), ("Grande", 3)]);
                Toggle("sound", "Som");
                Slider("volume", "Volume", 0, 100, "%", d.Sound);
                Group("MODO SOMBRA");
                Toggle("shadow", "Modo sombra", "Farme sem perder nem ganhar XP; recompensas permanentes em 35%. Entra e sai entre ondas.");
                Toggle("shadowabsent", "Sombra ao ficar ausente", "Entra em modo sombra sozinho quando você fica ausente (desligado por padrão).");
                Group("ALERTAS");
                Toggle("alerts", "Avisar defesa em risco");
                Toggle("alertsound", "Som curto no alerta");
                Group("ALERTAS SILENCIADOS (DESMARQUE PARA REATIVAR)");
                foreach (var t in Enum.GetValues<TdAlertType>())
                    if (t is not (TdAlertType.FatalDrop or TdAlertType.PassedLastTower))
                        Toggle("mute:" + (int)t, "Silenciar: " + AlertMuteName(t));
                Group("PRESENÇA");
                Info("Ausência após 5 min de inatividade (faixas de 15 e 60 min);");
                Info("card offline a partir de 10 min; teto offline de 12 h.");
                break;
            case 1:
                Info("Na faixa TD, o esmaecer padrão é 50% ao usar outro app.");
                Toggle("peek", "Esmaecer ao passar o mouse");
                Slider("peekop", "Opacidade ao passar o mouse", 0, 255, "", d.PeekOnHover);
                Toggle("absent", "Esmaecer quando ausente");
                Slider("absentop", "Opacidade quando ausente", 0, 255, "", d.FadeWhenAbsent);
                Toggle("fade", "Esmaecer ao usar outro app (TD)", "Esmaece a faixa e o HUD ao usar outro app.");
                Slider("fadepct", "Opacidade ao usar outro app", 10, 90, "%", d.FadeWhenUnfocused);
                break;
            case 2:
                Seg("fpsp", "FPS presente", [("20", 20), ("30", 30), ("60", 60)]);
                Seg("fpsa", "FPS ausente", [("4", 4), ("8", 8)]);
                Toggle("saver", "Economia na bateria");
                Toggle("forcesaver", "Forçar economia");
                break;
            case 3:
                Info("Clique no atalho e pressione a nova combinação. Esc cancela.");
                Info("Os atalhos passam a valer ao reiniciar o SideWar.");
                r.Add(new(CfgKind.Hotkey, "hk:interactive", "Modo interativo da faixa"));
                r.Add(new(CfgKind.Hotkey, "hk:visibility", "Visibilidade (boss key)"));
                r.Add(new(CfgKind.Hotkey, "hk:pause", "Pausa"));
                r.Add(new(CfgKind.Hotkey, "hk:wave", "Iniciar onda (Tower Defense)"));
                r.Add(new(CfgKind.Hotkey, "hk:speed", "Ciclar velocidade (TD)"));
                r.Add(new(CfgKind.Hotkey, "hk:shadow", "Modo sombra (TD)"));
                r.Add(new(CfgKind.Button, "hk:restore", "Restaurar padrão"));
                break;
            case 4:
                Toggle("motion", "Reduzir movimento");
                break;
            default:
                Toggle("record", "Gravar comandos");
                Seg("log", "Nível de log", [("Debug", 0), ("Info", 1), ("Aviso", 2), ("Erro", 3)]);
                break;
        }
        return r;
    }

    private static string AlertMuteName(TdAlertType type) => type switch
    {
        TdAlertType.CoreAtRisk => "Base em risco",
        TdAlertType.ImminentLeak => "Inimigo perto da base",
        TdAlertType.TowerDamage => "Torre tomará dano",
        TdAlertType.FewTowers => "Poucas torres",
        TdAlertType.IdleCredits => "Créditos acumulados",
        TdAlertType.FlyerNoAntiAir => "Voador sem antiaérea",
        TdAlertType.BossIncoming => "Chefe chegando",
        TdAlertType.RuneAvailable => "Runa disponível",
        TdAlertType.AbilityIdle => "Habilidade pronta",
        TdAlertType.VaultBackupIdle => "Cofre ou Backup parado",
        _ => "Phishing sem Sentinel",
    };

    private static int CfgGet(TdSettingsData d, string k)
    {
        if (k.StartsWith("mute:", StringComparison.Ordinal)) return (d.AlertsMutedMask >> int.Parse(k.AsSpan(5), CultureInfo.InvariantCulture)) & 1;
        if (k.StartsWith("xs:", StringComparison.Ordinal)) return (d.SpeedMask >> int.Parse(k.AsSpan(3), CultureInfo.InvariantCulture)) & 1;
        return k switch
        {
            "xspeeds" => d.ExtraSpeeds ? 1 : 0, "speedx" => d.GameSpeed, "shadowabsent" => d.ShadowOnAbsent ? 1 : 0, "typeinfo" => d.ShowTypeInfo ? 1 : 0,
            "difficulty" => d.Difficulty, "mode" => d.GameMode, "startup" => d.StartWithWindows ? 1 : 0, "hide" => d.HideOnFullscreen, "uia" => d.UseUia ? 1 : 0, "autoscale" => d.AutoScale ? 1 : 0,
            "questions" => d.BonusQuestions ? 1 : 0, "income" => d.ShowIncome ? 1 : 0, "discreet" => d.DiscreetMode ? 1 : 0, "continuous" => d.ContinuousWaves ? 1 : 0,
            "pinned" => d.TowerPanelPinned ? 1 : 0, "autostart" => (int)d.AutoStartMode, "autoseconds" => d.AutoStartSeconds, "autorestart" => d.AutoRestart ? 1 : 0,
            "band" or "bandfine" => d.BandHeight, "corner" or "cornerfine" => d.CornerUiScale, "speed" => d.GameSpeed, "notice" => d.NoticeSize,
            "sound" => d.Sound ? 1 : 0, "volume" => d.Volume, "alerts" => d.AlertsEnabled ? 1 : 0, "alertsound" => d.AlertsSound ? 1 : 0,
            "peek" => d.PeekOnHover ? 1 : 0, "peekop" => d.PeekOpacity, "absent" => d.FadeWhenAbsent ? 1 : 0, "absentop" => d.AbsentOpacity,
            "fade" => d.FadeWhenUnfocused ? 1 : 0, "fadepct" => d.FadePercent, "fpsp" => d.PresentFps, "fpsa" => d.AbsentFps,
            "saver" => d.SaverOnBattery ? 1 : 0, "forcesaver" => d.ForceSaver ? 1 : 0, "motion" => d.ReducedMotion ? 1 : 0, "record" => d.RecordCommands ? 1 : 0, "log" => d.LogLevel,
            _ => 0,
        };
    }

    private static TdSettingsData CfgPut(TdSettingsData d, string k, int v)
    {
        bool b = v != 0;
        if (k.StartsWith("mute:", StringComparison.Ordinal))
        {
            int bit = 1 << int.Parse(k.AsSpan(5), CultureInfo.InvariantCulture);
            return d with { AlertsMutedMask = b ? d.AlertsMutedMask | bit : d.AlertsMutedMask & ~bit };
        }
        if (k.StartsWith("xs:", StringComparison.Ordinal))
        {
            int bit = 1 << int.Parse(k.AsSpan(3), CultureInfo.InvariantCulture);
            var nd = d with { SpeedMask = b ? d.SpeedMask | bit : d.SpeedMask & ~bit };
            return nd with { GameSpeed = TdSpeed.Resolve(nd.GameSpeed, TdSpeed.Allowed(nd.ExtraSpeeds, nd.SpeedMask)) };
        }
        return k switch
        {
            "shadowabsent" => d with { ShadowOnAbsent = b }, "typeinfo" => d with { ShowTypeInfo = b },
            "xspeeds" => d with { ExtraSpeeds = b, GameSpeed = b ? d.GameSpeed : Math.Min(d.GameSpeed, 3), SpeedMask = b && d.SpeedMask == 0 ? TdSpeed.DefaultMask : d.SpeedMask }, "speedx" => d with { GameSpeed = v },
            "difficulty" => d with { Difficulty = v }, "mode" => d with { GameMode = v }, "startup" => d with { StartWithWindows = b }, "hide" => d with { HideOnFullscreen = v }, "uia" => d with { UseUia = b },
            "autoscale" => d with { AutoScale = b }, "questions" => d with { BonusQuestions = b }, "income" => d with { ShowIncome = b }, "discreet" => d with { DiscreetMode = b },
            "continuous" => d with { ContinuousWaves = b }, "pinned" => d with { TowerPanelPinned = b }, "autostart" => d with { AutoStartMode = (AutoStartMode)v, AutoStart = b },
            "autoseconds" => d with { AutoStartSeconds = v }, "autorestart" => d with { AutoRestart = b }, "band" or "bandfine" => d with { BandHeight = v },
            "corner" or "cornerfine" => d with { CornerUiScale = v }, "speed" => d with { GameSpeed = v }, "notice" => d with { NoticeSize = v },
            "sound" => d with { Sound = b }, "volume" => d with { Volume = v }, "alerts" => d with { AlertsEnabled = b },
            "alertsound" => d with { AlertsSound = b }, "peek" => d with { PeekOnHover = b }, "peekop" => d with { PeekOpacity = v }, "absent" => d with { FadeWhenAbsent = b },
            "absentop" => d with { AbsentOpacity = v }, "fade" => d with { FadeWhenUnfocused = b }, "fadepct" => d with { FadePercent = v },
            "fpsp" => d with { PresentFps = v }, "fpsa" => d with { AbsentFps = v }, "saver" => d with { SaverOnBattery = b }, "forcesaver" => d with { ForceSaver = b },
            "motion" => d with { ReducedMotion = b }, "record" => d with { RecordCommands = b }, "log" => d with { LogLevel = v },
            _ => d,
        };
    }

    private static string HotkeyGet(TdSettingsData d, string k) => k switch
    {
        "hk:interactive" => d.ToggleInteractive, "hk:visibility" => d.ToggleVisibility, "hk:pause" => d.TogglePause, "hk:speed" => d.CycleSpeed, "hk:shadow" => d.ToggleShadow, _ => d.StartWave,
    };

    private static TdSettingsData HotkeyPut(TdSettingsData d, string k, string v) => k switch
    {
        "hk:interactive" => d with { ToggleInteractive = v }, "hk:visibility" => d with { ToggleVisibility = v }, "hk:pause" => d with { TogglePause = v }, "hk:speed" => d with { CycleSpeed = v }, "hk:shadow" => d with { ToggleShadow = v }, _ => d with { StartWave = v },
    };

    private void ApplySetting(TdSettingsData next) { _host.ApplySettings(next); Changed?.Invoke(); }

    /// <summary>Itens da vista Configurações: lista lateral de seções e as linhas da seção (rolagem por linhas).</summary>
    private void AddSettings(List<TdUiItem> items)
    {
        var d = _host.Settings;
        for (int i = 0; i < TdSettingsLayout.Sections.Length; i++)
            items.Add(new(new DipRect(TdSettingsLayout.SidebarX, TdSettingsLayout.Top + i * 38, TdSettingsLayout.SidebarW, 34), TdUiKind.Button, TdSettingsLayout.Sections[i],
                TdUiAction.SettingsSection, i, Selected: i == _settingsSection, SArg: "cfg-section"));
        var rows = SettingsRows(d);
        _settingsScroll = Math.Clamp(_settingsScroll, 0, Math.Max(0, rows.Count - TdSettingsLayout.VisibleRows));
        const float x = TdSettingsLayout.ContentX, w = TdSettingsLayout.ContentW, h = TdSettingsLayout.ItemH;
        for (int i = _settingsScroll; i < Math.Min(rows.Count, _settingsScroll + TdSettingsLayout.VisibleRows); i++)
        {
            var row = rows[i];
            float y = TdSettingsLayout.Top + (i - _settingsScroll) * TdSettingsLayout.RowH;
            var rect = new DipRect(x, y, w, h);
            int value = row.Key == "shadow" ? ((_match().ShadowPending ?? _match().Shadow) ? 1 : 0) : row.Key.Length == 0 || row.Kind == CfgKind.Hotkey ? 0 : CfgGet(d, row.Key);
            switch (row.Kind)
            {
                case CfgKind.Group:
                    items.Add(new(new DipRect(x, y + 14, w, 16), TdUiKind.Label, row.Label, SArg: "cfg-group"));
                    break;
                case CfgKind.Info:
                    items.Add(new(new DipRect(x, y + 12, w, 16), TdUiKind.Label, row.Label, SArg: "cfg-info"));
                    break;
                case CfgKind.Toggle:
                    items.Add(new(rect, TdUiKind.Button, row.Label, TdUiAction.SettingToggle, 0, row.Key, Selected: value != 0, Enabled: row.Enabled, Tip: row.Tip));
                    break;
                case CfgKind.Seg:
                {
                    items.Add(new(rect, TdUiKind.Label, row.Label, SArg: "cfg-label", Tip: row.Tip));
                    var o = row.Options!;
                    float bw = (TdSettingsLayout.CtrlW - 4 * (o.Length - 1)) / o.Length;
                    for (int k = 0; k < o.Length; k++)
                        items.Add(new(new DipRect(TdSettingsLayout.CtrlX + k * (bw + 4), y + 6, bw, h - 12), TdUiKind.Button, o[k].Text, TdUiAction.SettingChoice, o[k].Value, row.Key, Selected: value == o[k].Value));
                    break;
                }
                case CfgKind.Slider:
                    items.Add(new(rect, TdUiKind.Button, row.Label, TdUiAction.SettingSlider, row.Min, row.Key, Right: value.ToString(CultureInfo.InvariantCulture) + row.Suffix,
                        Fill: Math.Clamp((value - row.Min) / (float)Math.Max(1, row.Max - row.Min), 0f, 1f), Enabled: row.Enabled, Tip: row.Tip, Sub: row.Max.ToString(CultureInfo.InvariantCulture)));
                    break;
                case CfgKind.Hotkey:
                {
                    bool capturing = _captureKey == row.Key;
                    items.Add(new(rect, TdUiKind.Button, row.Label, TdUiAction.SettingHotkey, 0, row.Key,
                        Right: capturing ? "Pressione a combinação…" : HotkeyGet(d, row.Key), Selected: capturing, Sub: capturing ? _hotkeyError : null, Tone: capturing && _hotkeyError is not null ? TdTone.Bad : TdTone.Normal));
                    break;
                }
                case CfgKind.Button:
                    items.Add(new(new DipRect(x, y, 160, h), TdUiKind.Button, row.Label, TdUiAction.SettingsRestoreHotkeys, SArg: "cfg-button"));
                    break;
            }
        }
    }

    private TdScrollInfo? SettingsScrollInfo()
    {
        int rows = SettingsRows(_host.Settings).Count;
        return rows > TdSettingsLayout.VisibleRows
            ? new(rows, TdSettingsLayout.VisibleRows, Math.Clamp(_settingsScroll, 0, rows - TdSettingsLayout.VisibleRows), TdSettingsLayout.Top, TdSettingsLayout.VisibleRows * TdSettingsLayout.RowH - 4f)
            : null;
    }

    /// <summary>Trata as ações da vista Configurações (aplicação imediata, sem Salvar/Cancelar). Devolve true se tratou.</summary>
    private bool ActivateSetting(TdUiItem item)
    {
        var d = _host.Settings;
        switch (item.Action)
        {
            case TdUiAction.SettingsOpen: OpenSettingsView(); return true;
            case TdUiAction.SettingsSection:
                _settingsSection = Math.Clamp(item.Arg, 0, TdSettingsLayout.Sections.Length - 1); _settingsScroll = 0; _captureKey = null; _hotkeyError = null;
                Changed?.Invoke(); return true;
            case TdUiAction.SettingToggle when item.SArg == "shadow": ToggleShadow(); return true;
            case TdUiAction.SettingToggle when item.SArg is { } key:
                _captureKey = null;
                ApplySetting(CfgPut(d, key, CfgGet(d, key) == 0 ? 1 : 0)); return true;
            case TdUiAction.SettingChoice when item.SArg is { } key:
                _captureKey = null;
                ApplySetting(CfgPut(d, key, item.Arg)); return true;
            case TdUiAction.SettingSlider: return true; // o valor vem da posição do clique (SetSettingAt)
            case TdUiAction.SettingHotkey when item.SArg is { } key:
                _captureKey = _captureKey == key ? null : key; _hotkeyError = null;
                Changed?.Invoke(); return true;
            case TdUiAction.SettingsRestoreHotkeys:
            {
                var def = new TdSettingsData();
                _captureKey = null; _hotkeyError = null;
                ApplySetting(d with { ToggleInteractive = def.ToggleInteractive, ToggleVisibility = def.ToggleVisibility, TogglePause = def.TogglePause, StartWave = def.StartWave, CycleSpeed = def.CycleSpeed, ToggleShadow = def.ToggleShadow });
                return true;
            }
        }
        return false;
    }

    /// <summary>Slider da vista Configurações: o valor vem da posição horizontal do clique na trilha (limitado ao intervalo da linha).</summary>
    public void SetSettingAt(TdUiItem item, float x)
    {
        if (item.Action != TdUiAction.SettingSlider || item.SArg is not { } key || !item.Enabled) return;
        int min = item.Arg, max = int.Parse(item.Sub ?? "0", CultureInfo.InvariantCulture);
        float f = Math.Clamp((x - TdSettingsLayout.TrackX) / TdSettingsLayout.TrackW, 0f, 1f);
        int value = (int)Math.Round(min + f * (max - min), MidpointRounding.AwayFromZero);
        var d = _host.Settings;
        if (CfgGet(d, key) == value) return;
        ApplySetting(CfgPut(d, key, value));
    }

    /// <summary>Captura de atalho dentro do menu: recebe a tecla (VK) e os modificadores do instante. Esc sem modificador cancela.</summary>
    public void CaptureHotkey(int vk, bool ctrl, bool alt, bool shift, bool win)
    {
        if (_captureKey is not { } key) return;
        if (vk is 16 or 17 or 18 or 91 or 92 or 160 or 161 or 162 or 163 or 164 or 165) return;
        if (vk == 27 && !(ctrl || alt || shift || win)) { _captureKey = null; _hotkeyError = null; Changed?.Invoke(); return; }
        var mods = (ctrl ? HotkeyModifiers.Ctrl : 0) | (alt ? HotkeyModifiers.Alt : 0) | (shift ? HotkeyModifiers.Shift : 0) | (win ? HotkeyModifiers.Win : 0);
        if (mods == HotkeyModifiers.None) { _hotkeyError = "Use Ctrl, Alt, Shift ou Win junto com a tecla."; Changed?.Invoke(); return; }
        string text;
        try { text = HotkeyParser.Format(new HotkeyChord(mods, (uint)vk)); }
        catch (ArgumentOutOfRangeException) { _hotkeyError = "Tecla não suportada."; Changed?.Invoke(); return; }
        var d = _host.Settings;
        string[] keys = ["hk:interactive", "hk:visibility", "hk:pause", "hk:wave", "hk:speed", "hk:shadow"];
        foreach (var other in keys)
            if (other != key && string.Equals(HotkeyGet(d, other), text, StringComparison.OrdinalIgnoreCase)) { _hotkeyError = "Já usado por outro atalho."; Changed?.Invoke(); return; }
        _captureKey = null; _hotkeyError = null;
        ApplySetting(HotkeyPut(d, key, text));
    }
}

public sealed partial class TdUiController
{
    private bool _awayActive;
    private long _awayFragments;
    private int _awayWaves, _awaySeals;

    /// <summary>Resumo discreto ao voltar da ausência em sombra: "Fora: +N frag · +M selos · X ondas".</summary>
    private void SyncShadowAway(TdMatchState m)
    {
        if (m.Shadow && !m.Present && !_awayActive)
        {
            _awayActive = true;
            _awayFragments = m.ShadowFragments; _awayWaves = m.ShadowWaves; _awaySeals = _host.ShadowSeals;
        }
        else if (_awayActive && m.Present)
        {
            _awayActive = false;
            long frag = m.ShadowFragments - _awayFragments;
            int waves = m.ShadowWaves - _awayWaves, seals = _host.ShadowSeals - _awaySeals;
            if (BossHidden || (frag <= 0 && waves <= 0 && seals <= 0)) return;
            _noticeTitle = "Sombra"; _noticeRank = null;
            _noticeText = $"Fora: +{frag} frag · +{seals} selo{(seals == 1 ? "" : "s")} · {waves} onda{(waves == 1 ? "" : "s")}";
            _noticeUntil = _now() + StageNoticeSeconds;
            Changed?.Invoke();
        }
    }
}
