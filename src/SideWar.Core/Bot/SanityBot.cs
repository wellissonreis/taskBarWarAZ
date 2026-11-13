using SideWar.Core.Simulation;

namespace SideWar.Core.Bot;

/// <summary>
/// Bot de sanidade idle (SDD-12 RF-12.10; só testes e benchmarks). Determinístico e usa apenas a API pública da
/// simulação (<see cref="SimState.Apply"/>): a cada chamada, constrói na primeira posição X livre (zonas da esquerda para a direita,
/// passo de 44 DIP) na ordem
/// <c>bolt, bolt, frost, bolt, cannon, bolt, …</c> (Scale Set no lugar do último Raio do ciclo após desbloquear);
/// sem posição livre, melhora a torre posicionada de menor nível (empate: menor Id). Chamar entre ticks.
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

    /// <summary>
    /// Layout fixo de teste (ADR-034): 1440 DIP, portal em <c>W − 80</c> (SpawnX = W − 46), uma zona livre <c>[640, 1032)</c>
    /// que comporta exatamente 8 torres de 44 DIP (mesma capacidade da versão com slots).
    /// </summary>
    public static StripLayout FallbackLayout()
        => StripLayout.Create(1440, 1394f, StripLayout.DefaultLeakX, 10f, 1360f, TowerHost.Taskbar, [new FreeZone(640, 1032)]);

    /// <summary>Executa as ações possíveis com os créditos atuais; devolve quantas foram aplicadas.</summary>
    public int Act(SimState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        int done = 0;
        while (done < MaxActionsPerCall)
        {
            int x = FirstFreeX(state);
            CommandResult r;
            bool isBuild = x >= 0;
            if (isBuild)
            {
                r = state.Apply(_ctx, new BuildTower(NextType(state), x));
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

    private static int FirstFreeX(SimState state)
    {
        if (state.Towers.Count >= TowerStore.MaxTowers) return -1;
        var layout = state.Layout;
        foreach (var z in layout.FreeZones)
            for (int x = z.X0 + PlacementValidator.HalfBox; x + PlacementValidator.HalfBox <= z.X1; x += PlacementValidator.BoxWidth)
                if (PlacementValidator.Validate(x, layout, state.Towers) == PlacementReason.None) return x;
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
            if (!t.Placed) continue;
            if (t.Level < bestLevel || (t.Level == bestLevel && t.Id < bestId)) { bestLevel = t.Level; bestId = t.Id; }
        }
        return bestId;
    }
}
