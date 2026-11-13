using SideWar.Runtime.Widget;
using Xunit;

namespace SideWar.Runtime.Tests.Widget;

/// <summary>T6.4b: badge "+X" clicável do widget (SDD-23 RF-23.7).</summary>
public class WidgetBadgeTests
{
    private sealed class Sink : IPresenceSink
    {
        public int Count;
        public void NotifyInteraction() => Count++;
    }

    private readonly Sink _presence = new();
    private readonly List<PanelCommand> _commands = [];
    private int _reopened;
    private readonly WidgetInteraction _i;

    public WidgetBadgeTests()
    {
        _i = new WidgetInteraction(_presence, _commands.Add) { Layout = WidgetLayout.Compute(48, 41, 20, 0), OnBadgeClick = () => _reopened++ };
    }

    private (float X, float Y) Badge()
    {
        var r = WidgetBadgeGeometry.Rect(_i.Layout.Credits, _i.Layout.HeightDip);
        return (r.X + 4, r.Y + r.H / 2);
    }

    [Fact(DisplayName = "Badge visível: clique reabre o card e conta como interação; não emite comando do nav")]
    public void Click_ReopensCard()
    {
        _i.BadgeVisible = true;
        var (x, y) = Badge();
        Assert.Equal(WidgetHitTarget.Badge, _i.Hit(x, y));
        Assert.True(_i.OnLeftDown(x, y));
        Assert.Equal(1, _presence.Count);
        Assert.True(_i.OnLeftUp(x, y));
        Assert.Equal(1, _reopened);
        Assert.Empty(_commands);
    }

    [Fact(DisplayName = "Badge oculto: a mesma área é fundo; soltar fora cancela; captura perdida cancela")]
    public void Hidden_Or_Cancelled()
    {
        var (x, y) = Badge();
        Assert.Equal(WidgetHitTarget.Background, _i.Hit(x, y));
        Assert.False(_i.OnLeftDown(x, y));
        _i.OnLeftUp(x, y);
        Assert.Equal(0, _reopened);

        _i.BadgeVisible = true;
        _i.OnLeftDown(x, y);
        _i.OnLeftUp(300, 24);
        Assert.Equal(0, _reopened);
        _i.OnLeftDown(x, y);
        _i.OnCaptureLost();
        _i.OnLeftUp(x, y);
        Assert.Equal(0, _reopened);
    }

    [Fact(DisplayName = "Fora da pílula (acima, nos créditos) segue como fundo mesmo com badge")]
    public void OutsideBadgeRow_IsBackground()
    {
        _i.BadgeVisible = true;
        Assert.Equal(WidgetHitTarget.Background, _i.Hit(20, 8));
    }
}
