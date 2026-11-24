using System.Reflection;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Core.Tests;

public sealed class TdShadowModeTests
{
    private static readonly TdContent Content = TdContentLoader.LoadEmbedded();

    private static TdMatchState Create(bool shadow = false, TdMatchOptions? options = null)
    {
        var m = TdMatchState.Create(Content, 1, Rng.Seed(7, 54), null, (options ?? TdMatchOptions.Default) with { InitialReputation = 6000, InitialShadow = shadow });
        m.Apply(new LayoutChanged(TdSanityBot.TypicalLayout()));
        m.Apply(new SetPresence(true));
        return m;
    }

    private static object? Call(TdMatchState m, string name, params object[] args) =>
        typeof(TdMatchState).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(m, args);

    private static void BuildAndStart(TdMatchState m)
    {
        typeof(TdMatchState).GetProperty("Credits")!.SetValue(m, 5000L);
        m.Apply(new BuildTower("bolt", 150));
        m.Apply(new StartWave());
    }

    [Fact]
    public void Reward_fraction_constant_is_35_percent_and_scales_down()
    {
        Assert.Equal(35, TdShadow.RewardPercent);
        Assert.Equal(35, TdShadow.Scale(100));
        Assert.Equal(0, TdShadow.Scale(2));
        Assert.Equal(0, TdShadow.Scale(-5));
    }

    [Fact]
    public void Xp_is_frozen_when_leaking_and_when_winning_in_shadow_but_moves_normally_outside()
    {
        var normal = Create();
        int before = normal.GateReputation;
        Call(normal, "LoseReputation", 10);
        Assert.True(normal.GateReputation < before);
        Call(normal, "WinWaveReputation", 3, false);
        Assert.True(normal.ReputationGainedTotal > 0);

        var shadow = Create(shadow: true);
        before = shadow.GateReputation;
        Call(shadow, "LoseReputation", 10);
        Call(shadow, "WinWaveReputation", 3, false);
        Call(shadow, "WinWaveReputation", 4, true);
        Assert.Equal(before, shadow.GateReputation);
        Assert.Equal(0, shadow.ReputationLostTotal);
        Assert.Equal(0, shadow.ReputationGainedTotal);
        Assert.Equal(0, shadow.Streak); // sem multiplicador de sequência
        Assert.DoesNotContain(shadow.Events, e => e.Type is TdEventType.BaseAttacked or TdEventType.ReputationGained);
        Assert.Equal(2, shadow.ShadowWaves);
        Assert.Equal(0, (int)Call(shadow, "RecoverReputation", 5)!);
    }

    [Fact]
    public void Run_in_shadow_keeps_xp_and_still_pays_credits_with_a_fraction_of_the_fragments()
    {
        var normal = Create();
        var shadow = Create(shadow: true);
        foreach (var m in new[] { normal, shadow }) BuildAndStart(m);
        for (int i = 0; i < 30 * 600 && (normal.WaveNumber < 3 || shadow.WaveNumber < 3); i++) { normal.Step(); shadow.Step(); }
        Assert.Equal(6000, shadow.GateReputation);
        Assert.Equal(0, shadow.ReputationGainedTotal);
        Assert.True(normal.ReputationGainedTotal > 0);
        Assert.True(shadow.Credits > 0);
        Assert.InRange(shadow.Fragments, 0, normal.Fragments); // fração, nunca mais que o normal
        Assert.True(shadow.Fragments <= TdShadow.Scale(normal.Fragments) + 1);
    }

    [Fact]
    public void Fragments_are_35_percent_with_a_carried_remainder()
    {
        var m = Create(shadow: true);
        for (int i = 0; i < 20; i++) Call(m, "GainFragments", 10);
        Assert.Equal(70, m.Fragments); // 200 × 35% (o resto fracionário acumula)
        Assert.Equal(70, m.ShadowFragments);
        var n = Create();
        for (int i = 0; i < 20; i++) Call(n, "GainFragments", 10);
        Assert.Equal(200, n.Fragments);
    }

    [Fact]
    public void Entering_and_leaving_shadow_only_take_effect_between_waves_and_reset_the_streak()
    {
        var m = Create();
        Assert.Equal(CommandResult.Ok, m.Apply(new SetShadow(true)));
        Assert.True(m.Shadow); // Preparo: na hora
        Assert.Contains(m.Events, e => e.Type == TdEventType.ShadowChanged && e.A == 1);
        BuildAndStart(m);
        Assert.Equal(MatchPhase.Onda, m.Phase);
        m.Apply(new SetShadow(false)); // no meio da onda: pendente
        Assert.True(m.Shadow);
        Assert.Equal(false, m.ShadowPending);
        int wave = m.WaveNumber;
        for (int i = 0; i < 30 * 900 && m.Shadow; i++) m.Step();
        Assert.False(m.Shadow);
        Assert.Null(m.ShadowPending);
        Assert.Equal(0, m.Streak);
        Assert.True(m.WaveNumber >= wave);
        Assert.Contains(m.Events, e => e.Type == TdEventType.ShadowChanged && e.A == 0);
    }

    [Fact]
    public void Shadow_runs_on_its_own_with_auto_start_in_classic_preparo()
    {
        var m = Create(options: TdMatchOptions.Default with { ContinuousWaves = false });
        typeof(TdMatchState).GetProperty("Credits")!.SetValue(m, 5000L);
        m.Apply(new BuildTower("bolt", 150));
        Assert.Equal(AutoStartMode.Off, m.AutoStart);
        m.Apply(new SetShadow(true));
        Assert.Equal(MatchPhase.Preparo, m.Phase);
        for (int i = 0; i < 30 * 40 && m.Phase == MatchPhase.Preparo; i++) m.Step();
        Assert.Equal(MatchPhase.Onda, m.Phase); // contagem automática iniciou a onda sem ação do jogador
    }

    [Fact]
    public void Shadow_state_changes_the_hash_only_while_active()
    {
        var a = Create();
        var b = Create();
        Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
        b.Apply(new SetShadow(true));
        Assert.NotEqual(a.ComputeStateHash(), b.ComputeStateHash());
    }
}
