using SideWar.Core.Content;

namespace SideWar.Core.Simulation;

/// <summary>
/// Núcleo, vazamento, queda e recuo, nível/XP, fases e estrelas (SDD-27 RF-27.8, RF-27.9; SDD-11 RF-11.4 passos 5 a 9).
/// </summary>
public sealed partial class SimState
{
    /// <summary>Capacidade das tabelas por fase (o conteúdo tem 10 fases).</summary>
    public const int MaxStages = 32;

    private readonly int[] _lastBossWave = new int[MaxStages + 1];
    private readonly byte[] _stageStars = new byte[MaxStages + 1];
    private double _goldFactor = 1.0;
    private double _xpFactor = 1.0;

    /// <summary>Maior fase desbloqueada (RF-27.9). Entra no hash.</summary>
    public int UnlockedStage { get; set; } = 1;

    /// <summary>Quedas na passagem atual pela fase (<c>StageRun.Falls</c>); zera ao entrar na fase.</summary>
    public int StageFalls { get; set; }

    /// <summary>Maior onda alcançada na passagem atual (<c>StageRun.ondaMax</c>).</summary>
    public int StageWaveMax { get; set; } = 1;

    /// <summary>Perguntas de chefe acertadas na passagem atual (SDD-26 escreve; zera ao entrar na fase).</summary>
    public int BossQuestionsCorrect { get; set; }

    /// <summary>Multiplicador de presença (SDD-25 RF-25.3); 1 = sem efeito. Escrito de fora; entra no hash.</summary>
    public double PresenceMultiplier { get; set; } = 1.0;

    /// <summary>Resto fracionário de XP (mesma regra do <see cref="Carry"/> de créditos).</summary>
    public double XpCarry { get; set; }

    /// <summary>Última onda de chefe concluída na fase (0 = nenhuma). RF-27.8 "chefe vencido".</summary>
    public int LastBossWaveBeaten(int stage) => (uint)stage <= MaxStages ? _lastBossWave[stage] : 0;

    /// <summary>Estrelas consolidadas da fase (0 a 3; nunca diminuem).</summary>
    public int StarsOf(int stage) => (uint)stage <= MaxStages ? _stageStars[stage] : 0;

    /// <summary>Restore (SDD-14): grava o progresso persistido por fase.</summary>
    public void RestoreStageProgress(int stage, int lastBossWave, int stars)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(stage, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(stage, MaxStages);
        _lastBossWave[stage] = Math.Max(0, lastBossWave);
        _stageStars[stage] = (byte)Math.Clamp(stars, 0, 3);
    }

    /// <summary>HP máximo do núcleo: <c>100 + 5 × (nível − 1)</c> (RF-27.8).</summary>
    public static int CoreHpForLevel(EconomyDef eco, int level)
    {
        long hp = eco.Core.BaseHp + (long)eco.Core.HpPerLevel * Math.Max(0, level - 1);
        return (int)Math.Min(hp, int.MaxValue);
    }

    /// <summary>
    /// Estrelas de uma conclusão (RF-27.9), da maior para a menor: 3 = acertou todas as perguntas (≥ 1) e 0 quedas;
    /// 2 = ≥ 2 acertos; 1 = concluída.
    /// </summary>
    public static int ComputeStars(int correct, int totalBossQuestions, int falls)
    {
        if (totalBossQuestions > 0 && correct >= totalBossQuestions && falls == 0) return 3;
        if (correct >= 2) return 2;
        return 1;
    }

    /// <summary>Fator iterativo b^e (sem <c>Math.Pow</c>), igual ao do <c>IdleWaveBuilder</c>.</summary>
    private static double IterPow(double b, int e)
    {
        double r = 1.0;
        for (int i = 0; i < e; i++) r *= b;
        return r;
    }

    private void RefreshRewardFactors(SimContext ctx)
    {
        var eco = ctx.Content.Economy;
        _goldFactor = IterPow(eco.GoldGrowth, Wave - 1);
        _xpFactor = IterPow(eco.XpGrowth, Wave - 1);
    }

    /// <summary>Início de onda "de jogo": núcleo restaurado, plano novo, evento <c>WaveStarted</c> (não usado no restore).</summary>
    private void BeginWave(SimContext ctx)
    {
        BaseHp = MaxBaseHp;
        StartWave(ctx);
        if (Wave > StageWaveMax) StageWaveMax = Wave;
        Events.Add(new SimEvent(SimEventType.WaveStarted, 0f, 0f, Wave, Stage));
    }

    /// <summary>Passo 5: remove inimigos com HP ≤ 0 em ordem de Id, paga ouro e XP e emite <c>Kill</c>.</summary>
    private void DeathSystem(SimContext ctx)
    {
        Span<int> order = stackalloc int[EnemyStore.CapacityConst];
        int n = Enemies.CopyIndicesByIdAscending(order);
        for (int k = 0; k < n; k++)
        {
            int idx = order[k];
            ref var e = ref Enemies.At(idx);
            if (e.Hp > 0) continue;
            var def = ctx.Enemy(e.Kind);
            double gold = def.Gold * _goldFactor;
            double xp = def.Xp * _xpFactor;
            Events.Add(new SimEvent(SimEventType.Kill, e.X, 0f, gold, (int)e.Kind));
            Enemies.Remove(Enemies.HandleAt(idx));
            PayCredits(gold * (1.0 + ctx.Content.Economy.ReservesBonus * Reservas) * PresenceMultiplier);
            PayXp(xp * PresenceMultiplier);
        }
    }

    private static long FloorToLong(double v) => v >= 9.2e18 ? long.MaxValue : v <= 0 ? 0 : (long)Math.Floor(v);

    private void PayCredits(double value)
    {
        double total = value + Carry;
        long gain = FloorToLong(total);
        Carry = total - gain;
        AddCredits(gain);
    }

    private void PayXp(double value)
    {
        double total = value + XpCarry;
        long gain = FloorToLong(total);
        XpCarry = total - gain;
        AddXp(gain);
    }

    /// <summary>Passo 6: <c>X −= Speed × dt</c>; ao chegar em <c>LeakX</c> causa o dano ao núcleo e o inimigo é removido.</summary>
    private void MovementSystem(SimContext ctx)
    {
        Span<int> order = stackalloc int[EnemyStore.CapacityConst];
        int n = Enemies.CopyIndicesByIdAscending(order);
        float leakX = _layout.LeakX;
        for (int k = 0; k < n; k++)
        {
            int idx = order[k];
            ref var e = ref Enemies.At(idx);
            e.X -= e.Speed * SimContext.Dt;
            if (e.X > leakX) continue;
            int dmg = ctx.Enemy(e.Kind).CoreDamage;
            Events.Add(new SimEvent(SimEventType.Leak, e.X, 0f, dmg, (int)e.Kind));
            Enemies.Remove(Enemies.HandleAt(idx));
            BaseHp -= dmg;
        }
    }

    /// <summary>Passo 7: <c>enquanto xp ≥ nível × 45</c> sobe de nível; o HP máximo do núcleo acompanha (valendo na próxima onda).</summary>
    private void LevelSystem(SimContext ctx)
    {
        var eco = ctx.Content.Economy;
        while (Level < int.MaxValue && Xp >= (long)Level * eco.XpPerLevel)
        {
            Xp -= (long)Level * eco.XpPerLevel;
            Level++;
            MaxBaseHp = CoreHpForLevel(eco, Level);
            Events.Add(new SimEvent(SimEventType.LevelUp, 0f, 0f, Level, 0));
        }
    }

    /// <summary>
    /// Passo 8: núcleo em 0 = queda. Descarta a onda e os inimigos, restaura o núcleo e recua
    /// <c>max(onda − 5, últimoChefeVencido + 1, 1)</c>; sem perda de créditos.
    /// </summary>
    private void FallSystem(SimContext ctx)
    {
        if (BaseHp > 0) return;
        Falls++;
        StageFalls++;
        Enemies.Clear();
        int fallback = LastBossWaveBeaten(Stage) + 1;
        Wave = Math.Max(Math.Max(Wave - ctx.Content.Economy.FallWaves, fallback), 1);
        ClearWave();
        BeginWave(ctx);
        Events.Add(new SimEvent(SimEventType.Fall, 0f, 0f, Wave, Falls));
    }

    /// <summary>
    /// Passo 9: com a agenda esgotada e o campo vazio, <c>Rest += dt</c> (senão zera); ao chegar em <c>waveRestSeconds</c>
    /// começa a próxima onda ou conclui a fase.
    /// </summary>
    private void CheckEnd(SimContext ctx)
    {
        if (!WaveFullySpawned || Enemies.Count != 0) { Rest = 0f; return; }
        Rest += SimContext.Dt;
        if (Rest < (float)ctx.Content.Economy.WaveRestSeconds) return;

        Events.Add(new SimEvent(SimEventType.WaveCleared, 0f, 0f, Wave, Stage));
        if (_plan is { IsBoss: true } && Stage <= MaxStages && Wave > _lastBossWave[Stage])
            _lastBossWave[Stage] = Wave;

        var stages = ctx.Content.Stages;
        int stageIndex = Math.Clamp(Stage - 1, 0, stages.Length - 1);
        int total = stages[stageIndex].Waves;
        if (Wave < total)
        {
            Wave++;
            BeginWave(ctx);
            return;
        }
        CompleteStage(ctx, stageIndex);
    }

    /// <summary>Conclusão da fase (RF-27.9): consolida estrelas, desbloqueia a seguinte e segue com torres e créditos, onda 1.</summary>
    private void CompleteStage(SimContext ctx, int stageIndex)
    {
        var stages = ctx.Content.Stages;
        int bossTotal = stages[stageIndex].Waves / ctx.Content.Waves.BossEvery;
        int stars = ComputeStars(BossQuestionsCorrect, bossTotal, StageFalls);
        if (Stage <= MaxStages && stars > _stageStars[Stage]) _stageStars[Stage] = (byte)stars;
        Events.Add(new SimEvent(SimEventType.StageCleared, 0f, 0f, Stage, stars));

        if (stageIndex + 1 < stages.Length)
        {
            Stage++;
            UnlockedStage = Math.Max(UnlockedStage, Stage);
            Wave = 1;
            StageFalls = 0;
            StageWaveMax = 1;
            BossQuestionsCorrect = 0;
            // fase 10: a partida fica em loop na última onda (farm) — a fase e a onda não mudam.
        }
        ClearWave();
        BeginWave(ctx);
    }

    partial void HashExtraSections(ref StateHasher h)
    {
        h.AddInt32(UnlockedStage);
        h.AddInt32(StageFalls);
        h.AddInt32(StageWaveMax);
        h.AddInt32(BossQuestionsCorrect);
        h.AddDouble(PresenceMultiplier);
        h.AddDouble(XpCarry);
        for (int i = 1; i <= MaxStages; i++)
        {
            h.AddInt32(_lastBossWave[i]);
            h.AddByte(_stageStars[i]);
        }
    }
}
