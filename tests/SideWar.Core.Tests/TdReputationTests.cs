using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using StartWave = SideWar.Core.Td.StartWave;

namespace SideWar.Core.Tests;

/// <summary>REPUTACAO: na corrida persistente a base atacada tira XP (sem derrota) e vencer onda dá XP; o clássico mantém a integridade.</summary>
public sealed class TdReputationTests
{
    private static readonly SideWar.Core.Content.Td.TdContent Base = TdMechanicsContent.Load();

    private static TdMatchState Armed(TdMatchOptions o)
    {
        var s = TdMatchState.Create(Base, 1, Rng.Seed(7, 54), null, o);
        Assert.True(s.Apply(new LayoutChanged(TdSanityBot.TypicalLayout())).IsOk);
        Assert.True(s.Apply(new SetPresence(false)).IsOk);
        Assert.True(s.Apply(new SideWar.Core.Td.BuildTower("bolt", 900)).IsOk);
        Assert.True(s.Apply(new StartWave()).IsOk);
        Assert.True(s.Apply(new SellTower(s.Towers[0].Id)).IsOk);
        return s;
    }

    private static List<TdEvent> StepUntil(TdMatchState s, TdEventType type, int max = 200_000)
    {
        for (int i = 0; i < max; i++)
        {
            s.ClearEvents();
            s.Step();
            if (s.Events.Any(e => e.Type == type)) return [.. s.Events];
        }
        throw new Xunit.Sdk.XunitException("evento " + type + " não ocorreu");
    }

    [Fact]
    public void Leak_in_a_run_costs_xp_and_never_ends_the_match()
    {
        var s = Armed(TdMatchOptions.Default);
        var ev = StepUntil(s, TdEventType.BaseAttacked);
        var leak = ev.Single(e => e.Type == TdEventType.BaseAttacked);
        Assert.Equal(leak.B * TdReputation.XpPerCoreDamage, leak.A);
        Assert.Equal(leak.A, s.ReputationLostTotal);
        for (int i = 0; i < 20_000; i++) s.Step(); // sem torres tudo vaza: nunca há derrota
        Assert.NotEqual(MatchPhase.Vitoria, s.Phase); // ainda sem fim: a corrida segue
        Assert.True(s.ReputationLostTotal > leak.A);
    }

    [Fact]
    public void Winning_a_wave_pays_xp_and_a_leaked_wave_pays_half()
    {
        var s = Armed(TdMatchOptions.Default);
        var ev = StepUntil(s, TdEventType.ReputationGained);
        var gain = ev.Single(e => e.Type == TdEventType.ReputationGained);
        Assert.Equal(1, gain.B);
        Assert.Equal(TdReputation.WaveXp(1, 1, true), gain.A); // sem torres a onda 1 vazou
        Assert.Equal(TdReputation.WaveXp(1, 1, false) / 2, gain.A);
        Assert.True(TdReputation.WaveXp(2, 5, false) > TdReputation.WaveXp(1, 5, false));
        Assert.True(TdReputation.WaveXp(1, 6, false) > TdReputation.WaveXp(1, 5, false));
    }

    [Fact]
    public void Classic_leaks_also_cost_xp_and_never_end_the_match()
    {
        var s = Armed(TdMatchOptions.Classic);
        StepUntil(s, TdEventType.EnemyLeaked);
        Assert.True(s.ReputationLostTotal > 0);
        Assert.NotEqual(MatchPhase.Vitoria, s.Phase);
    }

    [Theory]
    [InlineData(1, 0, 25)]
    [InlineData(2, 0, 50)]
    [InlineData(4, 50, 50)]
    [InlineData(1, 999, 6)] // escudo limitado a 75%: 25 * 25 / 100 = 6
    public void XpLost_applies_the_shield_with_a_cap(int damage, int shield, int expected) =>
        Assert.Equal(expected, TdReputation.XpLost(damage, shield));
}
