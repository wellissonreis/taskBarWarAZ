using SideWar.Runtime.Presence;
using SideWar.Runtime.Widget;
using Xunit;

namespace SideWar.Runtime.Tests.Widget;

public class WidgetModelTests
{
    private static WidgetInputs Inputs(double credits = 48200, double rate = 312, PresenceState p = PresenceState.Presente,
        double mult = 2.0, int bonus = 0, double pending = 0, int badge = 2, WidgetTab? tab = null, int hover = -1) =>
        new(credits, rate, 37, 60, p, mult, bonus, pending, badge, tab, hover);

    [Fact]
    public void Texts_FollowDesignWithPtBrFormat()
    {
        var b = new WidgetModelBuilder();
        WidgetModel m = b.Update(Inputs(), TimeSpan.Zero);
        Assert.Equal("48,2K", m.CreditsText);
        Assert.Equal("+312/s", m.RateText);
        Assert.Equal("37/60", m.WaveText);
        Assert.Equal("PRESENTE ×2,0", m.SealText);
        Assert.Equal("PRESENTE ×2,0", m.PresenceAccessibleName);
    }

    [Theory]
    [InlineData(2.0, "×2,0")]
    [InlineData(1.5, "×1,5")]
    [InlineData(2.5, "×2,5")]
    [InlineData(0.25, "×0,25")]
    public void Multiplier_UsesComma(double m, string expected) => Assert.Equal(expected, WidgetModelBuilder.FormatMultiplier(m));

    [Fact]
    public void Absent_ShowsOnlyMultiplier_FullLabelInAccessibleName()
    {
        WidgetModel m = new WidgetModelBuilder().Update(Inputs(p: PresenceState.Ausente, mult: 0.25, rate: 39), TimeSpan.Zero);
        Assert.Equal("AUSENTE ×0,25", m.SealText);
        Assert.Equal("AUSENTE ×0,25", m.PresenceAccessibleName);
        Assert.Equal("+39/s", m.RateText);
        Assert.False(m.IsPresent);
    }

    [Fact]
    public void Version_RisesOnlyWhenDisplayedTextChanges()
    {
        var b = new WidgetModelBuilder();
        long v = b.Update(Inputs(credits: 48200), TimeSpan.Zero).Version;
        Assert.Equal(v, b.Update(Inputs(credits: 48200.4), TimeSpan.FromSeconds(1)).Version);
        Assert.Equal(v + 1, b.Update(Inputs(credits: 49000), TimeSpan.FromSeconds(2)).Version);
    }

    [Fact]
    public void CreditsAndRate_AreCoalescedAtTwoHz()
    {
        var b = new WidgetModelBuilder();
        b.Update(Inputs(credits: 1000), TimeSpan.Zero);
        WidgetModel early = b.Update(Inputs(credits: 5000), TimeSpan.FromMilliseconds(499));
        Assert.Equal("1K", early.CreditsText);
        WidgetModel late = b.Update(Inputs(credits: 5000), TimeSpan.FromMilliseconds(500));
        Assert.Equal("5K", late.CreditsText);
    }

    [Fact]
    public void NonNumericFields_ApplyImmediately()
    {
        var b = new WidgetModelBuilder();
        long v = b.Update(Inputs(), TimeSpan.Zero).Version;
        WidgetModel m = b.Update(Inputs(hover: 2, tab: WidgetTab.Fases), TimeSpan.FromMilliseconds(10));
        Assert.Equal(v + 1, m.Version);
        Assert.Equal(2, m.HoverIndex);
        Assert.Equal(WidgetTab.Fases, m.ActiveTab);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(2, "2")]
    [InlineData(9, "9")]
    [InlineData(10, "9+")]
    [InlineData(250, "9+")]
    public void Badge_HiddenAtZero_NinePlusAbove(int count, string text) =>
        Assert.Equal(text, new WidgetModelBuilder().Update(Inputs(badge: count), TimeSpan.Zero).BadgeText);

    [Fact]
    public void PendingAndBonus_AppearAndDisappear()
    {
        var b = new WidgetModelBuilder();
        WidgetModel m = b.Update(Inputs(pending: 1500, bonus: 59), TimeSpan.Zero);
        Assert.Equal("+1,5K", m.PendingText);
        Assert.Equal("×3 0:59", m.BonusChipText);
        m = b.Update(Inputs(pending: 0, bonus: 0), TimeSpan.FromSeconds(1));
        Assert.Null(m.PendingText);
        Assert.Null(m.BonusChipText);
    }

    [Fact]
    public void Layout_FixedWidthIs348_NaturalWidth_AndButtonsTileNav()
    {
        Assert.Equal(348f, WidgetLayout.FixedWidth);
        WidgetLayout l = WidgetLayout.Compute(48, 100, 20, 0);
        Assert.Equal(348f + 12 + 100, l.WidthDip); // sem espaço flexível
        Assert.Equal(13f, l.Credits.X); // borda 1 + padding 12
        Assert.Equal(70f, l.Credits.W);
        Assert.Equal(95f, l.Wave.X); // 13 + 70 + 12
        Assert.Equal(52f, l.Wave.W);
        Assert.Equal(159f, l.Seal.X);
        Assert.Equal(l.WidthDip - 1 - 6 - 182, l.Nav0.X);
        Assert.Equal(44f, l.Nav0.W);
        Assert.Equal(l.Nav0.X + 46, l.Nav1.X);
        Assert.Equal(l.WidthDip - 7, l.Nav0.X + 3 * 46 + 44); // padding-right 6 + borda 1
        Assert.Equal(2f, l.Nav0.Y);
        Assert.Equal(2, l.HitButton(l.Nav2.X + 1, 10));
        Assert.Equal(-1, l.HitButton(10, 10));
    }
}
