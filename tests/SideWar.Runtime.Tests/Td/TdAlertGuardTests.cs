using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Td;
using SideWar.Runtime.Timing;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Runtime.Tests.Td;

/// <summary>TD.40b2: proteção contra derrota no host (SDD-38 §3), replay e cadência de alertas.</summary>
public sealed class TdAlertGuardTests
{
    private static readonly TdContent Content = TdContentLoader.LoadEmbedded();

    private sealed class Rig
    {
        public readonly FakeClock Clock = new();
        public readonly AppStateMachine App = new();
        public readonly TdMatchState Match = TdMatchState.Create(Content, 1, Rng.Seed(42, 54));
        public readonly TdGameHost Host;

        public Rig(bool autoPause = true, int speed = 1)
        {
            Host = new TdGameHost(Clock, App, Match) { GameSpeed = speed };
            Host.Alerts.Settings.AutoPause = autoPause;
            App.LeaveHidden(HiddenReason.Starting);
            Host.OnCommand(new LayoutChanged(StripLayout.Create(1200, 1200, 0f, 0f, 1200, TowerHost.Taskbar, [new FreeZone(0, 1200)])));
            Host.OnCommand(new StartWave());
            Wake();
        }

        public void Wake() => Host.OnWake(Clock);

        public void Run(double seconds)
        {
            int n = (int)Math.Round(seconds * 30);
            long step = (long)(Clock.TicksPerSecond / 30);
            for (int i = 0; i < n; i++) { Clock.AdvanceTicks(step); Wake(); }
        }

        /// <summary>Joga (iniciando ondas em Preparo) até a derrota ou o limite.</summary>
        public void RunToDefeat(double maxSeconds = 1200)
        {
            for (double t = 0; t < maxSeconds && Match.Phase is MatchPhase.Onda or MatchPhase.Preparo; t += 1)
            {
                if (Match.Phase == MatchPhase.Preparo) { Host.OnCommand(new StartWave()); Wake(); }
                Run(1);
            }
        }

        public void RunUntilHeld(double maxSeconds = 600)
        {
            for (double t = 0; t < maxSeconds && !Host.Alerts.Holding && Match.Phase == MatchPhase.Onda; t += 1) Run(1);
        }
    }

    [Fact]
    public void Without_towers_enemy_near_portal_holds_before_defeat_and_absence_never_loses()
    {
        var r = new Rig();
        r.RunUntilHeld();
        Assert.True(r.Host.Alerts.Holding);
        Assert.Equal(TdAlertType.PassedLastTower, r.Host.Alerts.HoldAlert!.Value.Type);
        Assert.Equal(MatchPhase.Onda, r.Match.Phase);
        Assert.False(r.Match.Paused); // não usa a pausa do Core: pausas do usuário seguem independentes
        long frame = r.Match.Frame;
        ulong hash = r.Match.ComputeStateHash();
        r.Run(3600); // ausência de uma hora
        Assert.Equal(frame, r.Match.Frame);
        Assert.Equal(hash, r.Match.ComputeStateHash());
        Assert.Null(r.Host.NextDeadline);
        Assert.NotEqual(MatchPhase.Derrota, r.Match.Phase);
    }

    [Fact]
    public void Let_fall_allows_defeat_and_replay_reproduces_the_same_state()
    {
        var r = new Rig();
        r.RunUntilHeld();
        r.Host.LetTowerFall();
        long f0 = r.Match.Frame; r.Run(5);
        Assert.True(r.Match.Frame > f0, $"frame parado em {f0}; holding={r.Host.Alerts.Holding} fall={r.Host.Alerts.FallAuthorized} nd={r.Host.NextDeadline}");
        r.RunToDefeat();
        Assert.True(r.Match.Phase == MatchPhase.Derrota, $"{r.Match.Phase} int={r.Match.Integrity} enemies={r.Match.Enemies.Count} holding={r.Host.Alerts.Holding} wave={r.Match.WaveNumber} frame={r.Match.Frame} paused={r.Match.Paused}");
        Assert.Contains(r.Host.Alerts.Log, e => e.Kind == TdProtectionKind.LetFall);
        var replay = TdReplayRunner.Run(Content, r.Host.BuildReplay(1, 42, 54));
        Assert.Equal(r.Match.ComputeStateHash(), replay.StateHash);
        Assert.Equal(r.Match.Frame, replay.Frames);
        Assert.Equal(0, replay.CommandsRejected);
    }

    [Fact]
    public void Save_lets_the_player_act_and_new_threats_hold_again_before_any_defeat()
    {
        var r = new Rig();
        r.RunUntilHeld();
        long held = r.Match.Frame;
        r.Host.OnCommand(new BuildTower("bolt", 300)); // age em retenção
        r.Wake();
        Assert.Equal(held, r.Match.Frame);
        r.Host.SaveTower();
        r.Run(2);
        Assert.True(r.Match.Frame > held || r.Host.Alerts.Holding);
        for (int i = 0; i < 40; i++)
        {
            if (r.Match.Phase == MatchPhase.Preparo) { r.Host.OnCommand(new StartWave()); r.Wake(); }
            r.RunUntilHeld(120);
            if (r.Host.Alerts.Holding) r.Host.SaveTower();
        }
        Assert.NotEqual(MatchPhase.Derrota, r.Match.Phase);
        var replay = TdReplayRunner.Run(Content, r.Host.BuildReplay(1, 42, 54));
        Assert.Equal(r.Match.ComputeStateHash(), replay.StateHash);
    }

    [Theory]
    [InlineData(2)] [InlineData(3)]
    public void Fast_speed_batches_check_before_every_step(int speed)
    {
        var r = new Rig(speed: speed);
        r.RunUntilHeld();
        Assert.True(r.Host.Alerts.Holding);
        double line = TdRiskAnalyzer.DefenseLineX(r.Match);
        // O passo que cruzou a linha foi o último: nenhum inimigo avançou um segundo passo além dela.
        Assert.All(r.Match.Enemies.Where(e => e.Hp > 0), e => Assert.True(e.X > line - e.Speed / 30.0 * 2));
        Assert.Equal(r.Match.Integrity, r.Match.IntegrityMax);
    }

    [Fact]
    public void Auto_pause_off_keeps_the_old_behavior_and_defeat_happens()
    {
        var r = new Rig(autoPause: false);
        r.RunToDefeat();
        Assert.Equal(MatchPhase.Derrota, r.Match.Phase);
        Assert.False(r.Host.Alerts.Holding);
    }

    [Fact]
    public void Boss_key_and_user_pause_are_not_released_by_the_guard()
    {
        var r = new Rig();
        r.RunUntilHeld();
        r.App.EnterHidden(HiddenReason.User);
        r.Run(10);
        Assert.True(r.Host.Alerts.Holding);
        r.App.LeaveHidden(HiddenReason.User);
        Assert.True(r.Host.Alerts.Holding);
        r.Host.OnCommand(new Pause()); r.Wake();
        r.Host.SaveTower(); r.Run(5);
        long f = r.Match.Frame;
        r.Run(5);
        Assert.True(r.Match.Paused);
        Assert.Equal(f, r.Match.Frame); // a pausa do usuário continua valendo após Salvar
    }

    [Fact]
    public void Protection_cannot_be_muted_but_ordinary_critical_types_can()
    {
        var s = new TdAlertSettings { AutoPause = true };
        s.SetMuted(TdAlertType.PassedLastTower, true);
        s.SetMuted(TdAlertType.FatalDrop, true);
        Assert.False(s.IsMuted(TdAlertType.PassedLastTower));
        Assert.False(s.IsMuted(TdAlertType.FatalDrop));
        s.SetMuted(TdAlertType.CoreAtRisk, true);
        Assert.True(s.IsMuted(TdAlertType.CoreAtRisk));
    }

    [Fact]
    public void Non_fatal_alerts_respect_cooldown_and_two_per_wave_and_do_not_pause()
    {
        var r = new Rig(autoPause: false);
        r.Host.Alerts.Settings.Enabled = true;
        r.Host.OnCommand(new BuildTower("bolt", 300)); r.Wake();
        int raised = r.Host.Alerts.Raised;
        r.Run(20);
        Assert.False(r.Host.Alerts.Holding);
        Assert.True(r.Host.Alerts.Raised - raised <= TdAlertGuard.MaxNonFatalPerWave);
    }
}
