using SideWar.Core.Content;

namespace SideWar.Core.Simulation;

/// <summary>
/// Estado completo e mutável da simulação (SDD-11). Só dados: o tick, os comandos, o hash e as fases
/// são adicionados por T3.5–T3.8 em arquivos próprios (a classe é <c>partial</c>), sem alterar este.
/// Nada aqui lê relógio, monitor ou conteúdo: o conteúdo é passado por quem calcula.
/// </summary>
public sealed partial class SimState
{
    private StripLayout _layout;

    public SimState(StripLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
        Level = 1;
        Wave = 1;
        Stage = 1;
        NextId = 1;
    }

    /// <summary>Estado inicial de uma partida nova: créditos iniciais e núcleo cheio (economy.json), nível 1, fase 1, onda 1.</summary>
    public static SimState CreateInitial(GameContent content, StripLayout layout, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(content);
        var s = new SimState(layout)
        {
            Credits = content.Economy.StartingCredits,
            MaxBaseHp = content.Economy.Core.BaseHp,
            BaseHp = content.Economy.Core.BaseHp,
            };
        s._rng = rng;
        return s;
    }

    // ---- tempo e progresso (T3.5/T3.8 escrevem; a fase de jogo do SDD-10 fica no GameHost) ----
    /// <summary>Ticks completos executados (SimTime = Tick / 30).</summary>
    public long Tick { get; set; }
    /// <summary>Fase atual, 1-based (SDD-27 RF-27.9; <c>stages.json</c> id).</summary>
    public int Stage { get; set; }
    /// <summary>Onda atual na fase, 1-based.</summary>
    public int Wave { get; set; }
    /// <summary>Quedas do núcleo (RF-27.8).</summary>
    public int Falls { get; set; }

    // ---- economia: long saturante (SDD-27 RF-27.1); use <see cref="AddCredits"/>/<see cref="AddXp"/> ----
    public long Credits { get; set; }
    /// <summary>Resto fracionário de créditos (<c>carry</c> do hash, SDD-11 RF-11.12); a regra é de T3.6/T3.8.</summary>
    public double Carry { get; set; }
    public long Reservas { get; set; }
    public long Xp { get; set; }
    public int Level { get; set; }

    // ---- núcleo ----
    public int BaseHp { get; set; }
    public int MaxBaseHp { get; set; }

    // ---- relógios de onda (preenchidos por T3.5/T3.8; a agenda da onda é estendida em partial) ----
    public float WaveClock { get; set; }
    public float Rest { get; set; }

    /// <summary>Próximo <c>Id</c> monotônico global de torre ou inimigo.</summary>
    public int NextId { get; set; }

    private Rng _rng;

    /// <summary>PCG32; consumir altera o hash. Use <c>state.Rng.NextUInt()</c> (por referência).</summary>
    public ref Rng Rng => ref _rng;

    public EnemyStore Enemies { get; } = new();
    public TowerStore Towers { get; } = new();

    /// <summary>Layout vigente (ADR-026): só muda por <see cref="ApplyLayout"/>.</summary>
    public StripLayout Layout => _layout;

    /// <summary>Verdadeiro entre <see cref="BeginTick"/> e <see cref="EndTick"/>.</summary>
    public bool InTick { get; private set; }

    public void BeginTick()
    {
        if (InTick) throw new InvalidOperationException("Tick já em andamento.");
        InTick = true;
    }

    public void EndTick()
    {
        if (!InTick) throw new InvalidOperationException("Nenhum tick em andamento.");
        InTick = false;
        Tick++;
    }

    public void AddCredits(long amount) => Credits = Sat.Add(Credits, amount);
    public void AddXp(long amount) => Xp = Sat.Add(Xp, amount);

    /// <summary>Aloca e devolve o próximo <c>Id</c>.</summary>
    public int TakeId() => NextId++;

    /// <summary>
    /// Aplica um <c>LayoutChanged</c> (ADR-026), <b>só entre ticks</b> (lança durante um tick).
    /// Inimigos com <c>X</c> além do novo <c>SpawnX</c> são limitados a ele, sem tocar nos demais (ADR-031, DP-11.1).
    /// Torres com slot fora do novo layout ficam SEM SLOT (<see cref="TowerHasSlot"/>); nada é removido.
    /// </summary>
    public void ApplyLayout(StripLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (InTick) throw new InvalidOperationException("LayoutChanged só pode ser aplicado entre ticks.");
        _layout = layout;
        var enemies = Enemies;
        for (int i = 0; i < enemies.Capacity; i++)
        {
            if (!enemies.IsAlive(i)) continue;
            ref var e = ref enemies.At(i);
            if (e.X > layout.SpawnX) e.X = layout.SpawnX;
        }
    }

    /// <summary>False = SEM SLOT (a torre não dispara, não conta bônus; SDD-11 RF-11.2, ADR-032).</summary>
    public bool TowerHasSlot(in Tower tower) => _layout.HasSlot(tower.SlotIndex);

    /// <summary>X da torre no layout vigente (<c>SlotX[SlotIndex]</c>); NaN se SEM SLOT.</summary>
    public float TowerX(in Tower tower) => TowerHasSlot(tower) ? _layout.SlotXAt(tower.SlotIndex) : float.NaN;

    /// <summary>Handle da torre que ocupa o slot (inclui torres SEM SLOT com o mesmo índice), ou <see cref="Handle.None"/>. O(16).</summary>
    public Handle FindTowerAtSlot(int slotIndex)
    {
        for (int i = 0; i < Towers.Capacity; i++)
            if (Towers.IsAlive(i) && Towers.At(i).SlotIndex == slotIndex) return Towers.HandleAt(i);
        return Handle.None;
    }
}
