using SideWar.Core.Simulation;
using SideWar.Runtime.Panel;
using SideWar.Runtime.Presence;
using Xunit;

namespace SideWar.Runtime.Tests.Panel;

public sealed class OfflineCardLayoutTests
{
    private static readonly FakeTextMetrics Tm = new();

    private static OfflineReport Report(int quedas = 0, int chefes = 1, int picos = 4, int caixas = 3, params AutoUpgrade[] ups)
        => new(3 * 3600 + 12 * 60, 186_000, 22, 37, quedas, 4100, 22, 23, ups, ups.Length, 0, picos, caixas, chefes, false);

    private static OfflineCardModel Model(OfflineReport? r = null, bool auto = true, Func<string, bool>? fit = null)
        => OfflineCardModel.From(new PendingRewards(1, r ?? Report(ups: [new(TowerType.Bolt, 12, 14), new(TowerType.Frost, 8, 9)]), 186_000, 4100), auto, fit);

    [Fact(DisplayName = "CA-24.11 textos do design: tempo, tiles, botões e aviso de perdas")]
    public void DesignTexts()
    {
        var m = Model();
        Assert.Equal("Você esteve fora por 3h 12min. A auto-escala segurou o núcleo.", m.Intro);
        Assert.Equal("3h 12min / 12h máx.", m.TimeLabelRight);
        Assert.Equal(0.267f, m.TimeFraction, 0.001f);   // design: 27%
        Assert.Equal(["CRÉDITOS", "ONDAS", "XP", "AUTO-ESCALA"], m.Tiles.Select(t => t.Label));
        Assert.Equal(("+186K", "a ×0,25"), (m.Tiles[0].Value, m.Tiles[0].Sub));
        Assert.Equal(("22 → 37", "sem perder vida"), (m.Tiles[1].Value, m.Tiles[1].Sub));
        Assert.Equal(("+4,1K", "LV 22 → 23"), (m.Tiles[2].Value, m.Tiles[2].Sub));
        Assert.Equal(("2 melhorias", "Raio 12→14 · Gelo 8→9"), (m.Tiles[3].Value, m.Tiles[3].Sub));
        Assert.Equal("Perdido por estar ausente: 1 chefe (pergunta Azure), 4 picos de demanda e 3 caixas. Esses só aparecem com você na tela.", m.WarningText);
        Assert.Equal("COLETAR ×2", m.CollectX2Label);
        Assert.Equal("responda 1 pergunta Azure da fase", m.CollectX2Hint);
        Assert.Equal("COLETAR +186K", m.CollectLabel);
    }

    [Fact(DisplayName = "Frases condicionais: sem auto-escala/melhorias, quedas e perdas zeradas")]
    public void Conditionals()
    {
        Assert.EndsWith("min.", Model(auto: false).Intro);
        Assert.EndsWith("min.", Model(Report()).Intro);   // NMelhorias = 0
        Assert.Equal("sem melhorias", Model(Report()).Tiles[3].Sub);
        Assert.Equal("2 quedas", Model(Report(quedas: 2)).Tiles[1].Sub);
        Assert.Equal("1 queda", Model(Report(quedas: 1)).Tiles[1].Sub);
        Assert.Null(Model(Report(0, 0, 0, 0)).WarningText);
        Assert.Equal("Perdido por estar ausente: 1 pico de demanda. Esses só aparecem com você na tela.", Model(Report(0, 0, 1, 0)).WarningText);
        Assert.Equal("Perdido por estar ausente: 2 picos de demanda e 1 caixa. Esses só aparecem com você na tela.", Model(Report(0, 0, 2, 1)).WarningText);
    }

    [Fact(DisplayName = "Lista de melhorias termina em · +N quando não cabe")]
    public void UpgradeListOverflow()
    {
        var r = Report(ups: [new(TowerType.Bolt, 12, 14), new(TowerType.Frost, 8, 9), new(TowerType.Cannon, 1, 2), new(TowerType.ScaleSet, 3, 4)]);
        Assert.Equal("Raio 12→14 · Gelo 8→9 · +2", OfflineCardModel.UpgradeList(r, s => s.Length <= 30));
        Assert.Equal("+4", OfflineCardModel.UpgradeList(r, s => s.Length <= 3));
        Assert.Equal("Raio 12→14 · Gelo 8→9 · Canhão 1→2 · Scale Set 3→4", OfflineCardModel.UpgradeList(r, _ => true));
    }

    [Theory(DisplayName = "Formato de duração")]
    [InlineData(0, "0min")]
    [InlineData(600, "10min")]
    [InlineData(3600, "1h")]
    [InlineData(11520, "3h 12min")]
    [InlineData(43200, "12h")]
    public void Duration(double s, string expected) => Assert.Equal(expected, OfflineCardModel.FormatDuration(s));

    [Fact(DisplayName = "Layout: 472 × 612, blocos em coluna sem sobreposição, botões na base (52 + 8 + 44)")]
    public void LayoutGeometry()
    {
        var l = OfflineCardLayout.Compute(Model(), Tm);
        Assert.Equal(24f, l.OverlineBox.X);
        Assert.Equal(424f, l.IntroBox.W);
        Assert.Equal(588f - 44f, l.Collect.Y);
        Assert.Equal(44f, l.Collect.H);
        Assert.Equal(52f, l.CollectX2.H);
        Assert.Equal(l.CollectX2.Y + 52f + 8f, l.Collect.Y);
        Assert.Equal(588f, l.Collect.Y + l.Collect.H);
        Assert.True(l.ContentBottom + OfflineCardLayout.Gap <= l.CollectX2.Y, $"conteúdo termina em {l.ContentBottom}");
        Assert.Equal(4, l.Tiles.Length);
        Assert.Equal(l.Tiles[0].Box.Y, l.Tiles[1].Box.Y);
        Assert.Equal(l.Tiles[0].Box.Y + l.Tiles[0].Box.H + 8f, l.Tiles[2].Box.Y);
        Assert.Equal(208f, l.Tiles[0].Box.W);
        Assert.Equal(24f + 208f + 8f, l.Tiles[1].Box.X);
        Assert.Equal(MathF.Round(424f * 0.267f), l.BarFill.W, 1f);
        Assert.NotNull(l.WarningBox);
        Assert.Equal(20f, l.WarningIcon.W);
    }

    [Fact(DisplayName = "Layout: sem perdas o aviso é omitido")]
    public void NoWarningLayout()
    {
        var l = OfflineCardLayout.Compute(Model(Report(0, 0, 0, 0)), Tm);
        Assert.Null(l.WarningBox);
        Assert.Empty(l.WarningLines);
    }

    [Fact(DisplayName = "Layout: o tempo do parágrafo é marcado para realce, cobrindo quebra de linha")]
    public void IntroHighlight()
    {
        var m = Model();
        var l = OfflineCardLayout.Compute(m, Tm);
        var joined = string.Join(" ", l.IntroLines.Select(x => x.Text));
        Assert.Equal(m.Intro, joined);
        var marked = string.Concat(l.IntroLines.Where(x => x.HighlightStart >= 0).Select(x => x.Text.Substring(x.HighlightStart, x.HighlightLen)));
        Assert.Equal("3h" + "12min", marked.Replace(" ", ""));
    }

    [Fact(DisplayName = "CA-24.11 hit-test: botões, fundo, fora; botões desabilitados durante a pergunta")]
    public void HitTest()
    {
        var l = OfflineCardLayout.Compute(Model(), Tm);
        Assert.Equal(OfflineCardHitKind.CollectX2, l.Hit(l.CollectX2.X + 5, l.CollectX2.Y + 5));
        Assert.Equal(OfflineCardHitKind.Collect, l.Hit(l.Collect.X + 5, l.Collect.Y + 5));
        Assert.Equal(OfflineCardHitKind.Background, l.Hit(l.Tiles[0].Box.X + 1, l.Tiles[0].Box.Y + 1));
        Assert.Equal(OfflineCardHitKind.Background, l.Hit(l.Collect.X, l.Collect.Y + l.Collect.H));   // borda inferior exclusiva
        Assert.Equal(OfflineCardHitKind.None, l.Hit(-1, 10));
        Assert.Equal(OfflineCardHitKind.None, l.Hit(472, 10));
        Assert.Equal(OfflineCardHitKind.Background, l.Hit(l.Collect.X + 5, l.Collect.Y + 5, buttonsEnabled: false));
    }
}
