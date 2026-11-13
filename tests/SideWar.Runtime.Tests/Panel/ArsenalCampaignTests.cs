using SideWar.Core.Content;
using SideWar.Core.Simulation;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Panel;
using Xunit;

namespace SideWar.Runtime.Tests.Panel;

public sealed class ArsenalCampaignTests
{
    private sealed class MonoMetrics : IPanelTextMetrics
    {
        public float Width(PanelFont font, float sizeDip, string text, float letterSpacing = 0f) => text.Length * (sizeDip * 0.6f + letterSpacing);
        public float LineHeight(PanelFont font, float sizeDip) => sizeDip * 1.32f;
    }

    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);
    private static readonly MonoMetrics Tm = new();

    private static SimState NewState(long credits = 1_000_000, int slots = 4)
    {
        var xs = new float[slots];
        for (int i = 0; i < slots; i++) xs[i] = 200f + 60f * i;
        var s = SimState.CreateInitial(Content, TestLayouts.FromCenters(1000f, 94f, 10f, 980f, xs), Rng.Seed(42, 54));
        s.Credits = credits;
        return s;
    }

    [Fact]
    public void Arsenal_UpgradeRow_TextsAndCostsFromTowerCosts()
    {
        var s = NewState();
        Assert.True(s.Apply(Ctx, new BuildTower(TowerType.Bolt, TestLayouts.X(0))).IsOk);
        var m = ArsenalModelBuilder.Build(s, Ctx, true);
        var r = m.Rows[0];
        Assert.Equal(ArsenalRowKind.Tower, r.Kind);
        Assert.Equal("RAIO", r.Name);
        Assert.Equal("LV 1", r.LevelText);
        Assert.Equal("Máquina Virtual (Azure VM)", r.Azure);
        Assert.Equal("Dano 18 · Alc 3 · Cad 0,7s", r.Stats);
        Assert.Equal("MELHORAR", r.ButtonLabel);
        Assert.Equal("54", r.CostText); // ceil(45 × 1,2)
        Assert.Equal(ArsenalButtonStyle.Amber, r.ButtonStyle);
        Assert.Equal(1f, r.Opacity);
    }

    [Fact]
    public void Arsenal_ScaleSetLockedUntilStage9_ThenAbsentAsLocked()
    {
        var s = NewState();
        var locked = ArsenalModelBuilder.Build(s, Ctx, true).Rows.Single(x => x.Type == TowerType.ScaleSet);
        Assert.Equal(ArsenalRowKind.Locked, locked.Kind);
        Assert.Equal("BLOQ.", locked.LevelText);
        Assert.Equal("Desbloqueia na fase 09", locked.Stats);
        Assert.Equal("FASE 09", locked.ButtonLabel);
        Assert.Equal("", locked.CostText);
        Assert.Equal(0.45f, locked.Opacity);
        Assert.Equal(ArsenalButtonStyle.Locked, locked.ButtonStyle);
        s.Stage = 9;
        Assert.DoesNotContain(ArsenalModelBuilder.Build(s, Ctx, true).Rows, x => x.Kind == ArsenalRowKind.Locked);
    }

    [Fact]
    public void Arsenal_TowerWithoutSlot_IsNoSlotRow_AfterSlotted_NoHit()
    {
        var s = NewState();
        s.Apply(Ctx, new BuildTower(TowerType.Cannon, TestLayouts.X(3)));
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, TestLayouts.X(0)));
        s.ApplyLayout(TestLayouts.FromCenters(1000f, 94f, 10f, 980f, new[] { 200f, 260f }));
        var m = ArsenalModelBuilder.Build(s, Ctx, true);
        Assert.Equal([ArsenalRowKind.Tower, ArsenalRowKind.NoSlot, ArsenalRowKind.Locked], m.Rows.Select(x => x.Kind).ToArray());
        var ns = m.Rows[1];
        Assert.Equal("SEM SLOT", ns.LevelText);
        Assert.Equal("SEM SLOT", ns.ButtonLabel);
        Assert.Equal(0.45f, ns.Opacity);
        var lay = ArsenalLayout.Compute(m, Tm);
        Assert.True(lay.Rows[1].ButtonHit.IsNone);
        Assert.True(lay.Rows[2].ButtonHit.IsNone);
    }

    [Fact]
    public void Arsenal_FocusEmptySlot_BuildsRowsWithBuildCost_AndIntentIsBuildTower()
    {
        var s = NewState();
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, TestLayouts.X(0))); // n = 1 → Raio 68
        var m = ArsenalModelBuilder.Build(s, Ctx, true, focusSlot: TestLayouts.X(2)); // ADR-034: o "slot" focado passa a ser o X da posição livre (UI real: TA.11)
        Assert.Equal(TestLayouts.X(2), m.BuildSlot);
        var b = m.Rows.Where(r => r.Kind == ArsenalRowKind.Build).ToArray();
        Assert.Equal(3, b.Length); // Raio, Gelo, Canhão (Scale Set bloqueado)
        Assert.Equal("68", b[0].CostText);
        Assert.Equal("CONSTRUIR", b[0].ButtonLabel);
        var lay = ArsenalLayout.Compute(m, Tm);
        Assert.Equal(ArsenalHit.Build(TowerType.Bolt, TestLayouts.X(2)), lay.Rows[0].ButtonHit);
        var intent = ArsenalIntent.FromHit(lay.Rows[0].ButtonHit, true);
        Assert.Equal(new BuildTower(TowerType.Bolt, TestLayouts.X(2)), intent!.Value.Command);
        // slot ocupado ou inexistente: sem seção de construir
        Assert.Null(ArsenalModelBuilder.Build(s, Ctx, true, focusSlot: TestLayouts.X(0)).BuildSlot);
        Assert.Null(ArsenalModelBuilder.Build(s, Ctx, true, focusSlot: 9).BuildSlot);
    }

    [Fact]
    public void Intents_AreReturnedNotApplied()
    {
        var s = NewState();
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, TestLayouts.X(0)));
        long credits = s.Credits;
        var tower = s.Towers.At(0);
        var i = ArsenalIntent.FromHit(ArsenalHit.Upgrade(tower.Id), true)!.Value;
        Assert.Equal(new UpgradeTower(tower.Id), i.Command);
        Assert.Equal(credits, s.Credits);
        Assert.Equal(1, s.Towers.At(0).Level);
        var t = ArsenalIntent.FromHit(ArsenalHit.AutoScale, true)!.Value;
        Assert.Equal(ArsenalIntentKind.SetAutoScale, t.Kind);
        Assert.False(t.AutoScaleOn);
        Assert.Null(ArsenalIntent.FromHit(ArsenalHit.None, true));
        // aplicado entre ticks, o resultado é do Core
        Assert.True(s.Apply(Ctx, i.Command).IsOk);
        Assert.Equal(2, s.Towers.At(0).Level);
    }

    [Fact]
    public void ArsenalLayout_Geometry_MatchesSpec()
    {
        var s = NewState();
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, TestLayouts.X(0)));
        var lay = ArsenalLayout.Compute(ArsenalModelBuilder.Build(s, Ctx, true), Tm);
        Assert.Equal(new DipRect(16, 14, 364, lay.AutoCard.Rect.H), lay.AutoCard.Rect);
        Assert.True(lay.AutoCard.Toggle.H >= 44 && lay.AutoCard.Toggle.W >= 44);
        Assert.Equal(20f, lay.AutoCard.Box.W);
        var row = lay.Rows[0];
        Assert.Equal(74f, row.Rect.H);
        Assert.Equal(lay.AutoCard.Rect.Y + lay.AutoCard.Rect.H + 10f, row.Rect.Y);
        Assert.Equal(new DipRect(29, row.Rect.Y + 11, 52, 52), row.Slot);
        Assert.Equal(84f, row.Button.W);
        Assert.Equal(44f, row.Button.H);
        Assert.Equal(367f, row.Button.X + row.Button.W);
        Assert.Equal(lay.Rows[^1].Rect.Y + 74f + 14f, lay.ContentHeight);
    }

    [Fact]
    public void ArsenalHit_ButtonsToggleClipAndScroll()
    {
        var s = NewState();
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, TestLayouts.X(0)));
        int id = s.Towers.At(0).Id;
        var lay = ArsenalLayout.Compute(ArsenalModelBuilder.Build(s, Ctx, true), Tm);
        var b = lay.Rows[0].Button;
        float px = 2 + b.X + 5, py = 55 + b.Y + 5;
        Assert.Equal(ArsenalHit.Upgrade(id), lay.Hit(px, py, 0, 491));
        Assert.Equal(ArsenalHit.Upgrade(id), lay.Hit(px, py - 30, 30, 491)); // rolado
        Assert.True(lay.Hit(px - 100, py, 0, 491).IsNone);                  // fora do botão
        var t = lay.AutoCard.Toggle;
        Assert.Equal(ArsenalHit.AutoScale, lay.Hit(2 + t.X + 1, 55 + t.Y + 43, 0, 491));
        // botão parcialmente fora do viewport só responde na parte visível
        Assert.True(lay.Hit(px, 54f, 0, 491).IsNone);
        Assert.Equal(ArsenalHit.Upgrade(id), lay.Hit(px, 55f, b.Y, 491));
        Assert.True(lay.Hit(px, 55f + 10f, b.Y + 20, 10).IsNone); // abaixo do viewport encolhido
        Assert.True(lay.TryGetTowerRowTop(id, out var top));
        Assert.Equal(lay.Rows[0].Rect.Y, top);
    }

    [Fact]
    public void Arsenal_AutoScaleOff_ShowsOffText_AndSameDisplayDetectsChanges()
    {
        var s = NewState();
        var on = ArsenalModelBuilder.Build(s, Ctx, true);
        var off = ArsenalModelBuilder.Build(s, Ctx, false);
        Assert.True(on.SameDisplay(ArsenalModelBuilder.Build(s, Ctx, true)));
        Assert.False(on.SameDisplay(off));
        Assert.Equal("OFF", ArsenalLayout.Compute(off, Tm).AutoCard.ToggleText);
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, TestLayouts.X(0)));
        Assert.False(on.SameDisplay(ArsenalModelBuilder.Build(s, Ctx, true)));
    }

    [Fact]
    public void Arsenal_FrostAndCannonStatsFollowDesignFormat()
    {
        var s = NewState();
        s.Apply(Ctx, new BuildTower(TowerType.Frost, TestLayouts.X(0)));
        s.Apply(Ctx, new BuildTower(TowerType.Cannon, TestLayouts.X(1)));
        var m = ArsenalModelBuilder.Build(s, Ctx, true);
        Assert.Equal("Lentidão 35% · Alc 2", m.Rows[0].Stats);
        Assert.Equal("Dano 60 em área · Cad 2,1s", m.Rows[1].Stats);
    }

    [Fact]
    public void Campaign_CardAndRows()
    {
        var s = NewState();
        s.Stage = 3; s.Wave = 37; s.UnlockedStage = 3;
        s.RestoreStageProgress(1, 0, 3);
        s.RestoreStageProgress(2, 0, 2);
        var m = CampaignModelBuilder.Build(s, Ctx);
        Assert.Equal("FASE ATUAL · 03", m.StageLabel);
        Assert.Equal("Controle da máquina", m.StageTitle);
        Assert.Equal("Onda 37 de 60", m.WaveText);
        Assert.Equal("Chefe na onda 40 · tema IaaS", m.BossText);
        Assert.Equal(37f / 60f, m.Fraction);
        Assert.Equal(10, m.Rows.Count);
        Assert.Equal([3, 2, 0], m.Rows.Take(3).Select(r => r.Stars).ToArray());
        Assert.All(m.Rows.Take(3), r => Assert.False(r.Locked));
        Assert.All(m.Rows.Skip(3), r => { Assert.True(r.Locked); Assert.Equal(0.55f, r.Opacity); });
        Assert.Equal("04", m.Rows[3].Number);
        s.Wave = 40;
        Assert.Equal("Chefe na onda 40 · tema IaaS", CampaignModelBuilder.Build(s, Ctx).BossText);
    }

    [Fact]
    public void CampaignLayout_RowsMinHeight44_AndLongTitleGrows()
    {
        var s = NewState();
        s.Stage = 6; s.Wave = 1; s.UnlockedStage = 6;
        var lay = CampaignLayout.Compute(CampaignModelBuilder.Build(s, Ctx), Tm);
        Assert.Equal(10, lay.Rows.Count);
        Assert.Equal(44f, lay.Rows[0].Rect.H);
        Assert.True(lay.Rows[5].TitleLines.Length >= 1);
        var narrow = new CampaignModel("FASE ATUAL · 01", "x", 1, 60, "Onda 1 de 60", "Chefe na onda 20 · tema y",
            [new CampaignRowModel("01", string.Join(' ', Enumerable.Repeat("palavra", 12)), 1, false)]);
        var l2 = CampaignLayout.Compute(narrow, Tm);
        Assert.True(l2.Rows[0].TitleLines.Length > 1);
        Assert.True(l2.Rows[0].Rect.H > 44f);
        Assert.Equal(l2.Rows[0].Rect.Y + l2.Rows[0].Rect.H + 14f, l2.ContentHeight);
    }

    [Fact]
    public void PanelViewState_ContentHeightAndTabReset()
    {
        var s = NewState();
        for (int i = 0; i < 4; i++) s.Apply(Ctx, new BuildTower(TowerType.Bolt, TestLayouts.X(i)));
        var lay = ArsenalLayout.Compute(ArsenalModelBuilder.Build(s, Ctx, true), Tm);
        var v = new PanelViewState();
        v.SetContent(lay.ContentHeight, 491f);
        Assert.True(lay.ContentHeight > 491f);
        Assert.True(v.SetOffset(100f));
        v.ResetOffset();
        Assert.Equal(0f, v.Offset);
    }
}
