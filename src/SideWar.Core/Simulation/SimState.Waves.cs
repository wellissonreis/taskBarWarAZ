using SideWar.Core.Content;
using SideWar.Core.Events;
using SideWar.Core.Waves;

namespace SideWar.Core.Simulation;

/// <summary>Agenda da onda ativa e spawn (SDD-11 RF-11.4 passo 1; SDD-27 RF-27.7).</summary>
public sealed partial class SimState
{
    private WavePlan? _plan;
    private int _nextSpawn;
    private int _farmWave;

    /// <summary>Onda repetida em farm enquanto o chefe espera em Ausente (SDD-25 RF-25.4); 0 = nenhuma. No hash.</summary>
    public int FarmWave => _farmWave;

    /// <summary>
    /// Modo Ausente (SDD-25 RF-25.4): com a onda de chefe retida (<see cref="BossHold"/>, sem agenda ativa), joga a onda
    /// <paramref name="wave"/> (= w − 1, vinda de <c>AbsentMode.WaveToPlay</c>) em farm. <see cref="Wave"/> e a onda máxima não
    /// mudam; ganhos e HP seguem a onda de farm. Ao fim da onda (descanso) a retenção do chefe é reavaliada: em Ausente o
    /// host chama de novo (loop); em Presente o encontro começa. Só entre ticks. Devolve false se não se aplica.
    /// </summary>
    public bool StartFarmWave(SimContext ctx, int wave)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (InTick) throw new InvalidOperationException("StartFarmWave só entre ticks.");
        if (!_bossHold || _plan is not null || wave < 1 || wave >= Wave) return false;
        var stages = ctx.Content.Stages;
        int stageIndex = Math.Clamp(Stage - 1, 0, stages.Length - 1);
        _plan = ctx.Waves.Build(wave, stages[stageIndex].Waves, stageIndex, _regionHp);
        _nextSpawn = 0;
        WaveClock = 0f;
        Rest = 0f;
        _farmWave = wave;
        BaseHp = MaxBaseHp;
        RefreshRewardFactors(ctx, wave);
        Events.Add(new SimEvent(SimEventType.WaveStarted, 0f, 0f, wave, Stage));
        return true;
    }

    /// <summary>Agenda da onda ativa (null antes da primeira onda). Entra no <c>StateHash</c> (T3.7).</summary>
    public WavePlan? ActivePlan => _plan;

    /// <summary>Índice do próximo spawn pendente em <see cref="ActivePlan"/>.</summary>
    public int NextSpawnIndex => _nextSpawn;

    /// <summary>Todos os spawns da onda ativa já saíram (ou não há onda). A <c>PeakQueue</c> (SDD-26) será somada pela etapa de eventos.</summary>
    public bool WaveFullySpawned =>
        (_plan is null || _nextSpawn >= _plan.Spawns.Length) && _peakQueue == 0 && (_sched?.ExtrasPending ?? 0) == 0;

    /// <summary>
    /// Monta a agenda de <see cref="Wave"/> na fase <see cref="Stage"/> e zera <c>WaveClock</c>/<c>Rest</c>.
    /// Chamada pela própria ordem do tick quando não há onda ativa; T3.8 a chama ao avançar/recuar de onda.
    /// </summary>
    public void StartWave(SimContext ctx, double regionHpMultiplier = 1.0)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var stages = ctx.Content.Stages;
        int stageIndex = Math.Clamp(Stage - 1, 0, stages.Length - 1);
        _regionHp = regionHpMultiplier;
        _plan = ctx.Waves.Build(Wave, stages[stageIndex].Waves, stageIndex, regionHpMultiplier);
        _nextSpawn = 0;
        WaveClock = 0f;
        Rest = 0f;
        RefreshRewardFactors(ctx, Wave);
    }

    /// <summary>Multiplicador de HP da região da última <see cref="StartWave"/> (persistido pelo SDD-14).</summary>
    public double RegionHpMultiplier => _regionHp;

    /// <summary>
    /// Restore (SDD-14, T6.9a): reconstrói a agenda e os relógios. <paramref name="farmWave"/> &gt; 0 = onda de farm de Ausente
    /// (<see cref="StartFarmWave"/>); <paramref name="hasPlan"/> false = sem agenda ativa (descanso/espera de chefe).
    /// Não mexe no núcleo, em <see cref="Wave"/> nem em eventos. Chamar antes de <c>RestoreEvents</c>. Só entre ticks.
    /// </summary>
    public void RestoreWave(SimContext ctx, double regionHpMultiplier, bool hasPlan, int farmWave, int nextSpawn, float waveClock, float rest)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (InTick) throw new InvalidOperationException("RestoreWave só entre ticks.");
        _regionHp = regionHpMultiplier;
        if (!hasPlan)
        {
            _plan = null;
            _nextSpawn = 0;
            _farmWave = 0;
            RefreshRewardFactors(ctx, Wave);
        }
        else if (farmWave > 0)
        {
            var stages = ctx.Content.Stages;
            int stageIndex = Math.Clamp(Stage - 1, 0, stages.Length - 1);
            _plan = ctx.Waves.Build(farmWave, stages[stageIndex].Waves, stageIndex, regionHpMultiplier);
            _farmWave = farmWave;
            RefreshRewardFactors(ctx, farmWave);
            RestoreNextSpawn(nextSpawn);
        }
        else
        {
            StartWave(ctx, regionHpMultiplier);
            _farmWave = 0;
            RestoreNextSpawn(nextSpawn);
        }
        WaveClock = waveClock;
        Rest = rest;
    }

    /// <summary>Descarta a agenda (queda do núcleo, SDD-27 RF-27.8; usado por T3.8).</summary>
    public void ClearWave()
    {
        _farmWave = 0;
        _plan = null;
        _nextSpawn = 0;
        WaveClock = 0f;
        Rest = 0f;
    }

    /// <summary>Passo 1: avança o relógio e solta os spawns vencidos enquanto houver espaço no campo (limite 44).</summary>
    private void SpawnSystem(SimContext ctx)
    {
        if (_plan is null)
        {
            if (!_bossHold) BeginWave(ctx);
            if (_bossHold && !ReleaseBossHold(ctx)) return;   // chefe esperando no portal (SDD-26 RF-26.4)
        }
        var plan = _plan!;
        WaveClock += SimContext.Dt;
        int limit = ctx.Rules.MaxEnemiesOnField;
        while (_nextSpawn < plan.Spawns.Length
               && plan.Spawns[_nextSpawn].Time <= WaveClock
               && Enemies.Count < limit)
        {
            if (!SpawnEnemy(ctx, plan.Spawns[_nextSpawn])) break;
            _nextSpawn++;
        }
    }

    private bool SpawnEnemy(SimContext ctx, in WaveSpawn spawn)
    {
        if (!SimIds.TryEnemyKind(spawn.Enemy.Id, out var kind)) return false;
        // Acerto no chefe: HP ×0,4 derivado do ledger, consumido uma única vez no spawn (SDD-26 RF-26.7).
        bool bonus = kind == EnemyKind.Boss && _sched is not null && Ledger.HasPendingBonus(CurrentBossKey);
        double hp = bonus ? spawn.Hp * QuestionLedger.CorrectBossHpMultiplier : spawn.Hp;
        if (!AddEnemy(spawn.Enemy, kind, hp)) return false;
        if (bonus) Ledger.TryConsumeBonus(CurrentBossKey);
        return true;
    }

    private bool AddEnemy(EnemyDef def, EnemyKind kind, double hp)
    {
        float speed = (float)def.Speed;
        var e = new Enemy
        {
            Id = NextId,
            Kind = kind,
            X = _layout.SpawnX,
            Speed = speed,
            SpawnSpeed = speed,
            Hp = hp,
            MaxHp = hp,
            Armor = (short)def.Armor,
            Flags = kind == EnemyKind.Boss ? EnemyFlags.Boss : EnemyFlags.None,
            LastHit = TowerType.None,
        };
        if (Enemies.Add(e).IsNone) return false;
        TakeId();
        return true;
    }
}
