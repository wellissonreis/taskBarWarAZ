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
    /// Aplica um <c>LayoutChanged</c> (ADR-026, ADR-034), <b>só entre ticks</b> (lança durante um tick).
    /// Inimigos com <c>X</c> além do novo <c>SpawnX</c> são limitados a ele, sem tocar nos demais (ADR-031, DP-11.1).
    /// As torres são reavaliadas em ordem crescente de <c>Id</c> (RF-11.3): inválida → <c>SEM POSIÇÃO</c>; <c>SEM POSIÇÃO</c> cuja
    /// posição gravada voltou a ser válida → posicionada (sem custo). Nada é removido.
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
        ReevaluateTowers();
        OnLayoutAppliedForEvents();
    }

    /// <summary>Reavaliação por <c>Id</c> crescente: só as torres de <c>Id</c> menor já reavaliadas contam na sobreposição.</summary>
    private void ReevaluateTowers()
    {
        Span<int> order = stackalloc int[TowerStore.CapacityConst];
        int n = Towers.CopyIndicesByIdAscending(order);
        for (int k = 0; k < n; k++) Towers.At(order[k]).Placed = false;
        for (int k = 0; k < n; k++)
        {
            ref var t = ref Towers.At(order[k]);
            int x = PlacementValidator.CenterX(t.Anchor, t.OffsetDip, _layout.Width);
            t.Placed = PlacementValidator.Validate(x, _layout, Towers) == PlacementReason.None;
        }
    }

    /// <summary>Centro X (DIP inteiro) da torre no layout vigente, pela âncora; vale também para torres SEM POSIÇÃO (posição gravada).</summary>
    public int TowerCenterX(in Tower tower) => PlacementValidator.CenterX(tower.Anchor, tower.OffsetDip, _layout.Width);

    /// <summary>
    /// Pendência de <c>AutoPlaceUnpositioned</c> (<c>migratedSlots</c>, DP-33.7): ligada pelo restore de um save migrado (TA.4),
    /// limpa pelo comando. Entra no hash.
    /// </summary>
    public bool MigratedSlotsPending { get; set; }
}
