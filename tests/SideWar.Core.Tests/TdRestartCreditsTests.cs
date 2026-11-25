using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;

namespace SideWar.Core.Tests;

/// <summary>Reiniciar a fase nunca tira dinheiro (max(atuais, iniciais)) e nunca concede créditos extras.</summary>
public sealed class TdRestartCreditsTests
{
    private static readonly SideWar.Core.Content.Td.TdContent Content = TdMechanicsContent.Load();

    private static TdMatchState New() => TdMatchState.Create(Content, 1, Rng.Seed(7, 54));

    [Fact(DisplayName = "Restart: carrying more credits than the start keeps them (no loss)")]
    public void Carry_keeps_higher_credits()
    {
        var old = New();
        long initial = old.Credits;
        var next = New();
        next.CarryCredits(initial + 500);
        Assert.Equal(initial + 500, next.Credits);
    }

    [Fact(DisplayName = "Restart: carrying fewer credits never lowers or tops up (no farm, no extra)")]
    public void Carry_lower_credits_keeps_stage_start()
    {
        var next = New();
        long initial = next.Credits;
        next.CarryCredits(initial - 1);
        Assert.Equal(initial, next.Credits);
        next.CarryCredits(initial);
        Assert.Equal(initial, next.Credits);
    }

    [Fact(DisplayName = "Economy: reopening/restarting with towers does not grant the starting credits again")]
    public void Imported_towers_are_paid_by_the_starting_credits()
    {
        var empty = New();
        long initial = empty.Credits;
        empty.ImportTowers([]);
        Assert.Equal(initial, empty.Credits);

        var partial = New();
        partial.ImportTowers([new("bolt", 900, true, initial - 100, 0, null, 0)]);
        Assert.Equal(100, partial.Credits);

        var spent = New();
        spent.ImportTowers([new("bolt", 900, true, initial, 0, "cannon", 500)]);
        Assert.Equal(0, spent.Credits);
    }

    [Theory(DisplayName = "Economy: restart keeps the saved money with towers, in either call order, never adding to it")]
    [InlineData(true)]
    [InlineData(false)]
    public void Carried_money_survives_the_tower_discount(bool carryFirst)
    {
        var next = New();
        TdTowerCarry[] towers = [new("bolt", 900, true, 10_000, 0, null, 0)];
        if (carryFirst) next.CarryCredits(50);
        next.ImportTowers(towers);
        if (!carryFirst) next.CarryCredits(50);
        Assert.Equal(50, next.Credits);
    }
}
