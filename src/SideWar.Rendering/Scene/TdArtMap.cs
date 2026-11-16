using SideWar.Rendering.Sprites;
using SideWar.Rendering.Theme;
using SkiaSharp;

namespace SideWar.Rendering.Scene;

/// <summary>Uma linha da tabela de arte TD: sprite do design, altura em DIP, tinta por traço e se é provisória (sem arte própria).</summary>
public readonly record struct TdArtEntry(string Id, SpriteId Sprite, float HeightDip, SKColor Tint, byte TintAlpha, bool Provisional, string Note);

/// <summary>
/// TD.9 (SDD-28): mapa conteúdo TD → sprites existentes do design (assets/source/design). O design tem 3 torres e 6 inimigos; o resto
/// reaproveita a sprite mais próxima com tinta/escala distintas por traço e fica marcado PROVISÓRIO (espelhado em MANIFEST.json, "tdMapping").
/// Tintas por traço: solo sem tinta, voador ciano, blindado âmbar, regenera verde, drena magenta, chefe vermelho/ciano/verde.
/// </summary>
public static class TdArtMap
{
    /// <summary>Altura alvo das torres (DIP) numa faixa de 32 DIP.</summary>
    public const float TowerHeightDip = 28f;

    /// <summary>Largura (DIP) do núcleo e do portal: cabem na folga de 32 DIP às pontas.</summary>
    public const float EdgeWidthDip = 32f;

    /// <summary>Largura (DIP) da caixa-bomba (a área de clique tem 24 DIP de largura).</summary>
    public const float CrateWidthDip = 26f;

    private static readonly SKColor Cyan = Palette.Td.TintCyan, Amber = Palette.Td.TintAmber, Green = Palette.Td.TintGreen,
        Magenta = Palette.Td.TintMagenta, Orange = Palette.Td.TintOrange, Red = Palette.Td.TintRed, Violet = Palette.Td.TintViolet;

    public static readonly TdArtEntry[] Towers =
    [
        new("bolt", SpriteId.TowerBolt, TowerHeightDip, default, 0, false, "tower-bolt"),
        new("frost", SpriteId.TowerFrost, TowerHeightDip, default, 0, false, "tower-frost"),
        new("cannon", SpriteId.TowerCannon, TowerHeightDip, default, 0, false, "tower-cannon"),
        new("balancer", SpriteId.TowerFrost, TowerHeightDip, Green, 120, true, "tower-frost + tinta verde (sem arte própria)"),
        new("scaleset", SpriteId.TowerBolt, TowerHeightDip, Violet, 120, true, "tower-bolt + tinta violeta (sem arte própria)"),
    ];

    public static readonly TdArtEntry[] Enemies =
    [
        new("grunt", SpriteId.EnemyGrunt, 16f, default, 0, false, "enemy-scout"),
        new("runner", SpriteId.EnemyGrunt, 13f, Orange, 110, true, "enemy-scout menor + tinta laranja (rápido)"),
        new("swarm", SpriteId.EnemySwarm, 11f, default, 0, false, "enemy-crawler pequeno"),
        new("armored", SpriteId.EnemyArmored, 19f, Amber, 70, false, "enemy-shield + tinta âmbar"),
        new("tank", SpriteId.EnemyTank, 22f, default, 0, false, "enemy-brute"),
        new("flyer", SpriteId.EnemyRunner, 14f, Cyan, 90, false, "enemy-drone + tinta ciano"),
        new("regen", SpriteId.EnemySwarm, 15f, Green, 110, true, "enemy-crawler + tinta verde"),
        new("leech", SpriteId.EnemyGrunt, 19f, Magenta, 110, true, "enemy-scout maior + tinta magenta"),
        new("ransomware", SpriteId.EnemyBoss, 28f, Red, 100, true, "enemy-boss + tinta vermelha"),
        new("botnet", SpriteId.EnemyBoss, 28f, Cyan, 100, true, "enemy-boss + tinta ciano"),
        new("zeroday", SpriteId.EnemyBoss, 28f, Green, 100, true, "enemy-boss + tinta verde"),
    ];

    public static bool TryTower(string id, out TdArtEntry entry) => TryFind(Towers, id, out entry);

    public static bool TryEnemy(string id, out TdArtEntry entry) => TryFind(Enemies, id, out entry);

    private static bool TryFind(TdArtEntry[] list, string id, out TdArtEntry entry)
    {
        foreach (var e in list)
        {
            if (e.Id == id) { entry = e; return true; }
        }
        entry = default;
        return false;
    }
}
