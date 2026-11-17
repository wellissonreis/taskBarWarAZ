namespace SideWar.Core.Td;

// Feitos da partida, coroa, medalhas e pontuação (SDD-28 RF-28.21). Só contadores derivados dos comandos: ficam FORA do StateHash
// (não influenciam a simulação), então os goldens determinísticos permanecem iguais.
public sealed partial class TdMatchState
{
    private bool _sold;
    private int _earlyCalls;
    private int _peakTowers;

    /// <summary>Alguma torre foi vendida nesta partida.</summary>
    public bool SoldAnyTower => _sold;
    /// <summary>Alguma habilidade foi usada nesta partida.</summary>
    public bool UsedAnyAbility
    {
        get
        {
            for (int i = 0; i < _abilityUses.Length; i++)
                if (_abilityUses[i] > 0) return true;
            return false;
        }
    }
    /// <summary>Chamadas antecipadas aceitas (<c>CallNextWave</c>).</summary>
    public int EarlyCalls => _earlyCalls;
    /// <summary>Maior número de torres simultâneas já construídas (desafio Frugal).</summary>
    public int PeakTowers => _peakTowers;

    /// <summary>Segundos de combate (relógio do mundo, só avança em Onda).</summary>
    public double CombatSeconds => (Tick - _stageStartTick) / (double)TdWorld.TicksPerSecond;

    /// <summary>O desafio da fase (RF-28.10) foi cumprido: limite de torres, sem habilidades, sem chamada antecipada. O fator de créditos do Maratona não é aplicado.</summary>
    public bool ChallengeMet
    {
        get
        {
            var ch = Array.Find(_content.Stages.Rules.Challenges, c => c.Id == _stage.Challenge);
            if (ch is null) return false;
            if (ch.MaxTowers is int max && _peakTowers > max) return false;
            if (ch.NoAbilities && UsedAnyAbility) return false;
            if (ch.NoCallNext && _earlyCalls > 0) return false;
            return true;
        }
    }

    private TdMatchResult ComputeResult()
    {
        if (Phase != MatchPhase.Vitoria) return new(Phase, Stars, IntegrityLost);
        var s = BuildSummary(Stars, IntegrityLost);
        return new(Phase, Stars, IntegrityLost, s.Crown, s.Medals, s.Score);
    }

    /// <summary>Coroa, medalhas e pontuação da fase corrente (contadores por fase) para <paramref name="stars"/>/<paramref name="lost"/> dados.</summary>
    private TdStageSummary BuildSummary(int stars, int lost)
    {
        bool crown = lost == 0;
        var medals = TdMedals.None;
        if (!_sold) medals |= TdMedals.SemVender;
        if (!UsedAnyAbility) medals |= TdMedals.SemHabilidades;
        if (_waves.Length >= 2 && _earlyCalls >= _waves.Length - 1) medals |= TdMedals.Pressa;
        if (ChallengeMet) medals |= TdMedals.Desafio;
        int score = TdScoring.Compute(Integrity, CombatSeconds, _earlyCalls, Credits, stars, crown);
        return new(_stageNumber, _stage.Id, Cycle, stars, lost, crown, medals, score, CombatSeconds);
    }
}

/// <summary>
/// Pontuação determinística de uma partida vencida (RF-28.21). Inteiros puros, sem relógio real:
/// <c>soma = 1000 + 50 × integridade restante + max(0, 1000 − segundos de combate) + 100 × chamadas antecipadas + min(2000, créditos) / 4</c>;
/// <c>pontuação = soma × multiplicador%/100</c> com multiplicador 100 (1★), 110 (2★), 125 (3★) e +25 com coroa.
/// </summary>
public static class TdScoring
{
    public const int BasePoints = 1000;
    public const int PointsPerIntegrity = 50;
    public const int TimeBudgetSeconds = 1000;
    public const int PointsPerEarlyCall = 100;
    public const int CreditsCap = 2000;
    public const int CreditsDivisor = 4;
    public const int CrownMultiplierBonus = 25;

    public static int StarMultiplierPercent(int stars) => stars switch { >= 3 => 125, 2 => 110, _ => 100 };

    public static int Compute(int integrityLeft, double combatSeconds, int earlyCalls, long creditsLeft, int stars, bool crown)
    {
        long sum = BasePoints
            + (long)PointsPerIntegrity * Math.Max(0, integrityLeft)
            + Math.Max(0, TimeBudgetSeconds - (long)Math.Floor(Math.Max(0.0, combatSeconds)))
            + (long)PointsPerEarlyCall * Math.Max(0, earlyCalls)
            + Math.Min(CreditsCap, Math.Max(0L, creditsLeft)) / CreditsDivisor;
        long pct = StarMultiplierPercent(stars) + (crown ? CrownMultiplierBonus : 0);
        return (int)(sum * pct / 100);
    }
}
