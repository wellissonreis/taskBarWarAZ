namespace SideWar.Core.Simulation;

/// <summary>Struct de entidade com <c>Id</c> monotônico global (ordem de iteração determinística).</summary>
public interface IEntity { int Id { get; } }

/// <summary>Inimigo (SDD-11 RF-11.2). <c>X</c> = centro do sprite em DIP; marcha para a esquerda.</summary>
public struct Enemy : IEntity
{
    public int Id { get; set; }
    public EnemyKind Kind { get; set; }
    public float X { get; set; }
    public float Speed { get; set; }
    public float SpawnSpeed { get; set; }
    public double Hp { get; set; }
    public double MaxHp { get; set; }
    public short Armor { get; set; }
    public EnemyFlags Flags { get; set; }
    public TowerType LastHit { get; set; }
}

/// <summary>Âncora do X da torre (SDD-21 RF-21.8, DP-33.2): distância à borda esquerda (<c>Start</c>) ou direita (<c>End</c>) da faixa.</summary>
public enum TowerAnchor : byte { Start = 0, End = 1 }

/// <summary>
/// Torre (SDD-11 RF-11.2, ADR-034): guarda <c>Anchor</c> + <c>OffsetDip</c> (inteiro ≥ 0), não coordenadas;
/// o centro X vem do <c>Width</c> do layout (<see cref="SimState.TowerCenterX"/>). <c>Placed == false</c> = <c>SEM POSIÇÃO</c>.
/// </summary>
public struct Tower : IEntity
{
    public int Id { get; set; }
    public TowerType Type { get; set; }
    public TowerAnchor Anchor { get; set; }
    public int OffsetDip { get; set; }
    public bool Placed { get; set; }
    public float Cooldown { get; set; }
    public int Level { get; set; }
    public Targeting Mode { get; set; }
    public byte Hits { get; set; }
}
