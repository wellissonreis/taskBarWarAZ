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
            // 2: fatores de evento (SDD-26) — fora do escopo
            TowerSystem(ctx);          // 3 (4 removido: acerto instantâneo)
            // 5 DeathSystem, 6 MovementSystem, 7 nível/XP, 8 queda, 9 CheckEnd: T3.6/T3.8
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
    /// PONTO DE DANO (T3.6 completa): hoje aplica <c>max(1, floor(dano(nível) − armadura))</c> e grava <c>LastHit</c>.
    /// T3.6 troca o corpo pela ordem fixa de ADR-031 (super hit, Canhão×runner, chilled, pico, armadura), Gelo, splash,
    /// Scale Set e eventos <c>Hit</c>/<c>SuperHit</c>/<c>Splash</c>. A assinatura (torre por ref, índice do alvo) é o contrato.
    /// </summary>
    private void ApplyInstantHit(SimContext ctx, ref Tower tower, int enemyIndex)
    {
        ref var enemy = ref Enemies.At(enemyIndex);
        double d = ctx.BaseDamage(tower.Type, tower.Level) - enemy.Armor;
        enemy.Hp -= Math.Max(1.0, Math.Floor(d));
        enemy.LastHit = tower.Type;
    }
}
