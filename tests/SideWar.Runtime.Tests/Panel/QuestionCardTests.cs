using SideWar.Core.Events;
using SideWar.Runtime.Panel;
using Xunit;

namespace SideWar.Runtime.Tests.Panel;

public class QuestionCardTests
{
    private static readonly FakeTextMetrics Tm = new();

    private sealed class Rng : IQuestionRng
    {
        public int NextInt(int exclusiveMax) => 0;
    }

    private static BossEncounter Start()
    {
        var q = new QuestionDef("q1", "iaas", "mandatory", "Quem aplica os patches do SO numa VM?",
        [
            new("A Microsoft, sempre", false, "Não: no IaaS o cliente cuida do SO."),
            new("Você (cliente)", true, "Correto: no IaaS o cliente aplica os patches."),
            new("Ninguém", false, "Não é automático."),
            new("O provedor de rede", false, "Não tem relação."),
        ]);
        var bank = new QuestionBank(false, [q]);
        return BossEncounter.Begin(bank, new QuestionSelector(bank), new QuestionLedger(), new LedgerKey(1, 1, 40), "iaas", new Rng())!;
    }

    [Fact(DisplayName = "CA-24.10 waiting: 4 options enabled, timer 0:45, header, read-only on encounter")]
    public void Waiting()
    {
        var e = Start();
        var m = QuestionCardModel.From(e, 40, "iaas");
        Assert.Equal("CHEFE · ONDA 40 · IAAS", m.Header);
        Assert.Equal("0:45", m.TimerText);
        Assert.Equal("A · A Microsoft, sempre", m.OptionTexts[0]);
        Assert.Equal(4, m.OptionTexts.Count);
        Assert.True(m.OptionsEnabled);
        Assert.All(m.Visuals, v => Assert.Equal(OptionVisual.Normal, v));
        Assert.Null(m.ResultText);
        Assert.True(e.IsActive);
    }

    [Fact(DisplayName = "CA-24.10 correct / wrong / timeout states and explanations")]
    public void ResolvedStates()
    {
        var e = Start();
        e.Answer(1);
        var ok = QuestionCardModel.From(e, 40, "iaas");
        Assert.Equal(QuestionCardState.Correct, ok.State);
        Assert.Equal("ACERTOU", ok.ResultText);
        Assert.Equal(OptionVisual.Correct, ok.Visuals[1]);
        Assert.False(ok.OptionsEnabled);

        var w = Start();
        w.Answer(0);
        var wrong = QuestionCardModel.From(w, 40, "iaas");
        Assert.Equal("ERROU", wrong.ResultText);
        Assert.Equal(OptionVisual.Wrong, wrong.Visuals[0]);
        Assert.Equal(OptionVisual.Correct, wrong.Visuals[1]);
        Assert.Contains("Não: no IaaS", wrong.Explanation);
        Assert.Contains("Correto:", wrong.Explanation);

        var t = Start();
        t.Advance(60);
        var to = QuestionCardModel.From(t, 40, "iaas");
        Assert.Equal("TEMPO ESGOTADO", to.ResultText);
        Assert.Equal("0:00", to.TimerText);
        Assert.Equal(OptionVisual.Correct, to.Visuals[1]);
        Assert.Equal(OptionVisual.Normal, to.Visuals[0]);
    }

    [Fact(DisplayName = "Timer format uses ceiling seconds")]
    public void TimerFormat()
    {
        Assert.Equal("0:45", QuestionCardModel.FormatTimer(45));
        Assert.Equal("0:44", QuestionCardModel.FormatTimer(43.2));
        Assert.Equal("0:00", QuestionCardModel.FormatTimer(0));
        Assert.Equal("1:05", QuestionCardModel.FormatTimer(65));
    }

    [Fact(DisplayName = "Layout: base 242 DIP, 2x2 options min 44, minimize 44x44 inside the card")]
    public void LayoutWaiting()
    {
        var m = QuestionCardModel.TestData();
        var l = QuestionCardLayout.Compute(m, Tm);
        Assert.Equal(242f, l.Height);
        Assert.Equal(4, l.Options.Length);
        Assert.All(l.Options, o => Assert.True(o.H >= 44f));
        Assert.Equal(l.Options[0].Y, l.Options[1].Y);
        Assert.Equal(8f, l.Options[2].Y - (l.Options[0].Y + l.Options[0].H), 3);
        Assert.Equal(new DipRectEq(518 - 44, 2, 44, 44), new DipRectEq(l.ActionButton));
        Assert.False(l.ShowsExplanation);
        Assert.Equal(new QuestionHit(QuestionHitKind.Minimize), l.Hit(480, 10, m));
        Assert.Equal(new QuestionHit(QuestionHitKind.Option, 1), l.Hit(l.Options[1].X + 3, l.Options[1].Y + 3, m));
        Assert.Equal(new QuestionHit(QuestionHitKind.Background), l.Hit(5, 5, m));
        Assert.Equal(QuestionHitKind.None, l.Hit(-1, 5, m).Kind);
        Assert.Equal(QuestionHitKind.None, l.Hit(100, 300, m).Kind);
    }

    [Fact(DisplayName = "Layout: explanation grows the card upward (≤ 3 lines), options disabled, close instead of minimize")]
    public void LayoutAnswered()
    {
        var m = QuestionCardModel.TestData(QuestionCardState.Wrong, chosen: 0);
        m = m with { Explanation = string.Join(' ', Enumerable.Repeat("explicacao longa demais para caber", 12)) };
        var l = QuestionCardLayout.Compute(m, Tm);
        Assert.True(l.ShowsExplanation);
        Assert.Equal(QuestionCardLayout.MaxExplanationLines, l.FooterLines.Length);
        Assert.EndsWith("…", l.FooterLines[^1]);
        var design = QuestionCardLayout.Compute(QuestionCardModel.TestData(), Tm);
        Assert.Equal(242f - design.FooterBox.H + l.FooterBox.H, l.Height, 3);
        Assert.Equal(QuestionHitKind.Close, l.Hit(480, 10, m).Kind);
        Assert.Equal(QuestionHitKind.Background, l.Hit(l.Options[1].X + 3, l.Options[1].Y + 3, m).Kind);
    }

    private readonly record struct DipRectEq(float X, float Y, float W, float H)
    {
        public DipRectEq(SideWar.Runtime.Geometry.DipRect r) : this(r.X, r.Y, r.W, r.H) { }
    }
}
