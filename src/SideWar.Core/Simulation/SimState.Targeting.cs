namespace SideWar.Core.Simulation;

/// <summary>Mira 1D (SDD-11 RF-11.5, SDD-27 RF-27.4): distância <c>|X − xTorre|</c>, alcance em slots × 48 DIP.</summary>
public sealed partial class SimState
{
    /// <summary>
    /// Índice (no <c>EnemyStore</c>) do alvo da torre, ou -1. Candidatos: <c>Hp &gt; 0</c>, <c>X &gt; LeakX</c>,
    /// <c>|X − xTorre| ≤ alcanceDip</c>. Empate total: menor <c>Id</c>. Sem alocação.
    /// </summary>
    public int PickTarget(Targeting mode, float towerX, float rangeDip)
    {
        float leak = _layout.LeakX;
        int best = -1;
        for (int i = 0; i < Enemies.Capacity; i++)
        {
            if (!Enemies.IsAlive(i)) continue;
            ref readonly var e = ref Enemies.At(i);
            if (!(e.Hp > 0) || !(e.X > leak) || MathF.Abs(e.X - towerX) > rangeDip) continue;
            if (best < 0 || Better(mode, in e, in Enemies.At(best))) best = i;
        }
        return best;
    }

    // First: menor X · Last: maior X · Strong: maior Hp (empate menor X) · Weak: menor Hp (empate menor X) · total: menor Id.
    private static bool Better(Targeting mode, in Enemy a, in Enemy b)
    {
        int c = mode switch
        {
            Targeting.Last => b.X.CompareTo(a.X),
            Targeting.Strong => a.Hp != b.Hp ? b.Hp.CompareTo(a.Hp) : a.X.CompareTo(b.X),
            Targeting.Weak => a.Hp != b.Hp ? a.Hp.CompareTo(b.Hp) : a.X.CompareTo(b.X),
            _ => a.X.CompareTo(b.X),
        };
        return c != 0 ? c < 0 : a.Id < b.Id;
    }
}
