using SideWar.Core.Simulation;

namespace SideWar.Core.Bot;

/// <summary>
/// Bot de sanidade idle (SDD-12 RF-12.10; só testes e benchmarks). Determinístico e usa apenas a API pública da
/// simulação (<see cref="SimState.Apply"/>): a cada chamada, constrói em slots livres na ordem
/// <c>bolt, bolt, frost, bolt, cannon, bolt, …</c> (Scale Set no lugar do último Raio do ciclo após desbloquear);
/// sem slot livre, melhora a torre com slot de menor nível (empate: menor Id). Chamar entre ticks.
/// </summary>
public sealed class SanityBot
{
    private static readonly TowerType[] Cycle =
        { TowerType.Bolt, TowerType.Bolt, TowerType.Frost, TowerType.Bolt, TowerType.Cannon, TowerType.Bolt };

    private const int MaxActionsPerCall = 16;
    private readonly SimContext _ctx;
    private int _built;

    public SanityBot(SimContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
    }

    /// <summary>Layout fixo de teste: fallback virtual de 8 slots em 1440 DIP (SDD-21 RF-21.7; centros 662 + 48·i).</summary>
    public static StripLayout FallbackLayout()
    {
        var slots = new float[8];
        for (int i = 0; i < slots.Length; i++) slots[i] = 662f + 48f * i;
        return StripLayout.Create(1240f, StripLayout.DefaultLeakX, 10f, 1220f, slots);
    }

    /// <summary>Executa as ações possíveis com os créditos atuais; devolve quantas foram aplicadas.</summary>
    public int Act(SimState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        int done = 0;
        while (done < MaxActionsPerCall)
        {
            int slot = FirstFreeSlot(state);
            CommandResult r;
            bool isBuild = slot >= 0;
            if (isBuild)
            {
                r = state.Apply(_ctx, new BuildTower(NextType(state), slot));
                if (r.IsOk) _built++;
            }
            else
            {
                int id = LowestLevelTowerId(state);
                if (id < 0) break;
                r = state.Apply(_ctx, new UpgradeTower(id));
            }
            if (!r.IsOk) break;
            done++;
        }
        return done;
    }

    private TowerType NextType(SimState state)
    {
        int pos = _built % Cycle.Length;
        var type = Cycle[pos];
        if (pos == Cycle.Length - 1 && state.IsTowerUnlocked(_ctx, TowerType.ScaleSet)) type = TowerType.ScaleSet;
        return type;
    }

    private static int FirstFreeSlot(SimState state)
    {
        int n = state.Layout.SlotCount;
        for (int i = 0; i < n; i++)
            if (state.FindTowerAtSlot(i).IsNone) return i;
        return -1;
    }

    private static int LowestLevelTowerId(SimState state)
    {
        int bestId = -1, bestLevel = int.MaxValue;
        var towers = state.Towers;
        for (int i = 0; i < towers.Capacity; i++)
        {
            if (!towers.IsAlive(i)) continue;
            ref readonly var t = ref towers.At(i);
            if (!state.TowerHasSlot(in t)) continue;
            if (t.Level < bestLevel || (t.Level == bestLevel && t.Id < bestId)) { bestLevel = t.Level; bestId = t.Id; }
        }
        return bestId;
    }
}
