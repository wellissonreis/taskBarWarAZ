using SideWar.Runtime.Widget;
using Xunit;

namespace SideWar.Runtime.Tests.Widget;

public class WidgetInteractionTests
{
    private sealed class Sink : IPresenceSink
    {
        public int Count;
        public void NotifyInteraction() => Count++;
    }

    private readonly Sink _presence = new();
    private readonly List<PanelCommand> _commands = [];
    private readonly WidgetInteraction _i;

    public WidgetInteractionTests()
    {
        _i = new WidgetInteraction(_presence, _commands.Add) { Layout = WidgetLayout.Compute(48, 100, 20, 0) };
    }

    private (float X, float Y) Center(int b)
    {
        var r = _i.Layout.Button(b);
        return (r.X + r.W / 2, r.Y + r.H / 2);
    }

    [Fact]
    public void HitTest_ButtonsBackgroundAndCorners() // RF-23.8, CA-23.9
    {
        for (int b = 0; b < 4; b++)
        {
            var (x, y) = Center(b);
            Assert.Equal(WidgetHitTarget.Button, _i.Hit(x, y));
        }
        Assert.Equal(WidgetHitTarget.Background, _i.Hit(20, 24)); // créditos
        Assert.Equal(WidgetHitTarget.Background, _i.Hit(170, 24)); // selo
        // ADR-034 / CA-23.9: sem raio, os cantos também consomem o clique; só fora da caixa é None.
        Assert.Equal(WidgetHitTarget.Background, _i.Hit(0.5f, 0.5f));
        float w = _i.Layout.WidthDip;
        Assert.Equal(WidgetHitTarget.Background, _i.Hit(w - 0.5f, 47.5f));
        Assert.Equal(WidgetHitTarget.None, _i.Hit(w + 0.5f, 10));
        Assert.Equal(WidgetHitTarget.None, _i.Hit(-0.5f, 10));
        Assert.Equal(WidgetHitTarget.None, _i.Hit(10, 48.5f));
        Assert.Equal(WidgetHitTarget.None, _i.Hit(-1, 10));
        Assert.Equal(WidgetHitTarget.None, _i.Hit(w, 10));
    }

    [Fact]
    public void ClickOnSameButton_EmitsToggleTab_ForEachTab() // CA-23.5
    {
        for (int b = 0; b < 4; b++)
        {
            var (x, y) = Center(b);
            Assert.True(_i.OnLeftDown(x, y));
            Assert.True(_i.OnLeftUp(x, y));
            Assert.Equal(PanelCommand.ToggleTab((WidgetTab)b), _commands[^1]);
        }
        Assert.Equal(4, _commands.Count);
    }

    [Fact]
    public void ReleaseOnAnotherButtonOrOutside_EmitsNothing()
    {
        var (x0, y0) = Center(0);
        var (x1, y1) = Center(1);
        Assert.True(_i.OnLeftDown(x0, y0));
        _i.OnLeftUp(x1, y1);
        Assert.True(_i.OnLeftDown(x0, y0));
        _i.OnLeftUp(20, 24);
        Assert.Empty(_commands);
    }

    [Fact]
    public void LeftDownOnBackground_NoCapture_ButCountsInteraction() // RF-23.8
    {
        Assert.False(_i.OnLeftDown(20, 24));
        Assert.Equal(1, _presence.Count);
        Assert.False(_i.OnLeftUp(20, 24));
        Assert.Empty(_commands);
    }

    [Fact]
    public void Presence_OncePerPress_NotForMoveHoverReleaseOrCorners() // CA-23.8
    {
        _i.OnMouseMove(20, 24);
        _i.OnMouseMove(Center(2).X, Center(2).Y);
        _i.OnLeave();
        Assert.Equal(0, _presence.Count);
        var (x, y) = Center(3);
        _i.OnLeftDown(x, y);
        _i.OnLeftUp(x, y);
        Assert.Equal(1, _presence.Count);
        Assert.True(_i.OnRightDown(20, 24));
        Assert.Equal(2, _presence.Count);
        Assert.True(_i.OnRightDown(x, y)); // sobre botão: conta e abre menu, não aciona o nav
        Assert.Equal(3, _presence.Count);
        Assert.Single(_commands);
        Assert.False(_i.OnLeftDown(-0.5f, 0.5f)); // fora da caixa (ADR-034: o canto não é mais "fora")
        Assert.False(_i.OnRightDown(-0.5f, 0.5f));
        Assert.Equal(3, _presence.Count);
    }

    [Fact]
    public void Hover_ReportsOnlyChanges_AndLeaveClears() // RF-23.8
    {
        var (x, y) = Center(1);
        Assert.True(_i.OnMouseMove(x, y));
        Assert.Equal(1, _i.HoverIndex);
        Assert.False(_i.OnMouseMove(x + 1, y));
        Assert.True(_i.OnMouseMove(20, 24));
        Assert.Equal(-1, _i.HoverIndex);
        Assert.False(_i.OnMouseMove(21, 24));
        _i.OnMouseMove(x, y);
        Assert.True(_i.OnLeave());
        Assert.False(_i.OnLeave());
    }

    [Fact]
    public void CaptureLost_CancelsClick()
    {
        var (x, y) = Center(0);
        _i.OnLeftDown(x, y);
        _i.OnCaptureLost();
        Assert.False(_i.OnLeftUp(x, y));
        Assert.Empty(_commands);
    }

    [Fact]
    public void BonusChip_FollowsSeal_AndWidensTheWidget() // ADR-034
    {
        WidgetLayout none = WidgetLayout.Compute(48, 100, 20, 0);
        WidgetLayout l = WidgetLayout.Compute(48, 100, 20, 56);
        Assert.True(l.HasChip);
        Assert.False(none.HasChip);
        Assert.Equal(l.Seal.X + l.Seal.W + 12, l.Chip.X);
        Assert.Equal(none.WidthDip + 12 + 56, l.WidthDip);
        Assert.True(l.Chip.X + l.Chip.W + 12 <= l.Nav0.X + 0.001f);
    }
}
