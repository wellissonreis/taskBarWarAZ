namespace SideWar.Core.Td;

/// <summary>
/// Reputação (XP de patente, REPUTACAO): na corrida persistente a vida do núcleo não existe como derrota. Inimigo que chega à base tira XP,
/// vencer onda e fase dá XP. Os valores são do Tech Lead (ver REPORT); a patente vem de <see cref="TdRank"/> sobre o XP acumulado no perfil.
/// </summary>
public static class TdReputation
{
    /// <summary>XP perdido por ponto de dano que o inimigo daria ao núcleo (antes: 1 ponto de integridade de 20).</summary>
    public const int XpPerCoreDamage = 25;
    /// <summary>Teto do escudo de XP (runas + árvore de selos), em %.</summary>
    public const int MaxShieldPercent = 75;
    /// <summary>Cada ponto de nó de selos "xpShield" vale tantos % de XP perdida a menos.</summary>
    public const int MetaShieldPercentPerPoint = 3;

    /// <summary>XP por onda vencida: cresce com a onda e a fase; onda que vazou paga metade.</summary>
    public static int WaveXp(int stageNumber, int wave, bool leaked)
    {
        long xp = 30 + 2L * Math.Max(1, wave) + 10L * (Math.Max(1, stageNumber) - 1);
        return (int)Math.Min(100_000, leaked ? xp / 2 : xp);
    }

    /// <summary>XP bônus ao concluir uma fase da corrida: base + por fase, e por estrela (1 a 3).</summary>
    public static int StageClearXp(int stageNumber, int stars) =>
        (int)Math.Min(100_000, 100L + 20L * (Math.Max(1, stageNumber) - 1) + 50L * Math.Clamp(stars, 1, 3));

    public static int XpLost(int coreDamage, int shieldPercent)
    {
        shieldPercent = Math.Clamp(shieldPercent, 0, MaxShieldPercent);
        return (int)((long)Math.Max(0, coreDamage) * XpPerCoreDamage * (100 - shieldPercent) / 100);
    }
}

public sealed partial class TdMatchState
{
    /// <summary>XP de reputação ganho nesta partida (ondas, fases, recuperações).</summary>
    public long ReputationGainedTotal { get; private set; }
    /// <summary>XP de reputação perdido nesta partida (inimigos que chegaram à base).</summary>
    public long ReputationLostTotal { get; private set; }

    /// <summary>Escudo atual de XP perdida, em % (runas + árvore de selos, limitado).</summary>
    public int XpShieldPercent => Math.Min(TdReputation.MaxShieldPercent, Rune("xpShield") + _metaShield * TdReputation.MetaShieldPercentPerPoint);

    private void LoseReputation(int coreDamage)
    {
        int xp = TdReputation.XpLost(coreDamage, XpShieldPercent);
        if (xp <= 0) return;
        _repLostStage += xp;
        ReputationLostTotal += xp;
        Emit(TdEventType.BaseAttacked, xp, coreDamage);
    }

    private void GainReputation(int xp, int wave)
    {
        if (xp <= 0) return;
        ReputationGainedTotal += xp;
        Emit(TdEventType.ReputationGained, xp, wave);
    }

    private void WinWaveReputation(int wave, bool leaked) => GainReputation(TdReputation.WaveXp(_stageNumber, wave, leaked), wave);

    /// <summary>Devolve XP perdido nesta fase (Backup/Snapshot): <paramref name="coreDamageUnits"/> × XP por dano, limitado ao perdido. Retorna o XP recuperado.</summary>
    private int RecoverReputation(int coreDamageUnits)
    {
        int xp = Math.Min(_repLostStage, Math.Max(0, coreDamageUnits) * TdReputation.XpPerCoreDamage);
        if (xp <= 0) return 0;
        _repLostStage -= xp;
        GainReputation(xp, 0);
        return xp;
    }
}
