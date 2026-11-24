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
    /// <summary>
    /// Altura alvo das torres (DIP do espaço de desenho de 32). TD.28c: de volta a 28 (o usuário aprovou o tamanho torre × inimigos);
    /// o núcleo e o portal ficaram maiores (42 = 1,5× a torre) e passam do topo do espaço de 32 (<see cref="SideWar.Runtime.Td.TdBand.EdgeHeadroomDip"/>).
    /// </summary>
    public const float TowerHeightDip = 28f;

    /// <summary>Largura (DIP) do núcleo e do portal: cabem na folga de 32 DIP às pontas.</summary>
    public const float EdgeWidthDip = 32f;

    /// <summary>Altura (DIP) do núcleo e do portal: 42 = 1,5× a torre (28); passam do topo do espaço de 32 (folga de 14 na janela da cena).</summary>
    public const float EdgeHeightDip = 42f;

    private static readonly SKColor Cyan = Palette.Td.TintCyan, Amber = Palette.Td.TintAmber, Green = Palette.Td.TintGreen,
        Magenta = Palette.Td.TintMagenta, Orange = Palette.Td.TintOrange, Red = Palette.Td.TintRed, Violet = Palette.Td.TintViolet;

    public static readonly TdArtEntry[] Towers =
    [
        new("dns", SpriteId.TowerBolt, TowerHeightDip, Cyan, 110, true, "raio animado com tinta ciano"),
        new("frontdoor", SpriteId.TowerBolt, TowerHeightDip, default, 0, false, "vetor próprio"),
        new("shield", SpriteId.TowerBolt, TowerHeightDip, default, 0, false, "vetor próprio"),
        new("waf", SpriteId.TowerBolt, TowerHeightDip, default, 0, false, "vetor próprio"),
        new("eventgrid", SpriteId.TowerBolt, TowerHeightDip, default, 0, false, "sprite animado original (assets/source/anim/eventgrid-*, tools/sprites/gen_eventgrid.py)"),
        new("bastion", SpriteId.TowerBolt, TowerHeightDip, default, 0, false, "sprite do artboard (assets/source/design6-towers/bastion.png; efeito original)"),
        new("firewall", SpriteId.TowerBolt, TowerHeightDip, default, 0, false, "sprite animado original (assets/source/anim/firewall-*, tools/sprites/gen_firewall.py)"),
        new("backup", SpriteId.TowerBolt, TowerHeightDip, default, 0, false, "sprite do artboard (assets/source/design6-towers/backup.png; efeito original)"),
        new("vault", SpriteId.TowerBolt, TowerHeightDip, default, 0, false, "sprite do artboard (assets/source/design6-towers/vault.png; efeito original)"),
        new("queue", SpriteId.TowerBolt, TowerHeightDip, default, 0, false, "sprite do artboard (assets/source/design6-towers/queue.png; efeito original)"),
        new("aks", SpriteId.TowerBolt, TowerHeightDip, default, 0, false, "sprite do artboard (assets/source/design6-towers/aks.png; efeito original)"),
        new("pipeline", SpriteId.TowerBolt, TowerHeightDip, default, 0, false, "sprite animado original (assets/source/anim/pipeline-*, tools/sprites/gen_pipeline.py)"),
        new("bolt", SpriteId.TowerBolt, TowerHeightDip, default, 0, false, "tower-bolt"),
        new("frost", SpriteId.TowerFrost, TowerHeightDip, default, 0, false, "tower-frost"),
        new("cannon", SpriteId.TowerCannon, TowerHeightDip, default, 0, false, "tower-cannon"),
        new("balancer", SpriteId.TowerFrost, TowerHeightDip, Green, 120, true, "tower-frost + tinta verde (sem arte própria)"),
        new("scaleset", SpriteId.TowerBolt, TowerHeightDip, Violet, 120, true, "tower-bolt + tinta violeta (sem arte própria)"),
    ];

    public static readonly TdArtEntry[] Enemies =
    [
        new("chefe_botnet", SpriteId.EnemyBoss, 28f, default, 0, false, "arte animada Chefões"),
        new("aliado_botnet", SpriteId.EnemyBoss, 14f, default, 0, false, "arte animada Chefões"),
        new("chefe_kraken", SpriteId.EnemyBoss, 28f, default, 0, false, "arte animada Chefões"),
        new("aliado_kraken", SpriteId.EnemyBoss, 14f, default, 0, false, "arte animada Chefões"),
        new("chefe_golem", SpriteId.EnemyBoss, 28f, default, 0, false, "arte animada Chefões"),
        new("aliado_golem", SpriteId.EnemyBoss, 14f, default, 0, false, "arte animada Chefões"),
        new("chefe_ronin", SpriteId.EnemyBoss, 28f, default, 0, false, "arte animada Chefões"),
        new("aliado_ronin", SpriteId.EnemyBoss, 14f, default, 0, false, "arte animada Chefões"),
        new("chefe_trojan", SpriteId.EnemyBoss, 28f, default, 0, false, "arte animada Chefões"),
        new("aliado_trojan", SpriteId.EnemyBoss, 14f, default, 0, false, "arte animada Chefões"),
        new("chefe_hydra", SpriteId.EnemyBoss, 28f, default, 0, false, "arte animada Chefões"),
        new("aliado_hydra", SpriteId.EnemyBoss, 14f, default, 0, false, "arte animada Chefões"),
        new("grunt", SpriteId.EnemyGrunt, 16f, default, 0, false, "enemy-scout"),
        new("runner", SpriteId.EnemyGrunt, 13f, Orange, 110, true, "enemy-scout menor + tinta laranja (rápido)"),
        new("swarm", SpriteId.EnemySwarm, 11f, default, 0, false, "enemy-crawler pequeno"),
        new("armored", SpriteId.EnemyArmored, 19f, Amber, 70, false, "enemy-shield + tinta âmbar"),
        new("tank", SpriteId.EnemyTank, 22f, default, 0, false, "enemy-brute"),
        new("flyer", SpriteId.EnemyRunner, 14f, Cyan, 90, false, "enemy-drone + tinta ciano"),
        new("regen", SpriteId.EnemySwarm, 15f, Green, 110, true, "enemy-crawler + tinta verde"),
        new("leech", SpriteId.EnemyGrunt, 19f, Magenta, 110, true, "enemy-scout maior + tinta magenta"),
        new("fork", SpriteId.EnemySwarm, 15f, Violet, 110, false, "vetor próprio: ramificação replicadora"),
        new("trojan", SpriteId.EnemyArmored, 18f, Amber, 110, false, "vetor próprio: escudo e carga"),
        new("phishing", SpriteId.EnemyGrunt, 16f, Cyan, 110, false, "vetor próprio: envelope camuflado"),
        new("synflood", SpriteId.EnemySwarm, 13f, Cyan, 110, false, "pacotes em grupo"),
        new("scanner", SpriteId.EnemyRunner, 15f, Orange, 110, false, "sonda rápida"),
        new("slowloris", SpriteId.EnemyTank, 20f, Green, 110, false, "conexão persistente"),
        new("fragment", SpriteId.EnemySwarm, 16f, Violet, 110, false, "pacote dividido"),
        new("dnstunnel", SpriteId.EnemyGrunt, 16f, Cyan, 110, false, "túnel camuflado"),
        new("encrypted", SpriteId.EnemyArmored, 18f, Amber, 110, false, "carga protegida"),
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
