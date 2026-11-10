namespace SideWar.Core.Simulation;

/// <summary>Tipo de inimigo; a ordem espelha <c>enemies.json</c> (SDD-27 RF-27.6).</summary>
public enum EnemyKind : byte { Grunt, Runner, Swarm, Armored, Tank, Boss }

/// <summary>Tipo de torre; <see cref="None"/> só em <c>Enemy.LastHit</c> (nenhum acerto ainda).</summary>
public enum TowerType : byte { None = 0, Bolt, Frost, Cannon, ScaleSet }

/// <summary>Modo de mira (SDD-11 RF-11.5). Padrão: <see cref="First"/>.</summary>
public enum Targeting : byte { First = 0, Last, Strong, Weak }

[Flags]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "Nome fixado por SDD-11 RF-11.2.")]
public enum EnemyFlags : byte { None = 0, Chilled = 1, Boss = 2 }

/// <summary>Mapeamento id do JSON de conteúdo ⇄ enum (ids de <c>enemies.json</c> e <c>towers.json</c>).</summary>
public static class SimIds
{
    public static bool TryEnemyKind(string id, out EnemyKind kind)
    {
        switch (id)
        {
            case "grunt": kind = EnemyKind.Grunt; return true;
            case "runner": kind = EnemyKind.Runner; return true;
            case "swarm": kind = EnemyKind.Swarm; return true;
            case "armored": kind = EnemyKind.Armored; return true;
            case "tank": kind = EnemyKind.Tank; return true;
            case "boss": kind = EnemyKind.Boss; return true;
            default: kind = default; return false;
        }
    }

    public static bool TryTowerType(string id, out TowerType type)
    {
        switch (id)
        {
            case "bolt": type = TowerType.Bolt; return true;
            case "frost": type = TowerType.Frost; return true;
            case "cannon": type = TowerType.Cannon; return true;
            case "scaleset": type = TowerType.ScaleSet; return true;
            default: type = TowerType.None; return false;
        }
    }
}
