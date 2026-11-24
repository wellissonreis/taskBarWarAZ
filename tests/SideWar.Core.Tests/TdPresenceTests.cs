using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using StartWave = SideWar.Core.Td.StartWave;

namespace SideWar.Core.Tests;

/// <summary>Sinal de presença, restrição do MoveTower e replay determinístico (a caixa-bomba foi removida na TD.30a).</summary>
public sealed class TdPresenceTests
{
    private static readonly TdContent Base = TdMechanicsContent.Load();

    /// <summary>Conteúdo com créditos fartos e integridade alta.</summary>
    private static TdContent Cfg()
    {
        var rules = Base.Stages.Rules with { CoreIntegrity = 100_000 };
        var stages = Base.Stages.Stages.Select(s => s with { StartingCredits = 1_000_000 }).ToArray();
        return Base with { Stages = Base.Stages with { Rules = rules, Stages = stages } };
    }

    private static StripLayout Full() =>
        StripLayout.Create(1200, 1200, 0f, 0f, 1200, TowerHost.Taskbar, [new FreeZone(0, 1200)]);

    private static TdMatchState New(TdContent c, int stage = 2, ulong seed = 7, bool present = true, params int[] towerX)
    {
        var s = TdMatchState.Create(c, stage, Rng.Seed(seed, 54));
        Assert.True(s.Apply(new LayoutChanged(Full())).IsOk);
        foreach (int x in towerX) Assert.True(s.Apply(new BuildTower("bolt", x)).IsOk);
        if (present) Assert.True(s.Apply(new SetPresence(true)).IsOk);
        return s;
    }

    [Fact(DisplayName = "RF-28.19 SetPresence is idempotent and does not touch the Rng")]
    public void SetPresence_Idempotent()
    {
        var s = New(Cfg(), present: false, towerX: [100]);
        ulong rng = s.RngState;
        Assert.True(s.Apply(new SetPresence(true)).IsOk);
        ulong h = s.ComputeStateHash();
        Assert.True(s.Apply(new SetPresence(true)).IsOk);
        Assert.Equal(h, s.ComputeStateHash());
        Assert.Equal(rng, s.RngState);
    }

    [Fact(DisplayName = "TD.3b MoveTower only accepts towers WITHOUT position; placed towers are rejected")]
    public void MoveTower_OnlyUnplaced()
    {
        var s = New(Cfg(), towerX: [100, 500]);
        int a = s.Towers[0].Id, b = s.Towers[1].Id;
        Assert.Equal(CommandReason.NotUnpositioned, s.Apply(new MoveTower(a, 300)).Reason);
        Assert.Equal(CommandReason.TowerNotFound, s.Apply(new MoveTower(99, 300)).Reason);
        Assert.True(s.Apply(new LayoutChanged(StripLayout.Create(1200, 1200, 0f, 0f, 1200, TowerHost.Taskbar, [new FreeZone(0, 300)]))).IsOk);
        Assert.False(s.Towers.First(t => t.Id == b).Placed);
        Assert.True(s.Apply(new MoveTower(b, 250)).IsOk);
        Assert.True(s.Towers.First(t => t.Id == b).Placed);
    }

    private static TdReplayScript Script() => new(2, 42, 54,
    [
        new TdLogEntry(0, new LayoutChanged(Full())),
        new TdLogEntry(0, new SetPresence(true)),
        new TdLogEntry(0, new BuildTower("bolt", 200)),
        new TdLogEntry(0, new BuildTower("bolt", 600)),
        new TdLogEntry(0, new BuildTower("bolt", 1000)),
        new TdLogEntry(1, new StartWave()),
        new TdLogEntry(3000, new SetPresence(false)),
        new TdLogEntry(3300, new SetPresence(true)),
    ], 9000);

    // StateHash mudou na TD.30a: o hash deixou de incluir os campos da caixa-bomba (o Rng também deixou de consumir 3 sorteios por onda).
    [Fact(DisplayName = "RNF-28.1 Presence in the log: same log + seed → same StateHash in 5 runs (golden)")]
    public void Replay_Deterministic5Runs()
    {
        var first = TdReplayRunner.Run(Base, Script());
        for (int i = 0; i < 4; i++) Assert.Equal(first.StateHash, TdReplayRunner.Run(Base, Script()).StateHash);
        Assert.Equal(Golden, first.StateHash);
    }

    private const ulong Golden = 6478426478904929683UL;
}
