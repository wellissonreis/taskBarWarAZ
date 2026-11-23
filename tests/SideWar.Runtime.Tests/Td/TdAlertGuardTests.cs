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
        Assert.False(s.IsMuted(TdAlertType.PassedLastTower));
        s.SetMuted(TdAlertType.ImminentLeak, true);
        Assert.True(s.IsMuted(TdAlertType.ImminentLeak));
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

    [Fact]
    public void Damage_and_leak_share_three_warnings_per_run_and_restart_clears_the_limit()
    {
        var r = new Rig(autoPause: false);
        var guard = r.Host.Alerts;
        guard.Settings.Enabled = true;
        var enemies = Assert.IsType<List<TdEnemy>>(r.Match.Enemies);
        enemies.Add(new TdEnemy { Id = 901, X = r.Match.LeakX + 30, Hp = 5, Speed = 30, CoreDamage = 1 });
        for (int i = 0; i < 4; i++)
        {
            typeof(TdMatchState).GetProperty(nameof(TdMatchState.Tick))!.SetValue(r.Match, (long)i * TdAlertGuard.CooldownTicks);
            typeof(TdMatchState).GetProperty(nameof(TdMatchState.WaveNumber))!.SetValue(r.Match, i + 1);
            int before = guard.Raised;
            guard.Evaluate(r.Match);
            if (i < 3) { Assert.Equal(before + 1, guard.Raised); Assert.Equal(TdAlertType.ImminentLeak, guard.Current?.Type); }
            else if (guard.Raised > before) Assert.DoesNotContain(guard.Current!.Value.Type, new[] { TdAlertType.ImminentLeak, TdAlertType.TowerDamage });
        }
        guard.Reset();
        guard.Evaluate(r.Match);
        Assert.Equal(TdAlertType.ImminentLeak, guard.Current?.Type);
    }

    [Fact]
    public void Recurring_tips_are_shown_once_per_type_per_run()
    {
        var r = new Rig(autoPause: false);
        var guard = r.Host.Alerts;
        guard.Settings.Enabled = true;
        guard.Evaluate(r.Match);
        Assert.Equal(TdAlertType.FewTowers, guard.Current?.Type);
        typeof(TdMatchState).GetProperty(nameof(TdMatchState.Tick))!.SetValue(r.Match, (long)TdAlertGuard.CooldownTicks);
        typeof(TdMatchState).GetProperty(nameof(TdMatchState.WaveNumber))!.SetValue(r.Match, 2);
        guard.Evaluate(r.Match);
        Assert.Equal(TdAlertType.IdleCredits, guard.Current?.Type);
        int raised = guard.Raised;
        typeof(TdMatchState).GetProperty(nameof(TdMatchState.Tick))!.SetValue(r.Match, 2L * TdAlertGuard.CooldownTicks);
        typeof(TdMatchState).GetProperty(nameof(TdMatchState.WaveNumber))!.SetValue(r.Match, 3);
        guard.Evaluate(r.Match);
        Assert.Equal(raised, guard.Raised);
    }
}
