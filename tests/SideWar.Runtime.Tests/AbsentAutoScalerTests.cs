using SideWar.Core.Content;
using SideWar.Core.Simulation;
using SideWar.Runtime.Power;
using SideWar.Runtime.Presence;
using SideWar.Runtime.Timing;
using Xunit;

namespace SideWar.Runtime.Tests;

public sealed class AbsentAutoScalerTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);

    private static SimState NewState(long credits, params TowerType[] towers)
    {
        var layout = TestLayouts.FromCenters(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 380f });
        var s = SimState.CreateInitial(Content, layout, Rng.Seed(42, 54));
        s.Credits = 1_000_000;
        for (int i = 0; i < towers.Length; i++) Assert.True(s.Apply(Ctx, new BuildTower(towers[i], TestLayouts.X(i))).IsOk);
        s.Credits = credits;
        return s;
    }

    [Fact(DisplayName = "CA-25.3 Ausente: 300 s → 9000 ticks e render cai para 8 fps")]
    public void Absent_300s_Is_9000_Ticks_And_8fps()
    {
        Assert.Equal(9000, AbsentMode.SimTicksFor(300));
        var stepper = new FixedStepper();
        stepper.Reset(0, true);
        int ticks = 0;
        for (int i = 1; i <= 3000; i++) ticks += stepper.Advance(i * 0.1, true);
        Assert.Equal(9000, ticks);
        Assert.Equal(8, AbsentMode.RenderFps(PresenceState.Ausente));
        Assert.Equal(4, AbsentMode.RenderFps(PresenceState.Ausente, 30, 4));
        Assert.Equal(30, AbsentMode.RenderFps(PresenceState.Presente));
        var d = RenderPolicy.Evaluate(new RenderInputs(true, true, true, PresenceState.Ausente, false, PowerProfile.Normal, 10.125, 10.0));
        Assert.True(d.RenderNow);
        Assert.Equal(10.125, d.NextDeadline!.Value, 9);
    }

    [Fact(DisplayName = "CA-25.3 Ausente no chefe repete a onda w−1; Presente joga a onda seguinte")]
    public void Boss_Wave_Loops_Previous()
    {
        Assert.Equal(19, AbsentMode.WaveToPlay(PresenceState.Ausente, 20, true));
        Assert.Equal(20, AbsentMode.WaveToPlay(PresenceState.Presente, 20, true));
        Assert.Equal(7, AbsentMode.WaveToPlay(PresenceState.Ausente, 7, false));
        Assert.False(AbsentMode.LiveEventsAllowed(PresenceState.Ausente));
        Assert.True(AbsentMode.LiveEventsAllowed(PresenceState.Presente));
        Assert.True(AbsentMode.EcoQosOn(PresenceState.Ausente));
        Assert.False(AbsentMode.EcoQosOn(PresenceState.Presente));
    }

    [Fact(DisplayName = "CA-25.4 gasta no máximo 50% do saldo, na torre mais barata, recalculando o custo")]
    public void Spends_Half_On_Cheapest_Recalculating()
    {
        var s = NewState(1000, TowerType.Bolt);
        long start = s.Credits;
        var r = AutoScaler.Run(s, Ctx, true, PresenceState.Ausente);
        Assert.NotEmpty(r.Upgrades);
        Assert.True(r.Spent <= start / 2);
        Assert.Equal(start - r.Spent, s.Credits);
        Assert.Equal(1 + r.Upgrades.Count, s.Towers.Get(TestLayouts.TowerAt(s, 0)).Level);
        // custo recalculado: cada melhoria custa mais que a anterior
        for (int i = 1; i < r.Upgrades.Count; i++) Assert.True(r.Upgrades[i].Cost > r.Upgrades[i - 1].Cost);
        // para quando a próxima não cabe
        var next = r.Upgrades[^1];
        TowerCosts.TryUpgradeCost(Ctx.Tower(next.Type).Cost, next.ToLevel, Content.Economy.UpgradeCostFactor, Content.Economy.UpgradeCostGrowth, out long nc);
        Assert.True(nc > start / 2 - r.Spent);
        Assert.Equal(0, s.ManualUpgrades);
    }

    [Fact(DisplayName = "CA-25.4 escolhe a torre de menor custo de melhoria; empate pelo menor Id")]
    public void Picks_Cheapest_Then_Lowest_Id()
    {
        var s = NewState(200, TowerType.Bolt, TowerType.Bolt);
        s.Credits = 200; // orçamento 100: uma melhoria de 54, depois 54 na outra (Id menor primeiro) não cabe (108)
        var r = AutoScaler.Run(s, Ctx, true, PresenceState.Offline);
        Assert.Single(r.Upgrades);
        int firstId = s.Towers.Get(TestLayouts.TowerAt(s, 0)).Id;
        Assert.Equal(firstId, r.Upgrades[0].TowerId);
        Assert.Equal((2, 1), (s.Towers.Get(TestLayouts.TowerAt(s, 0)).Level, s.Towers.Get(TestLayouts.TowerAt(s, 1)).Level));

        var s2 = NewState(0, TowerType.Bolt, TowerType.Frost);
        long upBolt = Cost(TowerType.Bolt), upFrost = Cost(TowerType.Frost);
        s2.Credits = Math.Min(upBolt, upFrost) * 2 + 2;
        var r2 = AutoScaler.Run(s2, Ctx, true, PresenceState.Ausente);
        TowerType cheaper = upBolt <= upFrost ? TowerType.Bolt : TowerType.Frost;
        Assert.Equal(cheaper, r2.Upgrades[0].Type);
    }

    private static long Cost(TowerType t)
    {
        TowerCosts.TryUpgradeCost(Ctx.Tower(t).Cost, 1, Content.Economy.UpgradeCostFactor, Content.Economy.UpgradeCostGrowth, out long c);
        return c;
    }

    [Theory(DisplayName = "CA-25.4 só em Ausente e Offline; respeita o toggle; sem torres não gasta")]
    [InlineData(PresenceState.Presente, true, 1, false)]
    [InlineData(PresenceState.Ausente, true, 1, true)]
    [InlineData(PresenceState.Offline, true, 1, true)]
    [InlineData(PresenceState.Ausente, false, 1, false)]
    [InlineData(PresenceState.Ausente, true, 0, false)]
    public void Gating(PresenceState p, bool on, int towers, bool spends)
    {
        var s = NewState(10_000, towers == 0 ? Array.Empty<TowerType>() : new[] { TowerType.Bolt });
        var r = AutoScaler.Run(s, Ctx, on, p);
        Assert.Equal(spends, r.Spent > 0);
        if (!spends) Assert.Equal(10_000, s.Credits);
    }

    [Theory(DisplayName = "CA-25.8 saldo nunca negativo; saldos 0, 1, 2 e pequenos")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(107)]
    [InlineData(108)]
    [InlineData(109)]
    [InlineData(50_000_000)]
    public void Balance_Never_Negative(long credits)
    {
        var s = NewState(credits, TowerType.Bolt, TowerType.Frost);
        var r = AutoScaler.Run(s, Ctx, true, PresenceState.Ausente);
        Assert.True(s.Credits >= 0);
        Assert.True(r.Spent <= AutoScaler.Budget(credits));
        Assert.Equal(credits - r.Spent, s.Credits);
    }

    [Fact(DisplayName = "CA-25.4 nível máximo: não melhora além de MaxTowerLevel")]
    public void Stops_At_Max_Level()
    {
        var s = NewState(long.MaxValue / 4, TowerType.Bolt);
        AutoScaler.Run(s, Ctx, true, PresenceState.Ausente);
        Assert.True(s.Towers.Get(TestLayouts.TowerAt(s, 0)).Level <= Content.Economy.MaxTowerLevel);
    }

    [Fact(DisplayName = "RF-25.5 acumulador: 15 min por rodada, acumula em Ausente/Offline e zera em Presente")]
    public void Accumulator()
    {
        var a = new AutoScaleAccumulator();
        Assert.Equal(0, a.Advance(PresenceState.Ausente, 899));
        Assert.Equal(1, a.Advance(PresenceState.Offline, 1));
        Assert.Equal(0, a.AccumSeconds);
        Assert.Equal(0, a.Advance(PresenceState.Ausente, 500));
        Assert.Equal(500, a.AccumSeconds);
        Assert.Equal(0, a.Advance(PresenceState.Presente, 60));
        Assert.Equal(0, a.AccumSeconds);
        Assert.Equal(2, a.Advance(PresenceState.Offline, 1800 + 10));
        Assert.Equal(10, a.AccumSeconds, 9);
        Assert.Equal(300, new AutoScaleAccumulator(300).AccumSeconds);
    }
}
