using SideWar.Core.Simulation;
using SideWar.Rendering.Animation;
using SideWar.Rendering.Icons;
using SideWar.Rendering.Scene;
using SideWar.Rendering.Sprites;
using SideWar.Rendering.Text;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Power;
using SkiaSharp;

namespace SideWar.Rendering.Tests.Golden;

public enum StripScene
{
    Present,
    Away,
    Peak,
    Boss,
    Fallback,
    EmptySlots,
}

/// <summary>
/// Monta, de forma determinística, as cenas de SDD-22 CA-22.7 (1440 × 96 DIP) pela API pública: SimState/StripLayout,
/// <see cref="SceneRenderer.Draw"/> (com a borda de estado) e <see cref="HotspotRenderer"/> (banner, caixa, selo), compostos
/// sobre o fundo da barra de tarefas (#1b2230) como o shell faria com as janelas layered. Tempo de animação fixo.
/// </summary>
public static class StripScenes
{
    public const int WidthDip = 1440;
    public const int HeightDip = 96;
    public static readonly SKColor Backdrop = new(0x1b, 0x22, 0x30);
    private static readonly AnimationPolicy Normal = new(false, PowerProfile.Normal);

    public static string Name(StripScene scene) => scene switch
    {
        StripScene.Present => "strip-present",
        StripScene.Away => "strip-away",
        StripScene.Peak => "strip-peak",
        StripScene.Boss => "strip-boss",
        StripScene.Fallback => "strip-fallback",
        _ => "strip-empty-slots",
    };

    public static string GoldenName(StripScene scene, int dpi, bool text = true) => $"{Name(scene)}-{dpi * 100 / 96}{(text ? "" : "-notext")}";

    /// <summary>Cenas cujo texto vem dos hotspots (banner, selo): têm variante "sem texto" para a comparação entre SOs.</summary>
    public static bool HasHotspotText(StripScene scene) => scene is StripScene.Peak or StripScene.Boss or StripScene.Away;

    private static SimState NewState(bool towers)
    {
        var slots = new float[8];
        for (int i = 0; i < 8; i++) slots[i] = 640 + 48 * i + 22;
        var s = new SimState(TestLayouts.FromCenters(1290, 94, 10, 1256, slots)) { BaseHp = 78, MaxBaseHp = 100 };
        if (towers)
        {
            TowerType[] types = [TowerType.Bolt, TowerType.Frost, TowerType.Cannon, TowerType.Bolt, TowerType.Frost];
            int[] levels = [14, 3, 8, 5, 1];
            for (int i = 0; i < types.Length; i++)
                s.Towers.Add(new Tower { Id = s.TakeId(), Type = types[i], Anchor = TowerAnchor.Start, OffsetDip = TestLayouts.SceneX(i), Placed = true, Level = levels[i] });
        }
        return s;
    }

    private static void AddEnemy(SimState s, EnemyKind kind, float x, double hp, double max = 100)
    {
        s.Enemies.Add(new Enemy
        {
            Id = s.TakeId(), Kind = kind, X = x, Hp = hp, MaxHp = max,
            Flags = kind == EnemyKind.Boss ? EnemyFlags.Boss : EnemyFlags.None,
        });
    }

    /// <summary>Renderiza a cena (<paramref name="dpi"/> 96 = 100%, 144 = 150%) como bitmap opaco.</summary>
    public static SKBitmap Render(StripScene scene, int dpi, bool text = true)
    {
        using var typo = new Typography();
        using var icons = new IconCatalog();
        using var atlas = scene == StripScene.Fallback ? PlaceholderAtlas.Create() : SpriteAtlas.LoadEmbedded();
        using var renderer = new SceneRenderer(atlas, typo);
        renderer.SetDpi(dpi);
        using var hot = new HotspotRenderer(renderer.SpriteCache, typo, icons);
        hot.SetDpi(dpi);
        hot.TextEnabled = text;

        bool away = scene == StripScene.Away;
        bool boss = scene == StripScene.Boss;
        var state = NewState(scene != StripScene.EmptySlots);
        foreach (var (kind, x) in new (EnemyKind, float)[]
                 { (EnemyKind.Grunt, 560), (EnemyKind.Runner, 640), (EnemyKind.Swarm, 792), (EnemyKind.Armored, 880), (EnemyKind.Tank, 980), (EnemyKind.Grunt, 1150), (EnemyKind.Runner, 1210) })
            AddEnemy(state, kind, x, 100);
        if (boss) AddEnemy(state, EnemyKind.Boss, 1040, 72);

        // números de dano (design: -96 Bolt, -240 Cannon, LENTO Frost), idade fixa de 0,3 s
        const double t0 = 10.0;
        renderer.ConsumeEvents(state, t0);
        if (!away)
        {
            state.Events.Add(new SimEvent(SimEventType.Hit, 696, 0, 96, (int)TowerType.Bolt));
            state.Events.Add(new SimEvent(SimEventType.Hit, 744, 0, 240, (int)TowerType.Cannon));
            state.Events.Add(new SimEvent(SimEventType.Hit, 792, 0, 10, (int)TowerType.Frost));
            renderer.ConsumeEvents(state, t0);
        }
        double now = t0 + 0.3;

        int w = WidthDip * dpi / 96, h = HeightDip * dpi / 96;
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(Backdrop);

        var edge = away ? StripEdgeMode.Away : boss ? StripEdgeMode.Boss : StripEdgeMode.Present;
        using (var layer = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul)))
        {
            using (var lc = new SKCanvas(layer)) renderer.Draw(lc, state, now, Normal, edge);
            using var paint = new SKPaint { Color = SKColors.White.WithAlpha(away ? (byte)128 : (byte)255) };
            GoldenImage.Blit(canvas, layer, 0, 0, paint); // modo ausente: SetOpacity(128) na cena (DP-22.13)
        }

        var sceneRect = new PixelRect(0, 0, w, h);
        switch (scene)
        {
            case StripScene.Present:
                DrawSupplyBox(canvas, hot, sceneRect, dpi, now);
                break;
            case StripScene.Peak:
                DrawBanner(canvas, hot, sceneRect, dpi, BannerVariant.Peak, "0:24", now);
                DrawSupplyBox(canvas, hot, sceneRect, dpi, now);
                break;
            case StripScene.Boss:
                DrawBanner(canvas, hot, sceneRect, dpi, BannerVariant.Boss, "0:45", now);
                break;
            case StripScene.Away:
                DrawSeal(canvas, hot, sceneRect, dpi, "ausente há 1h 48min");
                break;
        }
        return bmp;
    }

    private static void DrawBanner(SKCanvas canvas, HotspotRenderer hot, PixelRect scene, int dpi, BannerVariant variant, string chip, double now)
    {
        // largura do controle: mesma conta do desenho, medida pelo renderer
        int control = hot.MeasureBannerWidth(variant, chip);
        var place = HotspotGeometry.Banner(scene, (uint)dpi, control);
        using var win = new SKBitmap(new SKImageInfo(place.Window.Width, place.Window.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var wc = new SKCanvas(win)) hot.DrawBanner(wc, variant, chip, now, Normal);
        GoldenImage.Blit(canvas, win, place.Window.Left, place.Window.Top);
    }

    private static void DrawSupplyBox(SKCanvas canvas, HotspotRenderer hot, PixelRect scene, int dpi, double now)
    {
        var place = HotspotGeometry.SupplyBox(scene, (uint)dpi, 1060f);
        using var win = new SKBitmap(new SKImageInfo(place.Window.Width, place.Window.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var wc = new SKCanvas(win)) hot.DrawSupplyBox(wc, now, Normal);
        GoldenImage.Blit(canvas, win, place.Window.Left, place.Window.Top);
    }

    private static void DrawSeal(SKCanvas canvas, HotspotRenderer hot, PixelRect scene, int dpi, string awayText)
    {
        var size = hot.MeasureSeal(awayText);
        var rect = HotspotGeometry.Seal(scene, (uint)dpi, size.Width, size.Height);
        using var win = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var wc = new SKCanvas(win)) hot.DrawSeal(wc, awayText);
        GoldenImage.Blit(canvas, win, rect.Left, rect.Top);
    }
}
