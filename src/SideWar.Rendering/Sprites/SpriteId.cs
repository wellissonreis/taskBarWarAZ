namespace SideWar.Rendering.Sprites;

/// <summary>Ids de sprite em tempo de execução (SDD-09 RF-09.4; mesma ordem de assets/source/sprites.json).</summary>
public enum SpriteId : ushort
{
    TowerBolt,
    TowerFrost,
    TowerCannon,
    TowerScaleSet,
    SlotEmpty,
    Core,
    Portal,
    EnemyGrunt,
    EnemyRunner,
    EnemySwarm,
    EnemyArmored,
    EnemyTank,
    EnemyBoss,
    SupplyCrate,
}

/// <summary>Metadados estáticos de um sprite: id textual do manifesto e tamanho em DIP (SDD-22 RF-22.9).</summary>
public readonly record struct SpriteInfo(SpriteId Id, string Key, float WorldW, float WorldH, float PivotX, float PivotY);

/// <summary>Tabela estática de sprites (SDD-09 RF-09.4: tabela + teste contra o manifesto).</summary>
public static class SpriteCatalog
{
    // Tamanhos: SDD-22 RF-22.9. O Scale Set (D-13) não consta da tabela: 44 x 44 como as demais torres.
    public static IReadOnlyList<SpriteInfo> All { get; } =
    [
        new(SpriteId.TowerBolt, "tower.bolt", 44, 44, 0.5f, 1f),
        new(SpriteId.TowerFrost, "tower.frost", 39, 44, 0.5f, 1f),
        new(SpriteId.TowerCannon, "tower.cannon", 45, 44, 0.5f, 1f),
        new(SpriteId.TowerScaleSet, "tower.scaleset", 44, 44, 0.5f, 1f),
        new(SpriteId.SlotEmpty, "slot.empty", 42, 26, 0.5f, 1f),
        new(SpriteId.Core, "core", 84, 56, 0.5f, 1f),
        new(SpriteId.Portal, "portal", 68, 52, 0.5f, 1f),
        new(SpriteId.EnemyGrunt, "enemy.grunt", 23, 26, 0.5f, 1f),
        new(SpriteId.EnemyRunner, "enemy.runner", 38, 22, 0.5f, 1f),
        new(SpriteId.EnemySwarm, "enemy.swarm", 36, 22, 0.5f, 1f),
        new(SpriteId.EnemyArmored, "enemy.armored", 32, 30, 0.5f, 1f),
        new(SpriteId.EnemyTank, "enemy.tank", 43, 34, 0.5f, 1f),
        new(SpriteId.EnemyBoss, "enemy.boss", 68, 52, 0.5f, 1f),
        new(SpriteId.SupplyCrate, "supply.crate", 45, 24, 0.5f, 1f),
    ];

    public static SpriteInfo Get(SpriteId id) => All[(int)id];

    public static bool TryGetByKey(string key, out SpriteInfo info)
    {
        foreach (var s in All)
        {
            if (string.Equals(s.Key, key, StringComparison.Ordinal))
            {
                info = s;
                return true;
            }
        }

        info = default;
        return false;
    }
}
