using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Runtime.Tests.Td;

/// <summary>BUG-ENTRE-FASES: vencer a fase e seguir para a próxima nunca pode deixar o jogo parado (qualquer auto-início, velocidade e sombra).</summary>
public sealed class TdBetweenStagesTests
{
    private static TdContent Tiny()
    {
        var b = TdContentLoader.LoadEmbedded();
        var defs = Enumerable.Range(1, 2).Select(i => new TdWaveDef(i, false, 1.0, [new TdWaveGroupDef("grunt", 1, 0.0, 1.0)])).ToArray();
        var stages = b.Stages.Stages.Select(s => s with { Waves = 2, StartingCredits = 5000 }).ToArray();
        var waveStages = b.Waves.Stages.Select(s => new TdStageWavesDef(s.Stage, defs)).ToArray();
        return b with { Waves = new TdWavesDef(waveStages), Stages = b.Stages with { Stages = stages } };
    }

    private static (TdGameHost Host, TdMatchState Match, FakeClock Clock) Rig(AutoStartMode mode, bool extras, int speed, bool shadow, bool towers = true)
    {
        var clock = new FakeClock();
        var app = new AppStateMachine();
        app.LeaveHidden(HiddenReason.Starting);
        var match = TdMatchState.Create(Tiny(), 1, Rng.Seed(42, 54), null, TdMatchOptions.Default);
        var host = new TdGameHost(clock, app, match) { ExtraSpeeds = extras, GameSpeed = speed };
        host.OnCommand(new LayoutChanged(TdSanityBot.TypicalLayout()));
        if (towers) host.OnCommand(new BuildTower("bolt", 600));
        host.OnCommand(new SetAutoStart(mode, 5));
        if (shadow) host.OnCommand(new SetShadow(true));
        host.OnWake(clock);
        return (host, match, clock);
    }

    [Theory]
    [InlineData(AutoStartMode.Off, false, 1, false)]
    [InlineData(AutoStartMode.Countdown, false, 1, false)]
    [InlineData(AutoStartMode.Immediate, false, 1, false)]
    [InlineData(AutoStartMode.Immediate, false, 2, false)]
    [InlineData(AutoStartMode.Immediate, false, 3, false)]
    [InlineData(AutoStartMode.Immediate, true, TdSpeed.Max, false)]
    [InlineData(AutoStartMode.Countdown, true, 10, false)]
    [InlineData(AutoStartMode.Off, false, 1, true)]
    [InlineData(AutoStartMode.Countdown, false, 1, true)]
    [InlineData(AutoStartMode.Immediate, true, 5, true)]
    public void Winning_a_stage_flows_into_the_next_one_by_itself(AutoStartMode mode, bool extras, int speed, bool shadow)
    {
        var (host, match, clock) = Rig(mode, extras, speed, shadow);
        // Off/Countdown sem sombra: a 1ª onda é do jogador; depois disso a corrida segue sozinha.
        if (mode == AutoStartMode.Off && !shadow) { host.OnCommand(new StartWave()); host.OnWake(clock); }
        long step = (long)(clock.TicksPerSecond / 30);
        int lastClears = 0;
        for (int i = 0; i < 400_000 && match.StageResults.Count < 3; i++)
        {
            clock.AdvanceTicks(step); host.OnWake(clock);
            if (match.Phase == MatchPhase.Preparo && mode == AutoStartMode.Off && !shadow && match.StageResults.Count > lastClears) throw new Xunit.Sdk.XunitException("parou em Preparo entre as fases");
            lastClears = match.StageResults.Count;
        }
        Assert.True(match.StageResults.Count >= 3, $"travou em fase {match.StageNumber} onda {match.WaveNumber} fase {match.Phase} pausa {match.Paused}");
    }
}
