using SideWar.Core.Content;
using SideWar.Core.Simulation;
using SideWar.Rendering.Icons;
using SideWar.Rendering.Panel;
using SideWar.Rendering.Sprites;
using SideWar.Rendering.Text;
using SideWar.Rendering.Widget;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Panel;
using SideWar.Runtime.Presence;
using SideWar.Runtime.Widget;
using SkiaSharp;

namespace SideWar.Rendering.Tests.Golden;

public enum HudScene
{
    WidgetPresent,
    WidgetAbsent,
    WidgetHover,
    WidgetActive,
    WidgetBadge,
    PanelArsenal,
    PanelCampaign,
    PanelMissions,
    PanelMigrate,
    CardWelcomeBack,
    CardBossQuestion,
    CardCollectQuestion,
}

/// <summary>
/// Cenas de golden do widget compacto (SDD-23 CA-23.4), do painel de 4 abas (SDD-24 CA-24.13) e dos cards (RF-24.9/RF-25): cada quadro é
/// renderizado pela API pública do renderer e composto sobre o fundo do desktop (#1b2230) para a imagem ficar opaca. Dados de teste do
/// design (Main/Offline/Presence). Arsenal/Campanha não têm cromo próprio (só o corpo): o teste desenha borda 2, fundo e o filete de 54.
/// </summary>
public static class HudScenes
{
    public static readonly SKColor Backdrop = new(0x1b, 0x22, 0x30);
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);

    public static string Name(HudScene scene) => "hud-" + scene switch
    {
        HudScene.WidgetPresent => "widget-present",
        HudScene.WidgetAbsent => "widget-absent",
        HudScene.WidgetHover => "widget-hover",
        HudScene.WidgetActive => "widget-active",
        HudScene.WidgetBadge => "widget-badge",
        HudScene.PanelArsenal => "panel-arsenal",
        HudScene.PanelCampaign => "panel-campaign",
        HudScene.PanelMissions => "panel-missions",
        HudScene.PanelMigrate => "panel-migrate",
        HudScene.CardWelcomeBack => "card-welcome-back",
        HudScene.CardBossQuestion => "card-question-boss",
        _ => "card-question-collect",
    };

    /// <summary>
    /// Cenas cujo layout não depende da medição de texto do SO (medido: Windows × Linux). As demais (widget: largura natural vem de
    /// MeasureText; Arsenal/Missões/Migrar/boas-vindas: quebra de linha e posições) diferem em 1–3 px entre SOs mesmo sem glifos,
    /// então a variante -notext delas também só compara no Linux. Renderer não alterado (divergência registrada no REPORT T5.6).
    /// </summary>
    public static bool NoTextIsCrossOs(HudScene scene) => scene is HudScene.PanelCampaign or HudScene.CardBossQuestion or HudScene.CardCollectQuestion;

    public static string GoldenName(HudScene scene, int dpi, bool text = true) => $"{Name(scene)}-{dpi * 100 / 96}{(text ? "" : "-notext")}";

    private static WidgetModel Widget(HudScene scene) => scene switch
    {
        HudScene.WidgetAbsent => new(1, "48,2K", "+39/s", "37/60", false, "×0,25", null, null, 0, null, -1),
        HudScene.WidgetHover => new(1, "48,2K", "+312/s", "37/60", true, "×2,0", null, null, 0, null, (int)WidgetTab.Fases),
        HudScene.WidgetActive => new(1, "48,2K", "+312/s", "37/60", true, "×2,0", null, null, 0, WidgetTab.Arsenal, -1),
        HudScene.WidgetBadge => new(1, "48,2K", "+312/s", "37/60", true, "×2,0", "×3 0:24", "+186K", 3, null, -1),
        _ => new(1, "48,2K", "+312/s", "37/60", true, "×2,0", null, null, 0, null, -1),
    };

    private static SimState PanelState()
    {
        var layout = SideWar.Rendering.Tests.TestLayouts.FromCenters(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 380f });
        var s = SimState.CreateInitial(Content, layout, Rng.Seed(1, 2));
        s.Credits = 48_200;
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, SideWar.Rendering.Tests.TestLayouts.X(0)));
        s.Apply(Ctx, new BuildTower(TowerType.Frost, SideWar.Rendering.Tests.TestLayouts.X(1)));
        s.Stage = 3; s.Wave = 37; s.UnlockedStage = 3;
        s.RestoreStageProgress(1, 0, 3);
        return s;
    }

    private static SKBitmap Over(SKBitmap layer, int pad)
    {
        var bmp = new SKBitmap(new SKImageInfo(layer.Width + 2 * pad, layer.Height + 2 * pad, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        c.Clear(Backdrop);
        GoldenImage.Blit(c, layer, pad, pad);
        return bmp;
    }

    private static SKBitmap Layer(int w, int h) => new(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));

    /// <summary>Renderiza a cena. <paramref name="text"/> falso: layout idêntico, sem glifos (comparável entre SOs).</summary>
    public static SKBitmap Render(HudScene scene, int dpi, bool text = true)
    {
        using var typo = new Typography();
        using var icons = new IconCatalog();
        var tm = new PanelTextMetricsSkia(typo);
        float s = dpi / 96f;
        switch (scene)
        {
            case <= HudScene.WidgetBadge:
            {
                using var r = new WidgetRenderer(typo, icons) { TextEnabled = text };
                var m = Widget(scene);
                var l = r.GetLayout(m, 48, dpi);
                using var layer = Layer((int)Math.Round(l.WidthDip * s), (int)Math.Round(48 * s));
                using (var c = new SKCanvas(layer)) r.Render(c, m, 48, dpi);
                return Over(layer, (int)Math.Round(8 * s));
            }
            case HudScene.PanelArsenal or HudScene.PanelCampaign:
            {
                using var atlas = SpriteAtlas.LoadEmbedded();
                using var cache = new ScaledSpriteCache(atlas);
                cache.Bake(s);
                var ctx = new ArsenalCampaignPaintContext(typo, cache, dpi) { TextEnabled = text };
                int w = (int)Math.Round(400 * s), h = (int)Math.Round(548 * s);
                using var layer = Layer(w, h);
                using (var c = new SKCanvas(layer))
                {
                    c.Clear(SideWar.Rendering.Theme.Palette.BordaPainel);
                    using var inner = new SKPaint { Color = SideWar.Rendering.Theme.Palette.BgPainel };
                    float b = MathF.Round(2 * s);
                    c.DrawRect(new SKRect(b, b, w - b, h - b), inner);
                    using var rule = new SKPaint { Color = SideWar.Rendering.Theme.Palette.BordaPainel };
                    c.DrawRect(new SKRect(b, MathF.Round(54 * s), w - b, MathF.Round(55 * s)), rule);
                    var state = PanelState();
                    if (scene == HudScene.PanelArsenal)
                        ArsenalTabRenderer.Draw(c, ArsenalLayout.Compute(ArsenalModelBuilder.Build(state, Ctx, true), tm), ArsenalHit.None, 0, 491, ctx);
                    else
                        CampaignTabRenderer.Draw(c, CampaignLayout.Compute(CampaignModelBuilder.Build(state, Ctx), tm), 0, 491, ctx);
                }
                return Over(layer, (int)Math.Round(8 * s));
            }
            case HudScene.PanelMissions or HudScene.PanelMigrate:
            {
                using var r = new MissionsMigratePanelRenderer(typo, icons, dpi) { TextEnabled = text };
                var (w, h) = r.WindowSize;
                using var layer = Layer(w, h);
                using var c = new SKCanvas(layer);
                var view = new PanelViewState();
                if (scene == HudScene.PanelMissions)
                {
                    var m = MissionsTabModel.TestData();
                    var l = MissionsLayout.Compute(m, tm);
                    view.SetContent(l.ContentHeight, SideWar.Runtime.Geometry.PanelScroll.ViewportDip);
                    r.DrawMissions(c, m, l, view, TabBodyHit.None);
                }
                else
                {
                    var m = MigrateTabModel.TestData(chosenRegion: 1);
                    var l = MigrateLayout.Compute(m, tm);
                    view.SetContent(l.ContentHeight, SideWar.Runtime.Geometry.PanelScroll.ViewportDip);
                    r.DrawMigrate(c, m, l, view, TabBodyHit.None);
                }
                return Over(layer, (int)Math.Round(8 * s));
            }
            case HudScene.CardWelcomeBack:
            {
                var rep = new OfflineReport(3 * 3600 + 12 * 60, 186_000, 22, 37, 0, 4100, 22, 23,
                    [new(TowerType.Bolt, 12, 14), new(TowerType.Frost, 8, 9)], 6, 0, 4, 3, 1, false);
                var m = OfflineCardModel.From(new PendingRewards(1, rep, 186_000, 4100), true);
                var l = OfflineCardLayout.Compute(m, tm);
                using var r = new OfflineCardRenderer(typo, icons, dpi) { TextEnabled = text };
                var (w, h) = r.WindowSize;
                using var layer = Layer(w, h);
                using (var c = new SKCanvas(layer)) r.Draw(c, m, l);
                return Over(layer, (int)Math.Round(8 * s));
            }
            default:
            {
                var m = scene == HudScene.CardBossQuestion
                    ? QuestionCardModel.TestData()
                    : QuestionCardModel.TestData() with { Header = "COLETAR ×2 · FASE 03" };
                var l = QuestionCardLayout.Compute(m, tm);
                using var r = new QuestionCardRenderer(typo, icons, dpi) { TextEnabled = text };
                var (w, h) = r.WindowSize(l.Height);
                using var layer = Layer(w, h);
                using (var c = new SKCanvas(layer)) r.Draw(c, m, l);
                return Over(layer, (int)Math.Round(8 * s));
            }
        }
    }
}
