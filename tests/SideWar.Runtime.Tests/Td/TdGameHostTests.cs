using BuildTower = SideWar.Core.Td.BuildTower;
using SideWar.Runtime.Timing;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Td;
using Xunit;

namespace SideWar.Runtime.Tests.Td;

public sealed class TdGameHostTests
{
    private static readonly TdContent Content = TdContentLoader.LoadEmbedded();

    private sealed class Observer : ITdMatchObserver
    {
        public int EventBatches, Ended;
        public TdMatchResult? Last;
        public void OnEvents(TdMatchState match, IReadOnlyList<TdEvent> events) => EventBatches++;
        public void OnMatchEnded(TdMatchState match, TdMatchResult result) { Ended++; Last = result; }
    }

    private sealed class Rig
    {
        public readonly FakeClock Clock = new();
        public readonly AppStateMachine App = new();
        public readonly Observer Obs = new();
        public TdMatchState Match = TdMatchState.Create(Content, 1, Rng.Seed(42, 54));
        public readonly TdGameHost Host;

        public Rig(bool visible = true, ITdPresence? input = null, TdMatchState? match = null)
        {
            if (match is not null) Match = match;
            Host = new TdGameHost(Clock, App, Match, Obs, input);
            if (visible) App.LeaveHidden(HiddenReason.Starting);
            Wake();
        }

        public void Wake() => Host.OnWake(Clock);

        /// <summary>Avança o relógio em passos de 1/30 s e acorda o host a cada passo.</summary>
        public void Run(double seconds)
        {
            int n = (int)Math.Round(seconds * 30);
            long step = (long)(Clock.TicksPerSecond / 30);
            for (int i = 0; i < n; i++) { Clock.AdvanceTicks(step); Wake(); }
        }
    }

    [Fact]
    public void Preparo_does_not_step_and_has_no_deadline()
    {
        var r = new Rig();
        r.Run(5);
        Assert.Equal(MatchPhase.Preparo, r.Match.Phase);
        Assert.Equal(0, r.Host.StepsExecuted);
        Assert.Equal(0, r.Match.Frame);
        Assert.Null(r.Host.NextDeadline); // 0 fps em Preparo
    }

    [Fact]
    public void StartWave_command_starts_the_wave_and_steps_run_at_30Hz()
    {
        var r = new Rig();
        r.Host.OnCommand(new StartWave());
        Assert.NotNull(r.Host.NextDeadline); // comando pendente acorda o laço
        r.Wake();
        Assert.Equal(MatchPhase.Onda, r.Match.Phase);
        r.Run(2);
        Assert.InRange(r.Host.StepsExecuted, 58, 61);
        Assert.NotNull(r.Host.NextDeadline);
        Assert.Contains(r.Host.CommandLog, e => e.Command is StartWave);
        Assert.True(r.Host.TryDequeueResult(out _, out var res));
        Assert.True(res.IsOk);
    }

    [Fact]
    public void Wave_request_starts_in_Preparo_and_is_logged_as_StartWave()
    {
        var r = new Rig();
        r.Host.RequestWave();
        r.Wake();
        Assert.Equal(MatchPhase.Onda, r.Match.Phase);
        Assert.IsType<StartWave>(Assert.Single(r.Host.CommandLog).Command);
    }

    [Fact]
    public void Wave_request_during_Onda_becomes_CallNextWave()
    {
        var r = new Rig();
        r.Host.RequestWave();
        r.Wake();
        r.Host.RequestWave(); // spawns ainda não esgotados: rejeitado pelo estado, mas traduzido para CallNextWave
        r.Wake();
        Assert.IsType<CallNextWave>(r.Host.CommandLog[1].Command);
        Assert.True(r.Host.TryDequeueResult(out _, out _));
        Assert.True(r.Host.TryDequeueResult(out _, out var second));
        Assert.False(second.IsOk);
    }

    [Fact]
    public void Commands_apply_between_ticks_with_frame_in_log()
    {
        var r = new Rig();
        r.Host.OnCommand(new StartWave());
        r.Wake();
        r.Run(1);
        long frame = r.Match.Frame;
        r.Host.OnCommand(new BuildTower("bolt", 600));
        r.Wake();
        var entry = r.Host.CommandLog[^1];
        Assert.Equal(frame, entry.Frame);
    }

    [Fact]
    public void Hiding_pauses_the_simulation_and_resuming_does_not_catch_up()
    {
        var r = new Rig();
        r.Host.OnCommand(new StartWave());
        r.Wake();
        r.Run(1);
        long before = r.Host.StepsExecuted;
        foreach (var reason in new[] { HiddenReason.User, HiddenReason.Fullscreen, HiddenReason.SessionLocked })
        {
            r.App.EnterHidden(reason);
            r.Run(10);
            Assert.Equal(before, r.Host.StepsExecuted);
            Assert.Null(r.Host.NextDeadline);
            Assert.False(r.Host.SimulationActive);
            r.App.LeaveHidden(reason);
            r.Wake();
            Assert.True(r.Host.SimulationActive);
            Assert.InRange(r.Host.StepsExecuted - before, 0, 1); // sem tempo acumulado do período oculto
            before = r.Host.StepsExecuted;
        }
    }

    [Fact]
    public void User_pause_stops_steps_but_commands_still_apply()
    {
        var r = new Rig();
        r.Host.OnCommand(new StartWave());
        r.Wake();
        r.App.SetPause(PauseReason.User);
        long before = r.Host.StepsExecuted;
        r.Run(2);
        Assert.Equal(before, r.Host.StepsExecuted);
        r.Host.OnCommand(new BuildTower("bolt", 300));
        r.Wake();
        Assert.Equal(before, r.Host.StepsExecuted);
        Assert.Equal(2, r.Host.CommandLog.Count);
    }

    [Fact]
    public void AutoStart_off_by_default_and_on_counts_down_then_starts_the_wave()
    {
        var r = new Rig();
        r.Run(20);
        Assert.Equal(MatchPhase.Preparo, r.Match.Phase); // padrão desligado: nunca inicia sozinha

        r.Host.ApplyAutoStart(true, 5);
        r.Wake();
        Assert.Equal(AutoStartMode.Countdown, r.Match.AutoStart);
        Assert.NotNull(r.Host.NextDeadline); // com auto-início o Preparo precisa de Step
        r.Run(4);
        Assert.Equal(MatchPhase.Preparo, r.Match.Phase);
        Assert.True(r.Host.StepsExecuted > 0);
        r.Run(2);
        Assert.Equal(MatchPhase.Onda, r.Match.Phase);
    }

    [Fact]
    public void AutoStart_countdown_freezes_while_hidden()
    {
        var r = new Rig();
        r.Host.ApplyAutoStart(true, 5);
        r.Wake();
        r.Run(2);
        r.App.EnterHidden(HiddenReason.User);
        r.Run(30);
        Assert.Equal(MatchPhase.Preparo, r.Match.Phase);
        r.App.LeaveHidden(HiddenReason.User);
        r.Wake();
        r.Run(1);
        Assert.Equal(MatchPhase.Preparo, r.Match.Phase);
        r.Run(3);
        Assert.Equal(MatchPhase.Onda, r.Match.Phase);
    }

    [Fact]
    public void AutoStart_seconds_are_clamped_to_5_60()
    {
        var r = new Rig();
        r.Host.ApplyAutoStart(true, 1);
        r.Wake();
        Assert.Equal(5, r.Match.AutoStartSeconds);
        r.Host.ApplyAutoStart(true, 500);
        r.Wake();
        Assert.Equal(60, r.Match.AutoStartSeconds);
    }

    [Fact]
    public void Match_end_is_reported_once_and_stepping_stops()
    {
        var r = new Rig();
        r.Host.ApplyAutoStart(true, 5);
        r.Wake();
        for (int i = 0; i < 400 && r.Match.Phase != MatchPhase.Derrota; i++) r.Run(10); // sem torres: as ondas vazam até a Derrota
        Assert.Equal(MatchPhase.Derrota, r.Match.Phase);
        Assert.Equal(1, r.Obs.Ended);
        Assert.Equal(MatchPhase.Derrota, r.Obs.Last!.Value.Phase);
        long steps = r.Host.StepsExecuted;
        r.Run(5);
        Assert.Equal(steps, r.Host.StepsExecuted);
        Assert.Equal(1, r.Obs.Ended);
        Assert.Null(r.Host.NextDeadline);
        Assert.True(r.Obs.EventBatches > 0);
        Assert.Empty(r.Match.Events); // o host limpa os eventos depois de entregar
    }

    [Fact]
    public void Shutdown_drops_pending_and_stops()
    {
        var r = new Rig();
        r.Host.OnCommand(new StartWave());
        r.Host.Shutdown();
        r.Wake();
        Assert.Equal(MatchPhase.Preparo, r.Match.Phase);
        Assert.Null(r.Host.NextDeadline);
    }

    [Fact]
    public void Replaying_the_command_log_reproduces_the_state_hash()
    {
        var r = new Rig();
        r.Host.OnCommand(new StartWave());
        r.Wake();
        r.Host.OnCommand(new BuildTower("bolt", 300));
        r.Run(20);
        var script = new TdReplayScript(1, 42, 54, r.Host.CommandLog, r.Match.Frame);
        var replay = TdReplayRunner.Run(Content, script);
        Assert.Equal(r.Match.ComputeStateHash(), replay.StateHash);
    }

    private sealed class FakeInput : ITdPresence
    {
        public bool Present = true;
        public bool IsPresent => Present;
    }

    [Fact]
    public void Presence_is_sent_to_the_log_only_on_change_and_only_in_wave()
    {
        var input = new FakeInput { Present = true };
        var r = new Rig(input: input);
        r.Run(1);
        Assert.DoesNotContain(r.Host.CommandLog, e => e.Command is SetPresence); // Preparo: não observa
        r.Host.OnCommand(new StartWave());
        r.Run(2);
        Assert.Single(r.Host.CommandLog, e => e.Command is SetPresence { Present: true }); // presente, enviado uma vez
        Assert.True(r.Match.Present);
        input.Present = true; r.Run(2);                                                       // continua presente: nada novo
        Assert.Single(r.Host.CommandLog, e => e.Command is SetPresence);
        input.Present = false; r.Run(1);                                                   // outro app ativado: ausente
        Assert.False(r.Match.Present);
        Assert.Equal(2, r.Host.CommandLog.Count(e => e.Command is SetPresence));
        r.Run(2);
        Assert.Equal(2, r.Host.CommandLog.Count(e => e.Command is SetPresence));
        input.Present = true; r.Run(1);
        Assert.True(r.Match.Present);
        Assert.Equal(3, r.Host.CommandLog.Count(e => e.Command is SetPresence));
    }

    [Fact]
    public void Without_an_input_source_presence_is_never_sent()
    {
        var r = new Rig();
        r.Host.OnCommand(new StartWave());
        r.Run(3);
        Assert.DoesNotContain(r.Host.CommandLog, e => e.Command is SetPresence);
    }

    [Fact]
    public void Presence_log_replays_to_the_same_hash_and_RequestCollectCrate_collects()
    {
        var bc = Content.Stages.Rules.BombCrate with { Chance = 1.0, MinDropSeconds = 1 };
        var stages = Content.Stages.Stages.Select(s => s with { StartingCredits = 1_000_000, MaxTowers = 12 }).ToArray();
        var content = Content with { Stages = Content.Stages with { Rules = Content.Stages.Rules with { BombCrate = bc, CoreIntegrity = 100_000 }, Stages = stages } };
        var match = TdMatchState.Create(content, 2, Rng.Seed(42, 54));
        var input = new FakeInput { Present = true };
        var r = new Rig(input: input, match: match);
        Assert.False(r.Host.RequestCollectCrate()); // sem caixa: nada enfileirado
        r.Host.OnCommand(new BuildTower("bolt", 300));
        r.Host.OnCommand(new StartWave());
        long credits = 0;
        for (int i = 0; i < 20 && !r.Match.CrateActive; i++) r.Run(1);
        Assert.True(r.Match.CrateActive);
        credits = r.Match.Credits;
        Assert.True(r.Host.RequestCollectCrate());
        r.Run(1); // o comando entra no próximo Wake; um passo depois fecha o Frame do replay
        Assert.False(r.Match.CrateActive);
        Assert.Contains(r.Host.CommandLog, e => e.Command is CollectCrate);
        Assert.True(r.Match.Credits >= credits + r.Match.WaveBonusFor(r.Match.WaveNumber));
        var replay = TdReplayRunner.Run(content, new TdReplayScript(2, 42, 54, r.Host.CommandLog, r.Match.Frame));
        Assert.Equal(r.Match.ComputeStateHash(), replay.StateHash);
    }
}
