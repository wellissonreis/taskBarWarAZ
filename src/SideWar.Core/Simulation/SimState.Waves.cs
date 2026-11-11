using SideWar.Core.Waves;

namespace SideWar.Core.Simulation;

/// <summary>Agenda da onda ativa e spawn (SDD-11 RF-11.4 passo 1; SDD-27 RF-27.7).</summary>
public sealed partial class SimState
{
    private WavePlan? _plan;
    private int _nextSpawn;

    /// <summary>Agenda da onda ativa (null antes da primeira onda). Entra no <c>StateHash</c> (T3.7).</summary>
    public WavePlan? ActivePlan => _plan;

    /// <summary>Índice do próximo spawn pendente em <see cref="ActivePlan"/>.</summary>
    public int NextSpawnIndex => _nextSpawn;

    /// <summary>Todos os spawns da onda ativa já saíram (ou não há onda). A <c>PeakQueue</c> (SDD-26) será somada pela etapa de eventos.</summary>
    public bool WaveFullySpawned => _plan is null || _nextSpawn >= _plan.Spawns.Length;

    /// <summary>
    /// Monta a agenda de <see cref="Wave"/> na fase <see cref="Stage"/> e zera <c>WaveClock</c>/<c>Rest</c>.
    /// Chamada pela própria ordem do tick quando não há onda ativa; T3.8 a chama ao avançar/recuar de onda.
    /// </summary>
    public void StartWave(SimContext ctx, double regionHpMultiplier = 1.0)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var stages = ctx.Content.Stages;
        int stageIndex = Math.Clamp(Stage - 1, 0, stages.Length - 1);
        _plan = ctx.Waves.Build(Wave, stages[stageIndex].Waves, stageIndex, regionHpMultiplier);
        _nextSpawn = 0;
        WaveClock = 0f;
        Rest = 0f;
        RefreshRewardFactors(ctx);
    }

    /// <summary>Descarta a agenda (queda do núcleo, SDD-27 RF-27.8; usado por T3.8).</summary>
    public void ClearWave()
    {
        _plan = null;
        _nextSpawn = 0;
        WaveClock = 0f;
        Rest = 0f;
    }

    /// <summary>Passo 1: avança o relógio e solta os spawns vencidos enquanto houver espaço no campo (limite 44).</summary>
    private void SpawnSystem(SimContext ctx)
    {
        if (_plan is null) BeginWave(ctx);
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
        float speed = (float)spawn.Enemy.Speed;
        var e = new Enemy
        {
            Id = NextId,
            Kind = kind,
            X = _layout.SpawnX,
            Speed = speed,
            SpawnSpeed = speed,
            Hp = spawn.Hp,
            MaxHp = spawn.Hp,
            Armor = (short)spawn.Enemy.Armor,
            Flags = kind == EnemyKind.Boss ? EnemyFlags.Boss : EnemyFlags.None,
            LastHit = TowerType.None,
        };
        if (Enemies.Add(e).IsNone) return false;
        TakeId();
        return true;
    }
}
