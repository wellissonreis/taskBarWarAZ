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
}
