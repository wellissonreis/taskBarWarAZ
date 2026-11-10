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

/// <summary>Torre (SDD-11 RF-11.2): guarda <c>SlotIndex</c>, não coordenadas; X e alcance vêm do layout e do tipo.</summary>
public struct Tower : IEntity
{
    public int Id { get; set; }
    public TowerType Type { get; set; }
    public int SlotIndex { get; set; }
    public float Cooldown { get; set; }
    public int Level { get; set; }
    public Targeting Mode { get; set; }
    public byte Hits { get; set; }
}
