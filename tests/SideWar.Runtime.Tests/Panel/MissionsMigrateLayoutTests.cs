using SideWar.Runtime.Geometry;
using SideWar.Runtime.Panel;
using Xunit;

namespace SideWar.Runtime.Tests.Panel;

public class MissionsMigrateLayoutTests
{
    private static readonly FakeTextMetrics Tm = new();

    [Fact(DisplayName = "CA-24.7 presence card: 3 equal bands, bars 5 DIP, COLETAR only on completed rows")]
    public void Missions_Structure()
    {
        var m = MissionsTabModel.TestData();
        var l = MissionsLayout.Compute(m, Tm);
        Assert.Equal(18f, l.PresenceCard.X);
        Assert.Equal(364f, l.PresenceCard.W);
        Assert.Equal(69f, l.PresenceCard.Y);
        Assert.Equal(3, l.Bands.Length);
        Assert.All(l.Bands, b => Assert.Equal(l.Bands[0].W, b.W));
        Assert.Equal(1, m.Bands.ToList().FindIndex(b => b.Current));
        Assert.Equal(4, l.Rows.Length);
        Assert.All(l.Rows, r => Assert.Equal(5f, r.BarTrack.H));
        Assert.Equal([true, true, false, false], l.Rows.Select(r => r.Collect is not null));
        Assert.All(l.Rows.Where(r => r.Collect is not null), r =>
        {
            Assert.True(r.Collect!.Value.W >= 72f);
            Assert.Equal(44f, r.Collect.Value.H);
        });
        Assert.Equal(0.7f * l.Rows[2].BarTrack.W, l.Rows[2].BarFill, 3);
        Assert.Equal(l.Rows[0].BarTrack.W, l.Rows[0].BarFill, 3);
        Assert.Equal(l.Rows[^1].Row.Y + l.Rows[^1].Row.H + 14 - 55, l.ContentHeight, 3);
    }

    [Fact(DisplayName = "Collected mission has no button; multiplier formats with comma")]
    public void Missions_CollectedHasNoButton()
    {
        var m = MissionsTabModel.TestData();
        var rows = m.Missions.ToArray();
        rows[0] = rows[0] with { Collected = true, ProgressText = "COLETADA" };
        var l = MissionsLayout.Compute(m with { Missions = rows }, Tm);
        Assert.Null(l.Rows[0].Collect);
        Assert.Equal("×2,0", MissionsTabModel.FormatMultiplier(2.0));
        Assert.Equal("×0,25", MissionsTabModel.FormatMultiplier(0.25));
        Assert.Equal("×1,5", MissionsTabModel.FormatMultiplier(1.5));
    }

    [Fact(DisplayName = "T-24.5 missions hit-test: button hit, clipped outside viewport, offset applied")]
    public void Missions_Hit()
    {
        var l = MissionsLayout.Compute(MissionsTabModel.TestData(), Tm);
        var b = l.Rows[0].Collect!.Value;
        var hit = l.Hit(b.X + 5, b.Y + 5, 0f);
        Assert.Equal(new TabBodyHit(TabBodyHitKind.MissionCollect, 0), hit);
        Assert.Equal(TabBodyHit.None, l.Hit(b.X - 1, b.Y + 5, 0f));
        // rolado: o botão sobe; no ponto original não há mais alvo
        Assert.Equal(TabBodyHitKind.MissionCollect, l.Hit(b.X + 5, b.Y + 5 - 20, 20f).Kind);
        // parcialmente fora do viewport (acima de y = 55): não responde
        Assert.Equal(TabBodyHit.None, l.Hit(b.X + 5, 54f, b.Y - 54f + 5f));
        // abaixo do viewport
        Assert.Equal(TabBodyHit.None, l.Hit(b.X + 5, 546f, b.Y - 546f + 5f));
    }

    [Fact(DisplayName = "CA-24.8 migrate: button 48 DIP full width pinned to end when short; tiles; 44 DIP selectable rows")]
    public void Migrate_Structure()
    {
        var m = MigrateTabModel.TestData();
        var l = MigrateLayout.Compute(m, Tm);
        Assert.Equal(48f, l.Button.H);
        Assert.Equal(364f, l.Button.W);
        Assert.Equal(2, l.Tiles.Length);
        Assert.Equal(l.Tiles[0].W, l.Tiles[1].W);
        Assert.Equal(8f, l.Tiles[1].X - (l.Tiles[0].X + l.Tiles[0].W), 3);
        Assert.True(l.ContentHeight >= 491f);
        if (l.ContentHeight == 491f) Assert.Equal(55f + 491f - 14f - 48f, l.Button.Y, 3);
        Assert.Equal(44f, l.Regions[0].Row.H);
        Assert.Equal(44f, l.Regions[1].Row.H);
        Assert.True(l.Regions[2].Row.H < 44f && !l.Regions[2].Selectable);
        Assert.Equal("MIGRAR · +8 RESERVAS", m.ButtonLabel);
        Assert.Equal("14", m.CurrentReserves);
        Assert.Equal("+14% produção", m.CurrentProduction);
        Assert.Equal("+8", m.GainNow);
        Assert.Equal("total +22%", m.TotalProduction);
    }

    [Fact(DisplayName = "Migrate button follows content when taller than the viewport")]
    public void Migrate_ButtonAfterContentWhenTall()
    {
        var l = MigrateLayout.Compute(MigrateTabModel.TestData(), Tm, viewport: 200f);
        var lastRegionEnd = l.Regions[^1].Row.Y + l.Regions[^1].Row.H;
        Assert.Equal(lastRegionEnd + 12f, l.Button.Y, 3);
        Assert.Equal(l.Button.Y + 48f + 14f - 55f, l.ContentHeight, 3);
    }

    [Fact(DisplayName = "T-24.5 migrate hit-test: region rows (selectable only), MIGRAR, unavailable has no hit")]
    public void Migrate_Hit()
    {
        var m = MigrateTabModel.TestData();
        var l = MigrateLayout.Compute(m, Tm);
        var r1 = l.Regions[1].Row;
        Assert.Equal(new TabBodyHit(TabBodyHitKind.RegionRow, 1), l.Hit(r1.X + 10, r1.Y + 22, 0f));
        var r2 = l.Regions[2].Row;
        Assert.Equal(TabBodyHit.None, l.Hit(r2.X + 10, r2.Y + 2, 0f));
        Assert.Equal(new TabBodyHit(TabBodyHitKind.Migrate), l.Hit(l.Button.X + 5, l.Button.Y + 5, 0f));
        var off = MigrateTabModel.TestData(maxWave: 10);
        Assert.Equal(MigrateButtonState.Unavailable, off.Button);
        Assert.Equal("DISPONÍVEL NA ONDA 15", off.ButtonLabel);
        var lo = MigrateLayout.Compute(off, Tm);
        Assert.Equal(TabBodyHit.None, lo.Hit(lo.Button.X + 5, lo.Button.Y + 5, 0f));
    }

    [Fact(DisplayName = "Chosen region gets '→ ' and principal tone; armed label")]
    public void Migrate_ChosenAndArmedLabels()
    {
        var m = MigrateTabModel.TestData(chosenRegion: 1, armed: true);
        Assert.StartsWith("→ East US", m.Regions[1].Text);
        Assert.Equal(RegionTone.Principal, m.Regions[1].Tone);
        Assert.Equal(MigrateButtonState.Armed, m.Button);
        Assert.Equal("CONFIRMAR · REINICIA TORRES E ONDAS", m.ButtonLabel);
        Assert.StartsWith("Brazil South", m.Regions[0].Text);
    }

    [Fact(DisplayName = "PanelViewState: SetContent per tab, ResetOffset on tab switch, content limits offset")]
    public void ViewState_PerTab()
    {
        var v = new PanelViewState();
        var ml = MissionsLayout.Compute(MissionsTabModel.TestData(), Tm);
        v.SetContent(ml.ContentHeight, PanelScroll.ViewportDip);
        v.Wheel(-120);
        var expected = Math.Min(40f, PanelScroll.MaxOffset(ml.ContentHeight, 491f));
        Assert.Equal(expected, v.Offset);
        var gl = MigrateLayout.Compute(MigrateTabModel.TestData(), Tm);
        v.ResetOffset();
        v.SetContent(gl.ContentHeight, PanelScroll.ViewportDip);
        Assert.Equal(0f, v.Offset);
        Assert.Equal(gl.ContentHeight, v.Content);
    }

    [Fact(DisplayName = "TextFit: ellipsis and wrap with line limit")]
    public void TextFit_Works()
    {
        var s = TextFit.Ellipsize("abcdefghijklmnopqrstuvwxyz", 60f, PanelFont.JetBrainsMono, 10, Tm);
        Assert.EndsWith("…", s);
        Assert.True(Tm.Width(PanelFont.JetBrainsMono, 10, s) <= 60f);
        var lines = TextFit.Wrap("um dois tres quatro cinco seis sete oito", 60f, PanelFont.JetBrainsMono, 10, Tm, maxLines: 2);
        Assert.Equal(2, lines.Length);
        Assert.EndsWith("…", lines[1]);
        Assert.Equal("curto", TextFit.Ellipsize("curto", 60f, PanelFont.JetBrainsMono, 10, Tm));
    }
}
