using SideWar.Core.Simulation;
using SideWar.Rendering.Animation;
using SideWar.Rendering.Icons;
using SideWar.Rendering.Scene;
using SideWar.Rendering.Sprites;
using SideWar.Rendering.Text;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Input;
using SideWar.Runtime.Power;
using SideWar.Runtime.Taskbar;
using SkiaSharp;
using FreeZone = SideWar.Core.Simulation.FreeZone;
using TowerHost = SideWar.Core.Simulation.TowerHost;

namespace SideWar.Rendering.Tests.Golden;

public enum StripScene
{
    Present,
    Away,
    Peak,
    Boss,
    Build,
    Band,
    Unpositioned,
}

/// <summary>
/// Monta, de forma determinística, as cenas de SDD-22 CA-22.7 (1920 × 152 DIP, ADR-034, TA.9) pela API pública: SimState/StripLayout,
/// <see cref="SceneRenderer.Draw"/> (com a borda de estado), <see cref="HotspotRenderer"/> (banner, caixa, selo) e
/// <see cref="BuildOverlayRenderer"/> (modo construção), compostos sobre o fundo do desktop (#1b2230) e uma barra de tarefas sintética
/// do canvas <c>Strip.dc.html</c> (só formas, sem texto: fixture <c>Fixtures/Taskbar/canvas.json</c> da TA.2: widget 0–192, Iniciar+pesquisa+ícones
/// 686–1332, bandeja 1696–1920; zonas livres [192,686) e [1332,1696)). A barra real do Windows não é desenhada pelo jogo (CA-22.7); aqui ela
/// serve só de referência visual atrás da faixa. Tempo de animação fixo.
/// </summary>
public static class StripScenes
{
    public const int WidthDip = 1920;
    public const int HeightDip = 152;
    public const int BarHeightDip = 48;
    public static readonly SKColor Backdrop = new(0x1b, 0x22, 0x30);
    private static readonly AnimationPolicy Normal = new(false, PowerProfile.Normal);

    public static string Name(StripScene scene) => scene switch
    {
        StripScene.Present => "strip-present",
        StripScene.Away => "strip-away",
        StripScene.Peak => "strip-peak",
        StripScene.Boss => "strip-boss",
        StripScene.Build => "strip-build",
        StripScene.Band => "strip-band",
        _ => "strip-unpositioned",
    };

    public static string GoldenName(StripScene scene, int dpi, bool text = true) => $"{Name(scene)}-{dpi * 100 / 96}{(text ? "" : "-notext")}";

    /// <summary>Cenas com texto (banner, selo, overlay do modo construção): têm variante "sem texto" para a comparação entre SOs.</summary>
    public static bool HasHotspotText(StripScene scene) => scene is StripScene.Peak or StripScene.Boss or StripScene.Away or StripScene.Build;

    /// <summary>Trechos bloqueados do canvas/fixture TA.2 (DIP).</summary>
    public static readonly BlockedSpan[] Blocked =
    [
        new(0, 192, BlockReason.WindowsWidget),
        new(686, 1332, BlockReason.StartSearchApps),
        new(1696, 1920, BlockReason.TrayClock),
    ];

    // Torres do canvas (x = esquerda do sprite de 36 DIP) → centro = x + round(w/2): Raio 36, Gelo 32, Canhão 37.
    private static readonly (TowerType Type, int Center, int Level)[] CanvasTowers =
    [
        (TowerType.Bolt, 218, 14), (TowerType.Frost, 272, 9), (TowerType.Bolt, 330, 12), (TowerType.Cannon, 439, 11),
        (TowerType.Frost, 536, 6), (TowerType.Bolt, 658, 8), (TowerType.Bolt, 1354, 10), (TowerType.Frost, 1408, 5), (TowerType.Cannon, 1519, 7),
    ];

    private static SimState NewState(StripScene scene)
    {
        bool band = scene == StripScene.Band;
        FreeZone[] zones = band ? [new FreeZone(0, WidthDip)] : [new FreeZone(192, 686), new FreeZone(1332, 1696)];
        var layout = StripLayout.Create(WidthDip, WidthDip - 46, 88, 4, WidthDip - 80, band ? TowerHost.Band : TowerHost.Taskbar, zones);
        var s = new SimState(layout) { BaseHp = 78, MaxBaseHp = 100 };
        for (int i = 0; i < CanvasTowers.Length; i++)
        {
            if (scene == StripScene.Build && i == 6) continue; // o canvas tira a torre «next» no modo construir
            var (type, center, level) = CanvasTowers[i];
            bool placed = !(scene == StripScene.Unpositioned && i is 1 or 4 or 7); // SEM POSIÇÃO: não desenhadas
            s.Towers.Add(new Tower { Id = s.TakeId(), Type = type, Anchor = TowerAnchor.Start, OffsetDip = center, Placed = placed, Level = level });
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
        using var atlas = SpriteAtlas.LoadEmbedded();
        using var renderer = new SceneRenderer(atlas, typo);
        renderer.SetDpi(dpi);
        using var hot = new HotspotRenderer(renderer.SpriteCache, typo, icons);
        hot.SetDpi(dpi);
        hot.TextEnabled = text;

        bool away = scene == StripScene.Away;
        bool boss = scene == StripScene.Boss;
        var state = NewState(scene);
        foreach (var (kind, x) in new (EnemyKind, float)[]
                 { (EnemyKind.Grunt, 241), (EnemyKind.Runner, 349), (EnemyKind.Tank, 461), (EnemyKind.Grunt, 611), (EnemyKind.Swarm, 778), (EnemyKind.Armored, 1396), (EnemyKind.Grunt, 1531) })
            AddEnemy(state, kind, x, 100);
        if (boss) AddEnemy(state, EnemyKind.Boss, 1134, 72);

        // números de dano (design: -96 Bolt, -240 Cannon, LENTO Frost), idade fixa de 0,3 s
        const double t0 = 10.0;
        renderer.ConsumeEvents(state, t0);
        if (!away && scene != StripScene.Build)
        {
            state.Events.Add(new SimEvent(SimEventType.Hit, 262, 0, 96, (int)TowerType.Bolt));
            state.Events.Add(new SimEvent(SimEventType.Hit, 446, 0, 240, (int)TowerType.Cannon));
            state.Events.Add(new SimEvent(SimEventType.Hit, 760, 0, 10, (int)TowerType.Frost));
            renderer.ConsumeEvents(state, t0);
        }
        double now = t0 + 0.3;

        int w = WidthDip * dpi / 96, h = HeightDip * dpi / 96;
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(Backdrop);
        DrawTaskbarMock(canvas, dpi);

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
            case StripScene.Build:
                DrawBuildOverlay(canvas, state, atlas, typo, dpi, text, now);
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
        var place = HotspotGeometry.SupplyBox(scene, (uint)dpi, 1026f);
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

    private static void DrawBuildOverlay(SKCanvas canvas, SimState state, SpriteAtlas atlas, Typography typo, int dpi, bool text, double now)
    {
        using var overlay = new BuildOverlayRenderer(atlas, typo) { TextEnabled = text };
        overlay.SetDpi(dpi);
        var ghost = new BuildGhost(1352, true, PlacementReason.None); // canvas: fantasma verde em left 1330
        overlay.Draw(canvas, new BuildOverlayFrame(state, BarHeightDip, Blocked, ghost, TowerType.Bolt, "RAIO · 1,2K", now, ReducedMotion: true));
    }

    /// <summary>Barra sintética do canvas (#1c1c1c, borda superior #2e2e2e): widget, Iniciar, pesquisa, 8 ícones, bandeja; só formas.</summary>
    private static void DrawTaskbarMock(SKCanvas canvas, int dpi)
    {
        float s = dpi / 96f;
        using var p = new SKPaint { IsAntialias = false };
        void Rect(float x, float y, float w, float h, uint argb)
        {
            p.Color = new SKColor(argb);
            canvas.DrawRect(MathF.Round(x * s), MathF.Round(y * s), MathF.Round(w * s), MathF.Round(h * s), p);
        }
        Rect(0, 104, WidthDip, 48, 0xff1c1c1c);
        Rect(0, 104, WidthDip, 1, 0xff2e2e2e);
        Rect(10, 116, 22, 22, 0xfff5b335);                 // widget: sol
        Rect(40, 114, 36, 5, 0xfff2f2f2); Rect(40, 124, 52, 5, 0xffc8c8c8);
        Rect(701, 117, 22, 22, 0xff4cc2ff); Rect(703, 119, 18, 18, 0xff1c1c1c); // Iniciar
        Rect(740, 112, 200, 32, 0xff2c2c2c);                // pesquisa
        uint[] apps = [0xfff2c14e, 0xffe04f3a, 0xff2f6fe0, 0xff5a4fcf, 0xff3aa14d, 0xff7f7f7f, 0xff1c8de0, 0xff0e7ad1];
        for (int i = 0; i < apps.Length; i++) Rect(948 + i * 48 + 9, 106 + 9, 26, 26, apps[i]);
        Rect(1712, 120, 14, 10, 0xffd0d0d0); Rect(1740, 120, 12, 12, 0xffd0d0d0); Rect(1766, 120, 14, 12, 0xffd0d0d0); // bandeja
        Rect(1850, 110, 46, 5, 0xfff2f2f2); Rect(1840, 122, 56, 5, 0xffc8c8c8);                                     // relógio
    }
}
