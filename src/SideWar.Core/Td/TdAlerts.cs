namespace SideWar.Core.Td;

/// <summary>Tipos de alerta do SDD-38 §2, na ordem fixa da tabela (a ordem numérica é o desempate).</summary>
public enum TdAlertType : byte
{
    /// <summary>FatalDrop e CoreAtRisk (derrota por vida do núcleo) foram removidos na REPUTACAO: nunca são emitidos; os valores ficam só para a máscara de silenciados já gravada.</summary>
    FatalDrop, CoreAtRisk, ImminentLeak, TowerDamage, FewTowers, IdleCredits,
    FlyerNoAntiAir, BossIncoming, RuneAvailable, AbilityIdle, VaultBackupIdle, PhishingNoSentinel,
    /// <summary>Gatilho geométrico (SDD-38 §3): inimigo passou da torre posicionada mais próxima do portal (ou da distância de segurança, sem torres).</summary>
    PassedLastTower,
}

public enum TdAlertSeverity : byte { Tip, Warning, Critical }

/// <summary>Alerta detectado: tipo, severidade, mensagem e ação sugerida (SDD-38 §2).</summary>
public readonly record struct TdAlert(TdAlertType Type, TdAlertSeverity Severity, string Message, string Action);

/// <summary>
/// Catálogo de detecção do SDD-38. Funções puras sobre o estado: sem RNG, sem efeitos, fora do hash e do balanceamento.
/// Sem dados suficientes, o tipo não é emitido.
/// </summary>
public static class TdAlertCatalog
{
    public const double AtRiskSeconds = 5.0;
    public const double ImminentSeconds = 3.0;
    public const double DamageSeconds = 8.0;
    public const int MinTowers = 2;
    public const int IdleCreditsFactor = 2;

    public static TdAlertSeverity SeverityOf(TdAlertType type) => type switch
    {
        TdAlertType.FatalDrop or TdAlertType.CoreAtRisk or TdAlertType.PassedLastTower => TdAlertSeverity.Critical,
        TdAlertType.ImminentLeak or TdAlertType.TowerDamage or TdAlertType.FlyerNoAntiAir
            or TdAlertType.BossIncoming or TdAlertType.PhishingNoSentinel => TdAlertSeverity.Warning,
        _ => TdAlertSeverity.Tip,
    };

    /// <summary>Todos os alertas ativos, do mais grave ao menos grave; empate pela ordem da tabela.</summary>
    public static IReadOnlyList<TdAlert> Detect(TdMatchState m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var list = new List<TdAlert>();
        if (m.Phase is not (MatchPhase.Preparo or MatchPhase.Onda)) return list;
        bool wave = m.Phase == MatchPhase.Onda;
        for (var t = TdAlertType.FatalDrop; t <= TdAlertType.PassedLastTower; t++)
            if (wave || t is TdAlertType.FewTowers or TdAlertType.IdleCredits or TdAlertType.RuneAvailable)
                if (Test(m, t, out var alert)) list.Add(alert);
        return [.. list.OrderByDescending(a => a.Severity).ThenBy(a => a.Type)];
    }

    public static bool Test(TdMatchState m, TdAlertType type, out TdAlert alert)
    {
        alert = default;
        bool hit = type switch
        {
            TdAlertType.PassedLastTower => TdRiskAnalyzer.PastDefenseLine(m).Count > 0,
            TdAlertType.ImminentLeak => m.Phase == MatchPhase.Onda && TdRiskAnalyzer.SecondsToLeak(m.Enemies, m.LeakX) <= ImminentSeconds,
            TdAlertType.TowerDamage => m.Phase == MatchPhase.Onda && TdRiskAnalyzer.SecondsToLeak(m.Enemies, m.LeakX) <= DamageSeconds,
            TdAlertType.FewTowers => PlacedTowers(m) < MinTowers && (m.Phase == MatchPhase.Onda || m.CountdownRemainingSeconds > 0),
            TdAlertType.IdleCredits => IdleCredits(m),
            TdAlertType.FlyerNoAntiAir => m.Enemies.Any(e => e.Hp > 0 && e.Flying) && !m.Towers.Any(t => t.Placed && m.Content.Towers[t.DefIndex].Targets == "both"),
            TdAlertType.BossIncoming => m.Enemies.Any(e => e.Hp > 0 && e.IsBoss),
            TdAlertType.RuneAvailable => m.Fragments >= 1 && m.Frontier.Count > 0,
            TdAlertType.AbilityIdle => m.Enemies.Count > 0 && m.AbilityStatuses.Any(a => a.Unlocked && a.CooldownTicks == 0 && a.ActiveTicks == 0 && a.UsesLeft != 0),
            TdAlertType.VaultBackupIdle => m.Towers.Any(t => t.Placed && t.DisabledTicks > 0 && m.Content.Towers[t.DefIndex].Id is "vault" or "backup"),
            TdAlertType.PhishingNoSentinel => m.Enemies.Any(e => e.Hp > 0 && m.EnemyDefId(e.DefIndex) == "phishing") && !HasPlaced(m, "sentinel"),
            _ => false,
        };
        if (hit) alert = Make(type);
        return hit;
    }

    private static int PlacedTowers(TdMatchState m) => m.Towers.Count(t => t.Placed);

    private static bool HasPlaced(TdMatchState m, string id) => m.Towers.Any(t => t.Placed && m.Content.Towers[t.DefIndex].Id == id);

    private static bool IdleCredits(TdMatchState m)
    {
        long cheapest = long.MaxValue;
        var towers = m.Content.Towers;
        for (int i = 0; i < towers.Length; i++)
        {
            if (m.UnlockedTowerStage < towers[i].UnlockStage) continue;
            if (towers[i].Stats.TryGetValue("maxPerPlayer", out var limit) && m.TowerCount(i) >= limit) continue;
            cheapest = Math.Min(cheapest, m.NextTowerCost(i));
        }
        return cheapest != long.MaxValue && m.Credits >= cheapest * IdleCreditsFactor;
    }

    private static TdAlert Make(TdAlertType type) => type switch
    {
        TdAlertType.ImminentLeak => new(type, TdAlertSeverity.Warning, "Inimigo perto do núcleo.", "Intercepte-o."),
        TdAlertType.TowerDamage => new(type, TdAlertSeverity.Warning, "Nesse ritmo sua torre tomará dano.", "Reforce a defesa."),
        TdAlertType.FewTowers => new(type, TdAlertSeverity.Tip, "Poucas torres para a onda.", "Construa mais."),
        TdAlertType.IdleCredits => new(type, TdAlertSeverity.Tip, "Créditos acumulados.", "Construa ou melhore sua defesa."),
        TdAlertType.FlyerNoAntiAir => new(type, TdAlertSeverity.Warning, "Voador sem defesa.", "Construa antiaérea."),
        TdAlertType.BossIncoming => new(type, TdAlertSeverity.Warning, "Chefe chegando.", "Prepare habilidades."),
        TdAlertType.RuneAvailable => new(type, TdAlertSeverity.Tip, "Runa disponível.", "Desperte uma runa."),
        TdAlertType.AbilityIdle => new(type, TdAlertSeverity.Tip, "Habilidade pronta.", "Use se precisar."),
        TdAlertType.VaultBackupIdle => new(type, TdAlertSeverity.Tip, "Cofre ou Backup parado.", "Confira sua defesa."),
        TdAlertType.PassedLastTower => new(type, TdAlertSeverity.Critical, "Nesse ritmo sua torre vai cair.", "Um inimigo passou da sua última torre. Construa ou use uma habilidade."),
        _ => new(type, TdAlertSeverity.Warning, "Phishing sem Sentinel.", "Construa uma Sentinel."),
    };
}
