using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using StartWave = SideWar.Core.Td.StartWave;

namespace SideWar.Core.Tests;

/// <summary>TD.19b (SDD-29 §11, CA-29.8, T-29.10): pausa determinística no Core.</summary>
public sealed class TdPauseTests
{
    private static readonly TdContent Base = TdMechanicsContent.Load();

    private static TdContent Rich()
    {
        var rules = Base.Stages.Rules with { CoreIntegrity = 100_000 };
        var stages = Base.Stages.Stages.Select(s => s with { StartingCredits = 1_000_000 }).ToArray();
        return Base with { Stages = Base.Stages with { Rules = rules, Stages = stages } };
    }

    private static StripLayout Full() =>
        StripLayout.Create(1200, 1200, 0f, 0f, 1200, TowerHost.Taskbar, [new FreeZone(0, 1200)]);

    private static TdMatchState New(int stage = 2, ulong seed = 7)
    {
        var s = TdMatchState.Create(Rich(), stage, Rng.Seed(seed, 54));
        Assert.True(s.Apply(new LayoutChanged(Full())).IsOk);
        Assert.True(s.Apply(new SetPresence(true)).IsOk);
        foreach (int x in new[] { 100, 300, 500, 700 }) Assert.True(s.Apply(new BuildTower("bolt", x)).IsOk);
        return s;
    }

    private static void StepN(TdMatchState s, int n) { for (int i = 0; i < n; i++) s.Step(); }

    [Fact(DisplayName = "TD.19b Pause/Resume state machine and rejections")]
    public void PauseResume_StateMachine()
    {
        var s = New();
        Assert.Equal(CommandReason.NotPaused, s.Apply(new Resume()).Reason);
        Assert.True(s.Apply(new Pause()).IsOk);
        Assert.True(s.Paused);
        Assert.Equal(CommandReason.AlreadyPaused, s.Apply(new Pause()).Reason);
        Assert.True(s.Apply(new Resume()).IsOk);
        Assert.False(s.Paused);
    }

    [Fact(DisplayName = "TD.19b Paused enters the hash only when true; resume restores the hash")]
    public void Hash_PausedOnlyWhenTrue()
    {
        var s = New();
        ulong before = s.ComputeStateHash();
        s.Apply(new Pause());
        Assert.NotEqual(before, s.ComputeStateHash());
        s.Apply(new Resume());
        Assert.Equal(before, s.ComputeStateHash());
    }

    [Fact(DisplayName = "TD.19b Time commands are refused while paused; build/sell/target/answer are accepted")]
    public void Paused_CommandPolicy()
    {
        var s = New();
        s.Apply(new Pause());
        Assert.Equal(CommandReason.PausedCommand, s.Apply(new StartWave()).Reason);
        Assert.Equal(CommandReason.PausedCommand, s.Apply(new CallNextWave()).Reason);
        Assert.Equal(CommandReason.PausedCommand, s.Apply(new UseAbility("firewall")).Reason);
        Assert.Equal(MatchPhase.Preparo, s.Phase);
        Assert.True(s.Apply(new BuildTower("bolt", 400)).IsOk);
        Assert.True(s.Apply(new SideWar.Core.Td.SetTargeting(1, TdTargeting.Strong)).IsOk);
        Assert.True(s.Apply(new SellTower(2)).IsOk);
        Assert.True(s.Apply(new AnswerBonus(1, true)).IsOk);
    }

    [Fact(DisplayName = "TD.19b Step while paused freezes everything in Preparo (countdown) and Onda")]
    public void Step_FrozenWhenPaused()
    {
        var s = New();
        Assert.True(s.Apply(new SetAutoStart(AutoStartMode.Countdown, 20)).IsOk);
        StepN(s, 60);
        s.Apply(new Pause());
        ulong h = s.ComputeStateHash();
        double cd = s.CountdownRemainingSeconds;
        long frame = s.Frame;
        StepN(s, 5000); // mais que o countdown inteiro
        Assert.Equal(h, s.ComputeStateHash());
        Assert.Equal(cd, s.CountdownRemainingSeconds);
        Assert.Equal(frame, s.Frame);
        Assert.Equal(MatchPhase.Preparo, s.Phase);
        s.Apply(new Resume());
        StepN(s, 1);
        Assert.True(s.CountdownRemainingSeconds < cd);
    }

    [Fact(DisplayName = "TD.19b Ability cooldowns, spawns and overlapped waves freeze under pause")]
    public void Onda_AbilitiesSpawnsFrozen()
    {
        var s = New();
        Assert.True(s.Apply(new StartWave()).IsOk);
        StepN(s, 200);
        string ability = s.AbilityStatuses.First(a => a.Unlocked && a.Id != "snapshot").Id;
        Assert.True(s.Apply(new UseAbility(ability)).IsOk);
        StepN(s, 30);
        var cds = s.AbilityStatuses.Select(a => (a.CooldownTicks, a.ActiveTicks)).ToArray();
        long tick = s.Tick;
        s.Apply(new Pause());
        ulong h = s.ComputeStateHash();
        StepN(s, 3000);
        Assert.Equal(tick, s.Tick);
        Assert.Equal(cds, s.AbilityStatuses.Select(a => (a.CooldownTicks, a.ActiveTicks)).ToArray());
        Assert.Equal(h, s.ComputeStateHash());
        s.Apply(new Resume());
        s.Step();
        Assert.Equal(tick + 1, s.Tick);
    }

    [Fact(DisplayName = "TD.19b Presence changes during pause are deterministic")]
    public void Presence_DuringPause()
    {
        var s = New();
        s.Apply(new StartWave());
        StepN(s, 100);
        s.Apply(new Pause());
        Assert.True(s.Apply(new SetPresence(false)).IsOk);
        Assert.False(s.Present);
    }

    private static List<TdLogEntry> BaseLog() =>
    [
        new(0, new LayoutChanged(Full())), new(0, new SetPresence(true)),
        new(0, new BuildTower("bolt", 100)), new(0, new BuildTower("bolt", 300)), new(0, new BuildTower("bolt", 500)), new(0, new BuildTower("bolt", 700)),
        new(1, new StartWave()), new(3000, new SetPresence(false)), new(3300, new SetPresence(true)),
    ];

    private static TdReplayResult Run(IEnumerable<TdLogEntry> log) => TdReplayRunner.Run(Rich(), new TdReplayScript(2, 42, 54, log.ToList(), 6000));

    [Fact(DisplayName = "TD.19b Replay: pause/resume pairs at many points do not change the StateHash")]
    public void Replay_PausesAreTransparent()
    {
        var plain = Run(BaseLog());
        foreach (long f in new long[] { 1, 2, 50, 400, 1500, 2999, 3000, 3301, 5000 })
        {
            var withPause = BaseLog();
            withPause.Add(new TdLogEntry(f, new Pause(), 0));
            withPause.Add(new TdLogEntry(f, new Resume(), 9));
            var r = Run(withPause);
            Assert.Equal(plain.StateHash, r.StateHash);
            Assert.Equal(plain.Ticks, r.Ticks);
            Assert.Equal(0, r.CommandsRejected - plain.CommandsRejected);
        }
        var many = BaseLog();
        for (int i = 0; i < 40; i++) { many.Add(new TdLogEntry(100 + i * 97, new Pause(), 0)); many.Add(new TdLogEntry(100 + i * 97, new Resume(), 1)); }
        Assert.Equal(plain.StateHash, Run(many).StateHash);
    }

    [Fact(DisplayName = "TD.19b Replay: log with commands inside pause is deterministic (5 runs) and ordered by (frame, seq)")]
    public void Replay_CommandsInsidePause_Deterministic()
    {
        List<TdLogEntry> Log()
        {
            var l = BaseLog();
            // declarados fora de ordem: a ordem real vem de (Frame, Seq)
            l.Add(new TdLogEntry(500, new Resume(), 3));
            l.Add(new TdLogEntry(500, new SellTower(2), 2));
            l.Add(new TdLogEntry(500, new BuildTower("bolt", 800), 1));
            l.Add(new TdLogEntry(500, new Pause(), 0));
            l.Add(new TdLogEntry(900, new Pause(), 0));
            l.Add(new TdLogEntry(900, new StartWave(), 1)); // recusado em pausa
            l.Add(new TdLogEntry(900, new Resume(), 2));
            return l;
        }
        var first = Run(Log());
        for (int i = 0; i < 4; i++) Assert.Equal(first.StateHash, Run(Log()).StateHash);
        Assert.True(first.CommandsRejected >= 1);
        Assert.NotEqual(Run(BaseLog()).StateHash, first.StateHash);
    }

    [Fact(DisplayName = "TD.19b Replay ending paused stops instead of looping")]
    public void Replay_EndsPaused()
    {
        var l = BaseLog();
        l.Add(new TdLogEntry(200, new Pause()));
        var r = Run(l);
        Assert.Equal(200, r.Frames);
    }
}
