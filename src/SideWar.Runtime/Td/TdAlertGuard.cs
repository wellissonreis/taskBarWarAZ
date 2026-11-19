using SideWar.Core.Td;

namespace SideWar.Runtime.Td;

/// <summary>Opções de alerta vindas das configurações (TD.40b2). Padrão do host: tudo desligado (comportamento anterior).</summary>
public sealed class TdAlertSettings
{
    /// <summary>Exibe alertas preditivos (não fatais). NÃO desliga a proteção fatal.</summary>
    public bool Enabled { get; set; }
    /// <summary>Pausa diante de alertas críticos e protege contra a derrota por distração.</summary>
    public bool AutoPause { get; set; }
    public bool Sound { get; set; }
    /// <summary>Bit por <see cref="TdAlertType"/>: "não pausar por este tipo". A queda fatal ignora a máscara enquanto AutoPause estiver ligado.</summary>
    public int MutedMask { get; set; }

    /// <summary>Tipos da proteção contra derrota: não silenciáveis enquanto AutoPause estiver ligado.</summary>
    public static bool IsProtection(TdAlertType type) => type is TdAlertType.FatalDrop or TdAlertType.PassedLastTower;

    public bool IsMuted(TdAlertType type) => !IsProtection(type) && (MutedMask & (1 << (int)type)) != 0;
    public void SetMuted(TdAlertType type, bool muted)
    {
        if (IsProtection(type)) return;
        MutedMask = muted ? MutedMask | (1 << (int)type) : MutedMask & ~(1 << (int)type);
    }
}

/// <summary>Registro (fora do replay do Core) das decisões do jogador sobre a proteção, com o frame em que ocorreram.</summary>
public enum TdProtectionKind : byte { Held, Saved, LetFall, Dismissed }

public readonly record struct TdProtectionEntry(long Frame, TdProtectionKind Kind, TdAlertType Type);

/// <summary>
/// Proteção contra derrota e alertas do host (SDD-38 §3-§4). Vive só no host: não altera o Core nem o hash. O host consulta
/// <see cref="Blocks"/> ANTES de cada Step (inclusive em lotes 2x/3x): se o passo levaria à Derrota (simulado numa cópia, exato),
/// ele NÃO é executado e a partida fica retida até o jogador escolher <see cref="Save"/> ou <see cref="LetFall"/>.
/// Retenção não usa a pausa do Core: pausa do usuário, modais e boss key seguem independentes.
/// </summary>
public sealed class TdAlertGuard
{
    public const int CooldownTicks = 60 * 30;
    public const int MaxNonFatalPerWave = 2;
    public const int DefeatWarningWaves = 3;
    public const int MaxDamageWarningsPerRun = 3;

    private readonly List<TdProtectionEntry> _log = [];
    private readonly Dictionary<TdAlertType, long> _lastShown = [];
    private int _wave = -1, _shownThisWave;
    private bool _fallAuthorized;
    private readonly HashSet<int> _acknowledged = [];
    private readonly HashSet<TdAlertType> _shownTips = [];
    private int _damageWarnings;
    private long _lastDamageWarning = -CooldownTicks;

    public TdAlertSettings Settings { get; } = new();

    /// <summary>Partida retida: nenhum Step roda. Sem expiração.</summary>
    public bool Holding { get; private set; }
    /// <summary>Alerta crítico vigente (fatal ou "pode cair") quando <see cref="Holding"/>.</summary>
    public TdAlert? HoldAlert { get; private set; }
    /// <summary>Último alerta não fatal exibido (cartão único do cantinho).</summary>
    public TdAlert? Current { get; private set; }
    /// <summary>Incrementa a cada novo alerta (a UI usa para pulsar/badge/som).</summary>
    public int Raised { get; private set; }
    public bool FallAuthorized => _fallAuthorized;
    public IReadOnlyList<TdProtectionEntry> Log => _log;

    public void Reset()
    {
        _log.Clear(); _lastShown.Clear();
        _wave = -1; _shownThisWave = 0; _fallAuthorized = false; _acknowledged.Clear();
        _shownTips.Clear(); _damageWarnings = 0; _lastDamageWarning = -CooldownTicks;
        Holding = false; HoldAlert = null; Current = null; Raised = 0;
    }

    /// <summary>Verdadeiro se o próximo Step deve ser impedido.</summary>
    public bool Blocks(TdMatchState match)
    {
        if (Holding) return true;
        if (!Settings.AutoPause || _fallAuthorized || match.Phase != MatchPhase.Onda || match.Paused) return false;
        if (match.WavesReached >= DefeatWarningWaves) return false;
        // Rede de segurança: o passo seguinte levaria à derrota (simulado numa cópia).
        if (match.NextStepIsFatal())
        {
            Hold(match, TdAlertCatalog.Test(match, TdAlertType.FatalDrop, out var a) ? a : default, TdAlertType.FatalDrop);
            return true;
        }
        // Gatilho geométrico: inimigo novo passou da torre mais próxima do portal (ainda não reconhecido por Salvar).
        foreach (int id in TdRiskAnalyzer.PastDefenseLine(match))
            if (!_acknowledged.Contains(id))
            {
                Hold(match, TdAlertCatalog.Test(match, TdAlertType.PassedLastTower, out var g) ? g : default, TdAlertType.PassedLastTower);
                return true;
            }
        return false;
    }

    /// <summary>Salvar: libera a retenção; o risco é reavaliado antes de CADA Step e volta a reter se persistir.</summary>
    public void Save(TdMatchState match)
    {
        if (!Holding || HoldAlert is not { } a || !TdAlertSettings.IsProtection(a.Type)) return;
        foreach (int id in TdRiskAnalyzer.PastDefenseLine(match)) _acknowledged.Add(id); // reconhecidos: só inimigos novos voltam a reter
        Holding = false; HoldAlert = null;
        _log.Add(new(match.Frame, TdProtectionKind.Saved, a.Type));
    }

    /// <summary>Deixar cair: autoriza os passos até a derrota da partida corrente (não vale para partidas futuras).</summary>
    public void LetFall(TdMatchState match)
    {
        if (!Holding || HoldAlert is not { } a || !TdAlertSettings.IsProtection(a.Type)) return;
        _fallAuthorized = true; Holding = false; HoldAlert = null;
        _log.Add(new(match.Frame, TdProtectionKind.LetFall, a.Type));
    }

    /// <summary>Continuar: libera retenção de alerta crítico comum (o cooldown do tipo evita repetição imediata); opcionalmente silencia o tipo.</summary>
    public void Dismiss(TdMatchState match, bool muteType = false)
    {
        if (!Holding || HoldAlert is not { } a || TdAlertSettings.IsProtection(a.Type)) return;
        if (muteType) Settings.SetMuted(a.Type, true);
        Holding = false; HoldAlert = null;
        _log.Add(new(match.Frame, TdProtectionKind.Dismissed, a.Type));
    }

    /// <summary>Avalia os alertas não fatais após os passos do OnWake: cooldown por tipo, limite por onda, configuração por tipo.</summary>
    public void Evaluate(TdMatchState match)
    {
        if (Holding || !Settings.Enabled && !Settings.AutoPause) return;
        if (match.WaveNumber != _wave) { _wave = match.WaveNumber; _shownThisWave = 0; }
        foreach (var alert in TdAlertCatalog.Detect(match))
        {
            if (TdAlertSettings.IsProtection(alert.Type)) continue; // tratado por Blocks, sem cooldown
            if (alert.Type == TdAlertType.CoreAtRisk && match.WavesReached >= DefeatWarningWaves) continue;
            bool damage = alert.Type is TdAlertType.ImminentLeak or TdAlertType.TowerDamage;
            if (damage && (_damageWarnings >= MaxDamageWarningsPerRun || match.Tick - _lastDamageWarning < CooldownTicks)) continue;
            if (alert.Severity == TdAlertSeverity.Tip && _shownTips.Contains(alert.Type)) continue;
            if (_lastShown.TryGetValue(alert.Type, out long last) && match.Tick - last < CooldownTicks) continue;
            bool pauses = alert.Severity == TdAlertSeverity.Critical && Settings.AutoPause && !_fallAuthorized && !Settings.IsMuted(alert.Type);
            if (!pauses && (!Settings.Enabled || _shownThisWave >= MaxNonFatalPerWave)) continue;
            _lastShown[alert.Type] = match.Tick;
            if (damage) { _damageWarnings++; _lastDamageWarning = match.Tick; }
            if (alert.Severity == TdAlertSeverity.Tip) _shownTips.Add(alert.Type);
            if (!pauses) _shownThisWave++;
            Current = alert; Raised++;
            if (pauses) Hold(match, alert, alert.Type);
            return;
        }
    }

    private void Hold(TdMatchState match, TdAlert alert, TdAlertType type)
    {
        Holding = true; HoldAlert = alert; Current = alert; Raised++;
        _log.Add(new(match.Frame, TdProtectionKind.Held, type));
    }
}
