namespace SideWar.Core.Simulation;

/// <summary>Dano, armadura, chilled, splash, super hit e eventos (SDD-11 RF-11.6, ADR-027, ordem de ADR-031).</summary>
public sealed partial class SimState
{
    /// <summary>Eventos de simulação para a renderização (T4.6); não entram no <c>StateHash</c>.</summary>
    public SimEventBuffer Events { get; } = new();

    /// <summary>
    /// Fator de pico de demanda (SDD-26 RF-26.3: ×3 durante o pico), passo 5 da ordem de dano. 1 = sem pico.
    /// SDD-26 escreve; T3.7 deve incluí-lo no hash se for estado persistente.
    /// </summary>
    public double PeakDamageFactor { get; set; } = 1.0;

    /// <summary>Quantidade de torres Raio com slot (base do bônus do Scale Set; ADR-032: SEM SLOT não conta).</summary>
    public int CountActiveBolts()
    {
        int n = 0;
        for (int i = 0; i < Towers.Capacity; i++)
        {
            if (!Towers.IsAlive(i)) continue;
            ref readonly var t = ref Towers.At(i);
            if (t.Type == TowerType.Bolt && TowerHasSlot(in t)) n++;
        }
        return n;
    }

    /// <summary>
    /// Dano final contra um alvo, na ordem fixa de ADR-031 (tudo em <c>double</c>): dano(nível) [× Scale Set] → fator
    /// (super hit ×2 ou área ×0,6) → Canhão×runner ×0,5 → chilled ×1,25 (exceto Gelo) → pico → menos armadura → <c>max(1, floor)</c>.
    /// </summary>
    internal double ComputeDamage(SimContext ctx, TowerType type, double baseDamage, double factor, in Enemy target)
    {
        var r = ctx.Rules;
        double d = baseDamage;
        d *= factor;
        if (type == TowerType.Cannon && target.Kind == EnemyKind.Runner) d *= r.CannonVsRunner;
        if ((target.Flags & EnemyFlags.Chilled) != 0 && type != TowerType.Frost) d *= r.ChilledBonus;
        d *= PeakDamageFactor;
        d -= target.Armor;
        return Math.Max(1.0, Math.Floor(d));
    }
}
