using SideWar.Core.Content;
using SideWar.Core.Simulation;
using Xunit;

namespace SideWar.Core.Tests;

/// <summary>Cobertura Core de <c>SimState.RestoreWave</c> (SDD-14 restore, T6.9a): três ramos e guardas.</summary>
public sealed class SimRestoreWaveTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);

    private static SimState Fresh()
    {
        var s = SimState.CreateInitial(Content,
            StripLayout.Create(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 380f }), Rng.Seed(7, 13));
        s.Stage = 1;
        s.Wave = 3;
        s.StartEvents(Ctx);
        return s;
    }

    [Fact(DisplayName = "T3.13 RestoreWave sem agenda limpa plano, farm e spawn; mantém relógios informados")]
    public void Restore_NoPlan_Clears_Plan()
    {
        var s = Fresh();
        s.StartWave(Ctx);
        s.RestoreWave(Ctx, 1.5, hasPlan: false, farmWave: 4, nextSpawn: 3, waveClock: 2f, rest: 1.25f);
        Assert.Null(s.ActivePlan);
        Assert.Equal(0, s.NextSpawnIndex);
        Assert.Equal(0, s.FarmWave);
        Assert.Equal(1.5, s.RegionHpMultiplier);
        Assert.Equal(2f, s.WaveClock);
        Assert.Equal(1.25f, s.Rest);
    }

    [Fact(DisplayName = "T3.13 RestoreWave com onda atual reconstrói a agenda da Wave e o índice de spawn")]
    public void Restore_CurrentWave_Rebuilds_Plan()
    {
        var s = Fresh();
        s.RestoreWave(Ctx, 1.0, hasPlan: true, farmWave: 0, nextSpawn: 2, waveClock: 0.5f, rest: 0f);
        Assert.NotNull(s.ActivePlan);
        Assert.Equal(0, s.FarmWave);
        Assert.Equal(Math.Min(2, s.ActivePlan!.Spawns.Length), s.NextSpawnIndex);
        Assert.Equal(0.5f, s.WaveClock);

        var reference = Fresh();
        reference.StartWave(Ctx);
        Assert.Equal(reference.ActivePlan!.Spawns.Length, s.ActivePlan.Spawns.Length);
    }

    [Fact(DisplayName = "T3.13 RestoreWave com farmWave > 0 reconstrói a agenda da onda de farm sem mexer em Wave")]
    public void Restore_Farm_Rebuilds_Farm_Plan()
    {
        var s = Fresh();
        s.RestoreWave(Ctx, 1.0, hasPlan: true, farmWave: 2, nextSpawn: 1, waveClock: 0.25f, rest: 0f);
        Assert.Equal(2, s.FarmWave);
        Assert.Equal(3, s.Wave);
        Assert.NotNull(s.ActivePlan);
        Assert.Equal(1, s.NextSpawnIndex);
        Assert.Equal(0.25f, s.WaveClock);
    }

    [Fact(DisplayName = "T3.13 RestoreWave valida contexto nulo")]
    public void Restore_Guards()
    {
        var s = Fresh();
        Assert.Throws<ArgumentNullException>(() => s.RestoreWave(null!, 1.0, false, 0, 0, 0f, 0f));
    }
}
