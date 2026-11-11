using SideWar.Core.Content;
using SideWar.Core.Simulation;
using Xunit;

namespace SideWar.Core.Tests;

/// <summary>TU.1: farm de w−1 com o chefe em espera em Ausente (SDD-25 RF-25.4) e regra única de nível (SDD-27 RF-27.9).</summary>
public sealed class SimFarmWaveTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);
    private static int BossWave => Content.Waves.BossEvery;

    private static SimState Absent()
    {
        var s = SimState.CreateInitial(Content,
            StripLayout.Create(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 380f }), Rng.Seed(42, 54));
        s.Stage = 1;
        s.Wave = BossWave;
        s.StartEvents(Ctx);
        s.Present = false;
        s.Step(Ctx);
        return s;
    }

    [Fact(DisplayName = "TU.1 Ausente: onda de chefe fica retida e StartFarmWave joga w−1 sem mexer em Wave/StageWaveMax")]
    public void Farm_Starts_On_Held_Boss()
    {
        var s = Absent();
        Assert.True(s.BossHold);
        Assert.Null(s.ActivePlan);
        int max = s.StageWaveMax;
        Assert.True(s.StartFarmWave(Ctx, BossWave - 1));
        Assert.Equal(BossWave - 1, s.FarmWave);
        Assert.Equal(BossWave, s.Wave);
        Assert.Equal(max, s.StageWaveMax);
        Assert.False(s.StartFarmWave(Ctx, BossWave - 1));   // já há agenda
        Assert.False(Absent().StartFarmWave(Ctx, BossWave));   // só w < Wave
    }

    [Fact(DisplayName = "TU.1 fim do farm: onda e chefe seguem retidos (loop); em Presente o encontro começa")]
    public void Farm_Ends_And_Boss_Starts_On_Present()
    {
        var s = Absent();
        Assert.True(s.StartFarmWave(Ctx, BossWave - 1));
        long cleared = 0;
        for (int i = 0; i < 20000 && s.FarmWave != 0; i++)
        {
            s.Step(Ctx);
            s.Enemies.Clear();
            s.BaseHp = s.MaxBaseHp;
        }
        Assert.Equal(0, s.FarmWave);
        Assert.Equal(BossWave, s.Wave);
        Assert.True(s.BossHold);
        Assert.Null(s.Encounter);
        cleared++;
        Assert.True(s.StartFarmWave(Ctx, BossWave - 1));   // loop

        for (int i = 0; i < 20000 && s.FarmWave != 0; i++)
        {
            s.Step(Ctx);
            s.Enemies.Clear();
            s.BaseHp = s.MaxBaseHp;
        }
        s.Present = true;
        s.Step(Ctx);
        Assert.NotNull(s.Encounter);
        Assert.Equal(1, cleared);
    }

    [Fact(DisplayName = "TU.1 queda durante o farm: recua da onda de chefe e libera a retenção")]
    public void Fall_In_Farm_Releases_Hold()
    {
        var s = Absent();
        Assert.True(s.StartFarmWave(Ctx, BossWave - 1));
        s.BaseHp = 0;
        s.Step(Ctx);
        Assert.Equal(0, s.FarmWave);
        Assert.False(s.BossHold);
        Assert.Equal(BossWave - Content.Economy.FallWaves, s.Wave);
    }

    [Fact(DisplayName = "TU.1 LevelRules: xp ≥ nível × 45 sobe, resto preservado; mesma regra do LevelSystem")]
    public void LevelRules_Match_Spec()
    {
        int level = 1; long xp = 100;
        Assert.Equal(1, LevelRules.Advance(ref level, ref xp, 45));
        Assert.Equal((2, 55L), (level, xp));
        level = 1; xp = 45 + 90 + 135;
        Assert.Equal(3, LevelRules.Advance(ref level, ref xp, 45));
        Assert.Equal((4, 0L), (level, xp));

        var s = SimState.CreateInitial(Content,
            StripLayout.Create(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 380f }), Rng.Seed(1, 2));
        s.Xp = 45 + 90 + 135 + 7;
        s.Step(Ctx);
        Assert.Equal(4, s.Level);
        Assert.Equal(7, s.Xp);
    }
}
