using SideWar.Runtime.Input;
using Xunit;

namespace SideWar.Runtime.Tests.Input;

/// <summary>Estado do "pegar torre": a faixa só deixa de ser click-through sobre torre colocada ou com o botão capturado (sem mouse real).</summary>
public sealed class TowerGrabGateTests
{
    [Fact]
    public void Starts_cold_and_stays_click_through()
    {
        var g = new TowerGrabGate();
        Assert.False(g.Hot);
        Assert.True(g.WantsTransparent(true));
        Assert.False(g.ReceivesMouse(false));
    }

    [Fact]
    public void Over_a_tower_makes_the_strip_receive_the_button_and_leaving_restores_click_through()
    {
        var g = new TowerGrabGate();
        Assert.True(g.SetOver(true));
        Assert.True(g.Hot);
        Assert.False(g.WantsTransparent(true));
        Assert.True(g.ReceivesMouse(false));
        Assert.False(g.SetOver(true));   // sem mudança, sem trabalho
        Assert.True(g.SetOver(false));
        Assert.True(g.WantsTransparent(true));
    }

    [Fact]
    public void Interactive_mode_without_a_tower_is_not_made_transparent_by_the_gate()
    {
        var g = new TowerGrabGate();
        Assert.False(g.WantsTransparent(false)); // modo Interativo continua como antes
        Assert.True(g.ReceivesMouse(true));
    }

    [Fact]
    public void Press_only_captures_when_a_tower_was_under_the_cursor()
    {
        var g = new TowerGrabGate();
        Assert.False(g.BeginCapture());
        g.SetOver(true);
        Assert.True(g.BeginCapture());
        Assert.False(g.BeginCapture()); // já capturado
        Assert.True(g.Captured);
    }

    [Fact]
    public void While_captured_the_strip_stays_hot_even_if_the_cursor_leaves_the_tower()
    {
        var g = new TowerGrabGate();
        g.SetOver(true);
        g.BeginCapture();
        Assert.False(g.SetOver(false));
        Assert.True(g.Hot);
        Assert.False(g.WantsTransparent(true));
    }

    [Fact]
    public void Release_over_empty_space_goes_back_to_click_through_and_over_a_tower_stays_hot()
    {
        var g = new TowerGrabGate();
        g.SetOver(true);
        g.BeginCapture();
        Assert.True(g.EndCapture(false));
        Assert.False(g.Captured);
        Assert.True(g.WantsTransparent(true));

        g.SetOver(true);
        g.BeginCapture();
        Assert.False(g.EndCapture(true));
        Assert.True(g.Hot);
    }

    [Fact]
    public void Reset_clears_everything_when_the_scene_hides()
    {
        var g = new TowerGrabGate();
        g.SetOver(true);
        g.BeginCapture();
        g.Reset();
        Assert.False(g.Hot);
        Assert.False(g.Captured);
    }
}
