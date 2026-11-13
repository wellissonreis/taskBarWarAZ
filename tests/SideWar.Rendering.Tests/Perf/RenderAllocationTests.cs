using SideWar.Core.Content;
using SideWar.Core.Simulation;
using SideWar.Rendering.Animation;
using SideWar.Rendering.Icons;
using SideWar.Rendering.Panel;
using SideWar.Rendering.Scene;
using SideWar.Rendering.Sprites;
using SideWar.Rendering.Text;
using SideWar.Rendering.Widget;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Input;
using SideWar.Runtime.Panel;
using SideWar.Runtime.Power;
using SideWar.Runtime.Presence;
using SideWar.Runtime.Widget;
using SkiaSharp;
using Xunit;
using BlockReason = SideWar.Runtime.Taskbar.BlockReason;
using BlockedSpan = SideWar.Runtime.Taskbar.BlockedSpan;

namespace SideWar.Rendering.Tests.Perf;

/// <summary>
/// SDD-08 RNF-08.3 / CA-08.5: 0 B/frame em regime (<see cref="GC.GetAllocatedBytesForCurrentThread"/> em 1000 frames, após aquecimento
/// que cria caches de texto/fonte/blob). Cobre cena, overlay de construção, hotspots, widget, painel (Arsenal, Campanha, Missões, Migrar) e card.
/// </summary>
public sealed class RenderAllocationTests : IDisposable
{
    private const int Frames = 1000;
    private const int Warmup = 30;
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);
    private static readonly AnimationPolicy Normal = new(false, PowerProfile.Normal);
    private readonly SpriteAtlas _atlas = SpriteAtlas.LoadEmbedded();
    private readonly Typography _typo = new();
    private readonly IconCatalog _icons = new();

    public void Dispose() { _icons.Dispose(); _typo.Dispose(); _atlas.Dispose(); }

    /// <summary>Bytes alocados por frame na thread corrente; <paramref name="frame"/> recebe o índice do frame.</summary>
    private static long BytesPerFrame(Action<int> frame)
    {
        for (int i = 0; i < Warmup; i++) frame(i);
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Frames; i++) frame(Warmup + i);
        long total = GC.GetAllocatedBytesForCurrentThread() - before;
        return total;
    }

    private static void AssertZero(long totalBytes, string what) =>
        Assert.True(totalBytes == 0, $"{what}: {totalBytes} B em {Frames} frames ({(double)totalBytes / Frames:F1} B/frame); esperado 0.");

    private static SKBitmap Bmp(int w, int h) => new(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));

    private static SimState SceneState()
    {
        var slots = new float[8];
        for (int i = 0; i < 8; i++) slots[i] = 640 + 48 * i + 22;
        var s = new SimState(TestLayouts.FromCenters(1394, 88, 4, 1360, slots)) { BaseHp = 50, MaxBaseHp = 100 };
        s.Towers.Add(new Tower { Id = s.TakeId(), Type = TowerType.Bolt, Anchor = TowerAnchor.Start, OffsetDip = TestLayouts.SceneX(0), Placed = true, Level = 14 });
        s.Towers.Add(new Tower { Id = s.TakeId(), Type = TowerType.Frost, Anchor = TowerAnchor.Start, OffsetDip = TestLayouts.SceneX(1), Placed = true, Level = 3 });
        s.Towers.Add(new Tower { Id = s.TakeId(), Type = TowerType.Cannon, Anchor = TowerAnchor.Start, OffsetDip = TestLayouts.SceneX(2), Placed = true, Level = 7 });
        for (int i = 0; i < 24; i++)
            s.Enemies.Add(new Enemy { Id = s.TakeId(), Kind = EnemyKind.Grunt, X = 100 + 40 * i, Hp = 50 + i, MaxHp = 100 });
        s.Enemies.Add(new Enemy { Id = s.TakeId(), Kind = EnemyKind.Boss, X = 900, Hp = 80, MaxHp = 100, Flags = EnemyFlags.Boss });
        return s;
    }

    [Theory]
    [InlineData(96)]
    [InlineData(150)]
    public void SceneRenderer_Frame_IsZeroAlloc(int dpi)
    {
        var state = SceneState();
        using var r = new SceneRenderer(_atlas, _typo);
        r.SetDpi(dpi);
        using var bmp = Bmp(1440 * dpi / 96, 152 * dpi / 96);
        using var canvas = new SKCanvas(bmp);
        // números de dano vivos: Add reaproveita o pool de FloatingNumbers; textos vêm do TextCache
        long bytes = BytesPerFrame(i =>
        {
            double now = i / 60.0;
            if (i % 5 == 0) { r.Numbers.Add(500 + i % 300, now, i % 2 == 0 ? "LENTO" : "12", i % 3 == 0 ? FloatKind.Slow : FloatKind.Bolt); }
            r.Draw(canvas, state, now, Normal, StripEdgeMode.Present);
        });
        AssertZero(bytes, $"SceneRenderer {dpi} DPI");
    }

    [Fact]
    public void BuildOverlayRenderer_Frame_IsZeroAlloc()
    {
        var layout = StripLayout.Create(1920, 1880, 88, 4, 1840, TowerHost.Taskbar, [new FreeZone(100, 500), new FreeZone(1400, 1600)]);
        var s = new SimState(layout);
        s.Towers.Add(new Tower { Id = s.TakeId(), Type = TowerType.Bolt, Anchor = TowerAnchor.Start, OffsetDip = 300, Placed = true, Level = 1 });
        BlockedSpan[] blocked = [new BlockedSpan(686, 1332, BlockReason.StartSearchApps), new BlockedSpan(1700, 1800, BlockReason.TrayClock)];
        using var r = new BuildOverlayRenderer(_atlas, _typo);
        r.SetDpi(96);
        using var bmp = Bmp(1920, 152);
        using var canvas = new SKCanvas(bmp);
        var ghost = new BuildGhost(1500, true, PlacementReason.None);
        long bytes = BytesPerFrame(i => r.Draw(canvas, new BuildOverlayFrame(s, 48, blocked, ghost, TowerType.Bolt, "RAIO · 1,2K", i / 60.0)));
        AssertZero(bytes, "BuildOverlayRenderer");
    }

    [Fact]
    public void HotspotRenderer_Banner_Box_Seal_AreZeroAlloc()
    {
        using var scene = new SceneRenderer(_atlas, _typo);
        scene.SetDpi(96);
        using var hot = new HotspotRenderer(scene.SpriteCache, _typo, _icons);
        hot.SetDpi(96);
        using var bmp = Bmp(1440, 152);
        using var canvas = new SKCanvas(bmp);
        long bytes = BytesPerFrame(i =>
        {
            double now = i / 60.0;
            hot.DrawBanner(canvas, i % 2 == 0 ? BannerVariant.Peak : BannerVariant.Boss, "0:24", now, Normal);
            hot.DrawSupplyBox(canvas, now, Normal);
            hot.DrawSeal(canvas, "ausente há 1h 48min");
        });
        AssertZero(bytes, "HotspotRenderer");
    }

    [Fact]
    public void WidgetRenderer_Frame_IsZeroAlloc()
    {
        using var r = new WidgetRenderer(_typo, _icons);
        var present = new WidgetModel(1, "48,2K", "+312/s", "37/60", true, "×2,0", "×3 0:59", null, 2, WidgetTab.Arsenal, 1);
        var away = new WidgetModel(1, "48,2K", "+39/s", "37/60", false, "×0,25", null, "pendente", 0, null, -1);
        using var bmp = Bmp(700, 48);
        using var canvas = new SKCanvas(bmp);
        // regime = modelo estável entre frames (o render é por evento); presente (com hover/chip) e ausente (com pílula pendente) medidos em separado
        AssertZero(BytesPerFrame(i => r.Render(canvas, present, 48, 96)), "WidgetRenderer presente");
        AssertZero(BytesPerFrame(i => r.Render(canvas, away, 48, 96)), "WidgetRenderer ausente");
    }

    [Fact]
    public void ArsenalAndCampaignTabs_AreZeroAlloc()
    {
        var s = SimState.CreateInitial(Content, TestLayouts.FromCenters(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 380f }), Rng.Seed(1, 2));
        s.Credits = 1_000_000;
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, TestLayouts.X(0)));
        using var cache = new ScaledSpriteCache(_atlas);
        cache.Bake(1f);
        var ctx = new ArsenalCampaignPaintContext(_typo, cache, 96);
        var tm = new PanelTextMetricsSkia(_typo);
        var arsenal = ArsenalLayout.Compute(ArsenalModelBuilder.Build(s, Ctx, true), tm);
        var campaign = CampaignLayout.Compute(CampaignModelBuilder.Build(s, Ctx), tm);
        using var bmp = Bmp(400, 548);
        using var canvas = new SKCanvas(bmp);
        AssertZero(BytesPerFrame(i => ArsenalTabRenderer.Draw(canvas, arsenal, ArsenalHit.None, 0, 491, ctx)), "ArsenalTabRenderer");
        AssertZero(BytesPerFrame(i => CampaignTabRenderer.Draw(canvas, campaign, 0, 491, ctx)), "CampaignTabRenderer");
    }

    [Fact]
    public void MissionsMigrateAndQuestionCard_AreZeroAlloc()
    {
        var tm = new PanelTextMetricsSkia(_typo);
        using var r = new MissionsMigratePanelRenderer(_typo, _icons, 96);
        var (w, h) = r.WindowSize;
        using var bmp = Bmp(w, h);
        using var canvas = new SKCanvas(bmp);
        var mm = MissionsTabModel.TestData();
        var ml = MissionsLayout.Compute(mm, tm);
        var gm = MigrateTabModel.TestData();
        var gl = MigrateLayout.Compute(gm, tm);
        var view = new PanelViewState();
        view.SetContent(Math.Max(ml.ContentHeight, gl.ContentHeight), PanelScroll.ViewportDip);
        AssertZero(BytesPerFrame(i => r.DrawMissions(canvas, mm, ml, view, TabBodyHit.None)), "Missions");
        AssertZero(BytesPerFrame(i => r.DrawMigrate(canvas, gm, gl, view, new TabBodyHit(TabBodyHitKind.Migrate))), "Migrate");

        var qm = QuestionCardModel.TestData(QuestionCardState.Wrong, 0);
        var ql = QuestionCardLayout.Compute(qm, tm);
        using var card = new QuestionCardRenderer(_typo, _icons, 96);
        var (cw, ch) = card.WindowSize(ql.Height);
        using var cbmp = Bmp(cw, ch);
        using var ccanvas = new SKCanvas(cbmp);
        AssertZero(BytesPerFrame(i => card.Draw(ccanvas, qm, ql)), "QuestionCard");
    }
}
