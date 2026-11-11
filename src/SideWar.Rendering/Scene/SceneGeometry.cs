using SideWar.Core.Simulation;
using SideWar.Rendering.Sprites;

namespace SideWar.Rendering.Scene;

/// <summary>Retângulo em DIP (a 100%), origem no canto superior esquerdo da faixa de 96 DIP.</summary>
public readonly record struct DipRect(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
}

/// <summary>
/// Geometria pura da cena (SDD-22 RF-22.5, CA-22.2): posições e tamanhos exatos transcritos de <c>Strip.dc.html</c>,
/// em DIP. Sem estado, sem Skia, sem alocação. A escala para pixels é feita pelo renderizador.
/// </summary>
public static class SceneGeometry
{
    /// <summary>Altura da faixa (linha de base em y = 96).</summary>
    public const float StripHeight = 96f;
    public const float CellWidth = 44f;
    public const float BarContentWidth = 60f;
    public const float BarContentHeight = 5f;
    public const float BarBorder = 1f;

    /// <summary>Topo do número flutuante (DP-22.10, ADR-032).</summary>
    public const float FloatTop = 32f;
    public const float FloatRise = 16f;
    public const float FloatSeconds = 1.4f;

    // ---- núcleo ----
    public static DipRect Core(float coreLeft) => new(coreLeft, 40, 84, 56);

    /// <summary>Barra de HP do núcleo: caixa externa (borda de 1 px incluída), 62 × 7 em (left+11, 31).</summary>
    public static DipRect CoreBarOuter(float coreLeft) => Outer(CoreBarContent(coreLeft));

    /// <summary>Conteúdo (trilho/preenchimento) da barra do núcleo: 60 × 5 em (coreLeft + 12, 32).</summary>
    public static DipRect CoreBarContent(float coreLeft) => new(coreLeft + 12, 32, BarContentWidth, BarContentHeight);

    // ---- slots e torres ----
    public static float CellLeft(float centerXDip) => centerXDip - CellWidth / 2;

    public static float TowerWidth(TowerType type) => type switch
    {
        TowerType.Frost => 39,
        TowerType.Cannon => 45,
        _ => 44, // Bolt e Scale Set (D-13: 44 × 44 como as demais)
    };

    /// <summary>Math.round do design (meio para cima), não o arredondamento do .NET.</summary>
    public static float RoundHalfUp(float v) => MathF.Floor(v + 0.5f);

    public static DipRect Tower(TowerType type, float cellLeft)
    {
        float w = TowerWidth(type);
        return new(cellLeft + RoundHalfUp((CellWidth - w) / 2), 52, w, 44);
    }

    /// <summary>Caixa do rótulo "LVn" (texto centralizado nos 44 de largura).</summary>
    public static DipRect TowerLabelBox(float cellLeft) => new(cellLeft, 38, CellWidth, 0);

    public static DipRect EmptySlot(float cellLeft) => new(cellLeft + 1, 70, 42, 26);

    /// <summary>Caixa do "+" do slot vazio (texto centralizado nos 44 de largura).</summary>
    public static DipRect PlusBox(float cellLeft) => new(cellLeft, 54, CellWidth, 0);

    public const float EmptySlotOpacity = 0.85f;
    public const int LabelFontDip = 9;
    public const int PlusFontDip = 10;
    public const int FloatFontDip = 10;

    // ---- portal e inimigos ----
    public static DipRect Portal(float portalLeft) => new(portalLeft, 44, 68, 52);

    /// <summary>Tamanho do sprite do inimigo (SDD-22 RF-22.5); o topo é <c>96 − H</c> (base em y = 96).</summary>
    public static (float W, float H) EnemySize(EnemyKind kind) => kind switch
    {
        EnemyKind.Grunt => (23, 26),
        EnemyKind.Runner => (38, 22),
        EnemyKind.Swarm => (36, 22),
        EnemyKind.Armored => (32, 30),
        EnemyKind.Tank => (43, 34),
        _ => (68, 52), // Boss
    };

    /// <summary>Sprite do inimigo: centro X em <paramref name="centerX"/>, pé em y = 96, deslocado por <paramref name="bobDip"/>.</summary>
    public static DipRect Enemy(EnemyKind kind, float centerX, float bobDip = 0)
    {
        var (w, h) = EnemySize(kind);
        return new(centerX - w / 2, StripHeight - h + bobDip, w, h);
    }

    public static SpriteId EnemySprite(EnemyKind kind) => kind switch
    {
        EnemyKind.Grunt => SpriteId.EnemyGrunt,
        EnemyKind.Runner => SpriteId.EnemyRunner,
        EnemyKind.Swarm => SpriteId.EnemySwarm,
        EnemyKind.Armored => SpriteId.EnemyArmored,
        EnemyKind.Tank => SpriteId.EnemyTank,
        _ => SpriteId.EnemyBoss,
    };

    public static SpriteId TowerSprite(TowerType type) => type switch
    {
        TowerType.Frost => SpriteId.TowerFrost,
        TowerType.Cannon => SpriteId.TowerCannon,
        TowerType.ScaleSet => SpriteId.TowerScaleSet,
        _ => SpriteId.TowerBolt,
    };

    /// <summary>Conteúdo da barra do chefe: 60 × 5 em (boss.Left + 4, spriteTop − 8) (top 36 sem bob).</summary>
    public static DipRect BossBarContent(DipRect bossSprite) => new(bossSprite.X + 4, bossSprite.Y - 8, BarContentWidth, BarContentHeight);

    public static DipRect BossBarOuter(DipRect bossSprite) => Outer(BossBarContent(bossSprite));

    private static DipRect Outer(DipRect c) => new(c.X - BarBorder, c.Y - BarBorder, c.W + 2 * BarBorder, c.H + 2 * BarBorder);

    /// <summary>Largura do preenchimento em DIP: <c>clamp(hp/max) × 60</c> (0 se max ≤ 0 ou não finito).</summary>
    public static float BarFillWidth(double hp, double max)
    {
        if (!(max > 0) || !double.IsFinite(hp)) return 0;
        return (float)(Math.Clamp(hp / max, 0.0, 1.0) * BarContentWidth);
    }

    // ---- bob e números flutuantes ----

    /// <summary>cubic-bezier(0, 0, 0.58, 1) avaliada em x = <paramref name="t"/> ∈ [0,1] (bisseção em t paramétrico).</summary>
    public static float EaseOut(float t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        const float x1 = 0f, y1 = 0f, x2 = 0.58f, y2 = 1f;
        float lo = 0, hi = 1, u = t;
        for (int i = 0; i < 24; i++)
        {
            u = (lo + hi) * 0.5f;
            float x = Bez(x1, x2, u);
            if (x < t) lo = u; else hi = u;
        }
        return Bez(y1, y2, u);
    }

    private static float Bez(float p1, float p2, float u)
    {
        float v = 1 - u;
        return 3 * v * v * u * p1 + 3 * v * u * u * p2 + u * u * u;
    }
}
