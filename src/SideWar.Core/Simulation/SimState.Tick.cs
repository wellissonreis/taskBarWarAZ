namespace SideWar.Core.Simulation;

/// <summary>
/// Ordem do tick (SDD-11 RF-11.4 vigente). Implementados aqui: passo 1 (spawn), passo 3 (torres, mira e acerto
/// instantâneo). Ficam como pontos de extensão, na ordem: 2 (fatores de evento, SDD-26), 5 DeathSystem (T3.6/T3.8),
/// 6 MovementSystem e vazamento (T3.8), 7 nível/XP (T3.8), 8 queda (T3.8), 9 CheckEnd (T3.8).
/// </summary>
public sealed partial class SimState
{
    /// <summary>Executa um tick completo (<c>BeginTick</c> … <c>EndTick</c>). O conteúdo vem de <paramref name="ctx"/>.</summary>
    public void Step(SimContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        BeginTick();
        try
        {
            SpawnSystem(ctx);          // 1
            EventsSystem(ctx);         // 2 (SDD-26: pico, caixa, chefe, extras)
            TowerSystem(ctx);          // 3 (4 removido: acerto instantâneo)
            DeathSystem(ctx);          // 5
            MovementSystem(ctx);       // 6 (inclui vazamento)
            LevelSystem(ctx);          // 7
            FallSystem(ctx);           // 8
            CheckEnd(ctx);             // 9
        }
        finally
        {
            EndTick();
        }
    }

    /// <summary>Passo 3: torres com slot, em ordem crescente de Id: cooldown, mira e acerto instantâneo.</summary>
    private void TowerSystem(SimContext ctx)
    {
        Span<int> order = stackalloc int[TowerStore.CapacityConst];
        int n = Towers.CopyIndicesByIdAscending(order);
        for (int k = 0; k < n; k++)
        {
            ref var tower = ref Towers.At(order[k]);
            if (!TowerHasSlot(in tower)) continue;           // SEM SLOT: não atira e não esfria
            var p = ctx.Tower(tower.Type);
            tower.Cooldown = MathF.Max(0f, tower.Cooldown - SimContext.Dt);
            int target = PickTarget(tower.Mode, _layout.SlotXAt(tower.SlotIndex), p.RangeDip);
            if (target < 0 || tower.Cooldown != 0f) continue;
            ApplyInstantHit(ctx, ref tower, target);
            tower.Cooldown = p.Interval;
        }
    }

    /// <summary>
    /// Acerto instantâneo (RF-11.6, ADR-027): super hit do Raio (contador por torre, só acertos reais), Scale Set
    /// (bônus por Raios ativos, reavaliado a cada disparo), dano na ordem de ADR-031, Gelo (chilled e lentidão com piso) e
    /// splash do Canhão (raio e fator de rules.json). Emite <c>Hit</c>, <c>SuperHit</c> e <c>Splash</c>. Não remove mortos (T3.8).
    /// </summary>
    private void ApplyInstantHit(SimContext ctx, ref Tower tower, int enemyIndex)
    {
        var rules = ctx.Rules;
        ref var enemy = ref Enemies.At(enemyIndex);
        double baseDmg = ctx.BaseDamage(tower.Type, tower.Level);
        if (tower.Type == TowerType.ScaleSet)
            baseDmg *= 1.0 + ctx.Tower(TowerType.ScaleSet).DamageBonusPerBolt * CountActiveBolts();

        bool superHit = false;
        if (tower.Type == TowerType.Bolt && ++tower.Hits >= rules.BoltSuperHitEvery)
        {
            tower.Hits = 0;
            superHit = true;
        }

        double dmg = ComputeDamage(ctx, tower.Type, baseDmg, superHit ? rules.BoltSuperHitMultiplier : 1.0, in enemy);
        enemy.Hp -= dmg;
        enemy.LastHit = tower.Type;
        float cx = enemy.X;
        Events.Add(new SimEvent(SimEventType.Hit, cx, 0f, dmg, (int)tower.Type));
        if (superHit) Events.Add(new SimEvent(SimEventType.SuperHit, cx, 0f, dmg, (int)tower.Type));

        if (tower.Type == TowerType.Frost)
        {
            enemy.Flags |= EnemyFlags.Chilled;
            enemy.Speed = MathF.Min(enemy.Speed,
                MathF.Max(enemy.Speed * (float)rules.FrostSlowFactor, enemy.SpawnSpeed * (float)rules.FrostSlowFloor));
        }
        else if (tower.Type == TowerType.Cannon)
        {
            float radius = (float)rules.SplashRadius;
            int others = 0;
            for (int i = 0; i < Enemies.Capacity; i++)
            {
                if (i == enemyIndex || !Enemies.IsAlive(i)) continue;
                ref var o = ref Enemies.At(i);
                if (!(o.Hp > 0) || MathF.Abs(o.X - cx) > radius) continue;
                o.Hp -= ComputeDamage(ctx, TowerType.Cannon, baseDmg, rules.SplashFactor, in o);
                o.LastHit = TowerType.Cannon;
                others++;
            }
            Events.Add(new SimEvent(SimEventType.Splash, cx, 0f, radius, others));
        }
    }
}
