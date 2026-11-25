namespace SideWar.Core.Td;

/// <summary>
/// Reputação (XP de patente, REPUTACAO): na corrida persistente a vida do núcleo não existe como derrota. Inimigo que chega à base tira XP,
/// vencer onda e fase dá XP. Os valores são do Tech Lead (ver REPORT); a patente vem de <see cref="TdRank"/> sobre o XP acumulado no perfil.
/// </summary>
public static class TdReputation
{
    /// <summary>XP perdido por ponto de dano que o inimigo daria ao núcleo (INFINITO: dói de verdade; algumas ondas ruins derrubam a patente).</summary>
    public const int XpPerCoreDamage = 200;
    /// <summary>Teto do escudo de XP (runas + árvore de selos), em %.</summary>
    public const int MaxShieldPercent = 75;
    /// <summary>Cada ponto de nó de selos "xpShield" vale tantos % de XP perdida a menos.</summary>
    public const int MetaShieldPercentPerPoint = 3;

    /// <summary>Teto do bônus de XP ganho das runas, em pontos-base (+50%).</summary>
    public const int MaxXpGainBp = 5000;

    /// <summary>Multiplicador da sequência (streak) de ondas sem vazamento: +10% por onda, até x4 (30 ondas limpas). Zera ao vazar.</summary>
    public const double StreakStep = 0.10, StreakCap = 4.0;

    public static double StreakMultiplier(int streak) => Math.Min(StreakCap, 1.0 + StreakStep * Math.Max(0, streak));

    /// <summary>
    /// XP por onda vencida (INFINITO): 2 × (6 + onda/6 + 2·√(fase−1)) × multiplicador da sequência sem vazar. Onda que vazou paga metade do
    /// base, sem multiplicador (e zera a sequência). O vazamento custa <see cref="XpPerCoreDamage"/> por dano: jogo passivo perde mais do que ganha.
    /// </summary>
    public static int WaveXp(int stageNumber, int wave, bool leaked, int streak = 0)
    {
        long baseXp = 2 * (6L + Math.Max(1, wave) / 6 + (long)(2.0 * Math.Sqrt(Math.Max(1, stageNumber) - 1)));
        return (int)Math.Min(100_000, leaked ? baseXp / 2 : (long)(baseXp * StreakMultiplier(streak)));
    }

    /// <summary>XP bônus ao concluir uma fase: base + por fase, e por estrela (1 a 3, 150 cada): desempenho (menos vazamento = mais estrelas) paga mais.</summary>
    public static int StageClearXp(int stageNumber, int stars, bool flawless = false) =>
        (int)Math.Min(100_000, 100L + 10L * (Math.Max(1, stageNumber) - 1) + 150L * Math.Clamp(stars, 1, 3) + (flawless ? FlawlessStageXp : 0));

    /// <summary>Campanha: XP ganho (%) a partir de Sênior (rumo a Especialista) e a partir de Especialista (rumo a Arquiteto).</summary>
    public const int SeniorXpGainPercent = 60, SpecialistXpGainPercent = 40;

    /// <summary>Barra de XP da patente (do limiar dela ao próximo; na máxima, a última barra).</summary>
    public static int RankBar(TdRankLevel rank)
    {
        var from = rank == TdRankLevel.Arquiteto ? TdRankLevel.Especialista : rank;
        return TdRank.Threshold(from + 1) - TdRank.Threshold(from);
    }

    /// <summary>Bônus de fase sem nenhum vazamento (INFINITO): premia desempenho, além do multiplicador da sequência.</summary>
    public const int FlawlessStageXp = 300;

    public static int XpLost(int coreDamage, int shieldPercent)
    {
        shieldPercent = Math.Clamp(shieldPercent, 0, MaxShieldPercent);
        return (int)((long)Math.Max(0, coreDamage) * XpPerCoreDamage * (100 - shieldPercent) / 100);
    }
}

public sealed partial class TdMatchState
{
    /// <summary>Reputação da corrida, iniciada com o valor do perfil para decidir a próxima região.</summary>
    public int GateReputation { get; private set; }
    /// <summary>XP de reputação ganho nesta partida (ondas, fases, recuperações).</summary>
    public long ReputationGainedTotal { get; private set; }
    /// <summary>XP de reputação perdido nesta partida (inimigos que chegaram à base).</summary>
    public long ReputationLostTotal { get; private set; }

    /// <summary>Escudo atual de XP perdida, em % (runas + árvore de selos, limitado).</summary>
    public int XpShieldPercent => Math.Min(TdReputation.MaxShieldPercent, Rune("xpShield") + _metaShield * TdReputation.MetaShieldPercentPerPoint);

    private int _streak;
    /// <summary>Ondas seguidas vencidas sem nenhum vazamento (zera ao vazar); alimenta o multiplicador de XP.</summary>
    public int Streak => _streak;
    public double StreakMultiplier => TdReputation.StreakMultiplier(_streak);

    private void LoseReputation(int coreDamage)
    {
        if (_shadow) return; // sombra: XP congelado (sem perda nem rebaixamento)
        _streak = 0;
        var before = (TdRankLevel)CurrentRankId;
        // Sênior em diante: cada chegada à base custa METADE da barra da patente atual (sem escudo).
        bool halfBar = _options.UsesCampaignRules && before >= TdRankLevel.Senior;
        int xp = coreDamage <= 0 ? 0 : halfBar ? TdReputation.RankBar(before) / 2 : TdReputation.XpLost(coreDamage, XpShieldPercent);
        if (xp <= 0) return;
        _repLostStage += xp;
        ReputationLostTotal += xp;
        GateReputation = (int)Math.Max(0L, (long)GateReputation - xp);
        Emit(TdEventType.BaseAttacked, xp, coreDamage);
        if (halfBar && CurrentRankId < (int)before) TrimFusionsToRank();
    }

    /// <summary>Bônus de XP ganho das runas "xpGainBp" (pontos-base). Vale só para ganho (onda/fase), nunca para a perda (xpShield) nem para recuperação.</summary>
    public int XpGainBp => Math.Clamp(Rune("xpGainBp"), 0, TdReputation.MaxXpGainBp);

    private void GainReputation(int xp, int wave, bool boost = true)
    {
        if (xp <= 0 || _shadow) return; // sombra: sem ganho de XP
        if (boost && _options.UsesCampaignRules && CurrentRankId >= (int)TdRankLevel.Senior)
            xp = (int)Math.Max(1L, (long)xp * (CurrentRankId >= (int)TdRankLevel.Especialista ? TdReputation.SpecialistXpGainPercent : TdReputation.SeniorXpGainPercent) / 100);
        if (boost && XpGainBp > 0) xp = (int)Math.Min(100_000L, (long)xp * (10_000 + XpGainBp) / 10_000);
        ReputationGainedTotal += xp;
        GateReputation = (int)Math.Min(100_000_000L, (long)GateReputation + xp);
        Emit(TdEventType.ReputationGained, xp, wave);
    }

    private void WinWaveReputation(int wave, bool leaked)
    {
        if (_shadow) { ShadowWaves++; return; } // sombra: sem XP nem multiplicador de sequência
        _streak = leaked ? 0 : _streak + 1;
        GainReputation(TdReputation.WaveXp(_stageNumber, wave, leaked, _streak), wave);
    }

    /// <summary>Devolve XP perdido nesta fase (Backup/Snapshot): <paramref name="coreDamageUnits"/> × XP por dano, limitado ao perdido. Retorna o XP recuperado.</summary>
    private int RecoverReputation(int coreDamageUnits)
    {
        int xp = Math.Min(_repLostStage, Math.Max(0, coreDamageUnits) * TdReputation.XpPerCoreDamage);
        if (xp <= 0 || _shadow) return 0;
        _repLostStage -= xp;
        GainReputation(xp, 0, boost: false); // devolver o perdido não é ganho novo
        return xp;
    }
}
