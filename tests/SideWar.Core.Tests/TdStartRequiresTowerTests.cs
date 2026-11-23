using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using StartWave = SideWar.Core.Td.StartWave;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Core.Tests;

public sealed class TdStartRequiresTowerTests
{
    [Fact]
    public void Continuous_waves_continue_after_selling_the_last_tower()
    {
        var m = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54), null, TdMatchOptions.Default);
        m.Apply(new LayoutChanged(TdSanityBot.TypicalLayout()));
        Assert.True(m.Apply(new BuildTower("bolt", 900)).IsOk);
        Assert.True(m.Apply(new StartWave()).IsOk);
        Assert.True(m.Apply(new SellTower(m.Towers[0].Id)).IsOk);
        Assert.Equal(CommandReason.NotAvailable, m.Apply(new CallNextWave()).Reason);
        for (int i = 0; i < 30000 && m.WaveNumber == 1; i++) m.Step();
        Assert.True(m.WaveNumber > 1);
    }

    [Fact]
    public void Manual_and_automatic_start_wait_for_a_placed_tower()
    {
        var m = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54));
        m.Apply(new LayoutChanged(TdSanityBot.TypicalLayout()));
        Assert.Equal(CommandReason.NotAvailable, m.Apply(new StartWave()).Reason);
        m.Apply(new SetAutoStart(AutoStartMode.Countdown, 5));
        for (int i = 0; i < 300; i++) m.Step();
        Assert.Equal(MatchPhase.Preparo, m.Phase);
        Assert.Equal(5, m.CountdownRemainingSeconds);
        Assert.True(m.Apply(new BuildTower("bolt", 900)).IsOk);
        for (int i = 0; i < 150; i++) m.Step();
        Assert.Equal(MatchPhase.Onda, m.Phase);
    }
}
