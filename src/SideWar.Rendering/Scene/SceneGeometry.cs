using SideWar.Core.Simulation;
using SideWar.Rendering.Sprites;

namespace SideWar.Rendering.Scene;

/// <summary>Retângulo em DIP (a 100%), origem no canto superior esquerdo da janela de cena.</summary>
public readonly record struct DipRect(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
}

/// <summary>
/// Geometria pura da cena (SDD-22 RF-22.5, CA-22.2; ADR-034): posições e tamanhos exatos transcritos de <c>Strip.dc.html</c>
/// (canvas 1920 × 152), em DIP. Sem estado, sem Skia, sem alocação. A escala para pixels é feita pelo renderizador.
/// </summary>
public static class SceneGeometry
{
    /// <summary>Chão: pé de núcleo, inimigos, portal, chefe e caixa (RF-22.2).</summary>
    public const float GroundY = 102f;
    /// <summary>Linha de estado em <c>[102, 104)</c> (RF-22.6).</summary>
    public const float StatusLineTop = 102f;
    public const float StatusLineHeight = 2f;
    /// <summary>Topo da barra de tarefas na cena.</summary>
    public const float BarTop = 104f;
    /// <summary>Altura padrão da barra (DIP).</summary>
    public const float DefaultBarHeight = 48f;

    /// <summary>Altura total da superfície de cena: <c>104 + H</c> (152 com H = 48).</summary>
    public static float SurfaceHeight(float barHeightDip) => BarTop + barHeightDip;

    public const float TowerSpriteHeight = 36f;
    public const float TowerCellSize = 44f;
    public const float EnemyBarHeight = 3f;
    public const float EnemyBarGap = 6f;
    public const float EnemyFlyElevation = 20f;
    public const float CoreBarWidth = 64f;
    public const float BossBarWidth = 68f;
    public const float BarContentHeight = 5f;
    public const float BarBorder = 1f;

    /// <summary>Topo do número flutuante (RF-22.5: 64 DIP acima do chão).</summary>
    public const float FloatTop = 38f;
    public const float FloatRise = 16f;
    public const float FloatSeconds = 1.4f;

    // ---- núcleo ----
    public static DipRect Core(float coreLeft) => new(coreLeft, 46, 84, 56);

    /// <summary>Barra de HP do núcleo: caixa externa (borda de 1 incluída), 66 × 7.</summary>
    public static DipRect CoreBarOuter(float coreLeft) => Outer(CoreBarContent(coreLeft));

    /// <summary>Conteúdo (trilho/preenchimento) da barra do núcleo: 64 × 5 em (coreLeft + 10, 38).</summary>
    public static DipRect CoreBarContent(float coreLeft) => new(coreLeft + 10, 38, CoreBarWidth, BarContentHeight);

    // ---- torres ----

    /// <summary>Math.round do design (meio para cima), não o arredondamento do .NET.</summary>
    public static float RoundHalfUp(float v) => MathF.Floor(v + 0.5f);

    /// <summary>Altura do sprite: 36 na barra (<see cref="TowerHost.Taskbar"/>), 44 na faixa (<see cref="TowerHost.Band"/>).</summary>
    public static float TowerHeight(TowerHost host) => host == TowerHost.Band ? 44f : TowerSpriteHeight;

    public static float TowerWidth(TowerType type, TowerHost host)
    {
        bool band = host == TowerHost.Band;
        return type switch
        {
            TowerType.Frost => band ? 39 : 32,
            TowerType.Cannon => band ? 45 : 37,
            _ => band ? 44 : 36, // Bolt e Scale Set (D-13)
        };
    }

    /// <summary>Topo do sprite: <c>barTop + round((H − 36)/2)</c> na barra (110 com H = 48); 2 na faixa.</summary>
    public static float TowerTop(TowerHost host, float barHeightDip) =>
        host == TowerHost.Band ? 2f : BarTop + RoundHalfUp((barHeightDip - TowerSpriteHeight) / 2);

    /// <summary>Sprite da torre com centro X <paramref name="centerX"/>.</summary>
    public static DipRect Tower(TowerType type, TowerHost host, float centerX, float barHeightDip)
    {
        float w = TowerWidth(type, host);
        float left = host == TowerHost.Band
            ? centerX - TowerCellSize / 2 + RoundHalfUp((TowerCellSize - w) / 2)
            : centerX - RoundHalfUp(w / 2);
        return new(left, TowerTop(host, barHeightDip), w, TowerHeight(host));
    }

    /// <summary>Célula de clique / colisão 44 × 44 (RF-22.5): <c>top = barTop + round((H − 44)/2)</c> na barra.</summary>
    public static DipRect TowerCell(TowerHost host, float centerX, float barHeightDip) =>
        new(centerX - TowerCellSize / 2, host == TowerHost.Band ? 0f : BarTop + RoundHalfUp((barHeightDip - TowerCellSize) / 2), TowerCellSize, TowerCellSize);

    /// <summary>Canto do rótulo de nível: <c>(left + w − 10, top + h − 10)</c>; o texto é só o número.</summary>
    public static (float X, float Y) TowerLabelOrigin(DipRect sprite) => (sprite.X + sprite.W - 10, sprite.Y + sprite.H - 10);

    public const int LabelFontDip = 9;
    public const int FloatFontDip = 10;
    /// <summary>Brilho do canvas: <c>drop-shadow(0 0 3px rgba(76,194,255,.55))</c>.</summary>
    public const float GlowBlurDip = 3f;

    // ---- portal e inimigos ----
    /// <summary>Portal (camada estática): 68 × 52 em (<c>W − 80</c>, 50), pé no chão.</summary>
    public static DipRect Portal(float portalLeft) => new(portalLeft, 50, 68, 52);

    /// <summary>Tamanho do sprite do inimigo (SDD-22 RF-22.5); o topo é <c>102 − H − elevação</c>.</summary>
    public static (float W, float H) EnemySize(EnemyKind kind) => kind switch
    {
        EnemyKind.Grunt => (23, 26),
        EnemyKind.Runner => (38, 22),
        EnemyKind.Swarm => (36, 22),
        EnemyKind.Armored => (32, 30),
        EnemyKind.Tank => (43, 34),
        _ => (68, 52), // Boss
    };

    /// <summary>Elevação do pé: 20 só no drone (<see cref="EnemyKind.Runner"/>).</summary>
    public static float EnemyElevation(EnemyKind kind) => kind == EnemyKind.Runner ? EnemyFlyElevation : 0f;

    /// <summary>Sprite do inimigo: centro X em <paramref name="centerX"/>, pé em 102 (− elevação), deslocado por <paramref name="bobDip"/>.</summary>
    public static DipRect Enemy(EnemyKind kind, float centerX, float bobDip = 0)
    {
        var (w, h) = EnemySize(kind);
        return new(centerX - w / 2, GroundY - h - EnemyElevation(kind) + bobDip, w, h);
    }

    /// <summary>Barra de vida do inimigo comum: largura do sprite × 3, <c>top − 6</c> (sem borda, sem bob).</summary>
    public static DipRect EnemyBar(EnemyKind kind, float centerX)
    {
        var s = Enemy(kind, centerX);
        return new(s.X, s.Y - EnemyBarGap, s.W, EnemyBarHeight);
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

    /// <summary>Conteúdo da barra do chefe: 68 × 5 em (boss.Left, spriteTop − 8) (top 42 sem bob).</summary>
    public static DipRect BossBarContent(DipRect bossSprite) => new(bossSprite.X, bossSprite.Y - 8, BossBarWidth, BarContentHeight);

    public static DipRect BossBarOuter(DipRect bossSprite) => Outer(BossBarContent(bossSprite));

    private static DipRect Outer(DipRect c) => new(c.X - BarBorder, c.Y - BarBorder, c.W + 2 * BarBorder, c.H + 2 * BarBorder);

    /// <summary>Largura do preenchimento em DIP: <c>clamp(hp/max) × width</c> (0 se max ≤ 0 ou não finito).</summary>
    public static float BarFillWidth(double hp, double max, float width)
    {
        if (!(max > 0) || !double.IsFinite(hp)) return 0;
        return (float)(Math.Clamp(hp / max, 0.0, 1.0) * width);
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
