using SideWar.Core;
using SideWar.Core.Content;
using SideWar.Core.Simulation;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Hosting;
using SideWar.Runtime.Presence;
using SideWar.Runtime.Timing;
using Xunit;

namespace SideWar.Runtime.Tests;

public sealed class GameHostTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);

    private sealed class Sink : IGameFrameSink
    {
        public int Count;
        public void Present(SimState state, double presentationSeconds) => Count++;
    }

    private sealed class Hook : IGameTickHook
    {
        public readonly List<string> Log = [];
        public void BeforeStep(SimState s, GameTickContext c) => Log.Add($"B{s.Tick}");
        public void AfterStep(SimState s, GameTickContext c) => Log.Add($"A{s.Tick}");
    }

    private sealed class Rig
    {
        public FakeClock Clock = new();
        public AppStateMachine App = new();
        public PresenceTracker Presence;
        public SimState State;
        public Sink Sink = new();
        public GameHost Host;

        public Rig(bool quiet = true, bool visible = true)
        {
            Presence = new PresenceTracker(Clock);
            State = SimState.CreateInitial(Content,
                TestLayouts.FromCenters(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 380f }), Rng.Seed(42, 54));
            if (quiet)
            {
                State.StartWave(Ctx);
                State.WaveClock = -100000f;
            }

            Host = new GameHost(Clock, App, Presence, State, Ctx, Sink);
            if (visible) App.LeaveHidden(HiddenReason.Starting);
        }

        public void Run(double seconds, double step = 1.0 / 60)
        {
            long n = (long)Math.Round(seconds / step);
            long inc = (long)(step * Clock.TicksPerSecond);
            for (long i = 0; i < n; i++) { Clock.AdvanceTicks(inc); Host.OnWake(Clock); }
        }
    }

    [Fact(DisplayName = "CA-10.2 10 s de relógio = 300 ticks reais")]
    public void TenSecondsIs300Ticks()
    {
        var r = new Rig(quiet: false);
        r.Host.OnWake(r.Clock);
        r.Run(10);
        Assert.Equal(300, r.State.Tick);
        Assert.Equal(300, r.Host.TicksExecuted);
    }

    [Fact(DisplayName = "CA-10.2 salto de 5 s executa no máximo 8 ticks e zera o acumulador")]
    public void BigJumpCapped()
    {
        var r = new Rig();
        r.Host.OnWake(r.Clock);
        r.Clock.AdvanceTicks(5 * 30_000_000L);
        r.Host.OnWake(r.Clock);
        Assert.InRange(r.State.Tick, 1, 8);
        r.Host.OnWake(r.Clock);
        Assert.InRange(r.State.Tick, 1, 8);
    }

    [Fact(DisplayName = "Ondas automáticas: sem comando, inimigos nascem e a onda avança")]
    public void AutoWavesSpawn()
    {
        var r = new Rig(quiet: false);
        r.Host.OnWake(r.Clock);
        r.Run(20);
        Assert.NotNull(r.State.ActivePlan);
        Assert.True(r.State.NextSpawnIndex > 0 || r.State.Enemies.Count > 0 || r.State.Wave > 1);
    }

    [Fact(DisplayName = "Pausa User e Dialog congelam ticks; retomar descarta o tempo parado")]
    public void PauseFreezes()
    {
        var r = new Rig();
        r.Host.OnWake(r.Clock);
        r.Run(1);
        long t = r.State.Tick;
        r.App.SetPause(PauseReason.User);
        r.Run(10);
        Assert.Equal(t, r.State.Tick);
        r.App.ClearPause(PauseReason.User);
        r.Host.OnWake(r.Clock);   // origem nova
        Assert.Equal(t, r.State.Tick);
        r.Run(1);
        Assert.InRange(r.State.Tick - t, 29, 30);
        r.App.SetPause(PauseReason.Dialog);
        long t2 = r.State.Tick;
        r.Run(2);
        Assert.Equal(t2, r.State.Tick);
    }

    [Fact(DisplayName = "Oculto não roda ticks nem renderiza; ao exibir retoma sem pausa de usuário")]
    public void HiddenStops()
    {
        var r = new Rig();
        r.Host.OnWake(r.Clock);
        r.Run(1);
        r.App.EnterHidden(HiddenReason.User);
        long t = r.State.Tick; int p = r.Sink.Count;
        r.Run(10);
        Assert.Equal(t, r.State.Tick);
        Assert.Equal(p, r.Sink.Count);
        Assert.Equal(PresenceState.Offline, r.Presence.State);
        r.App.LeaveHidden(HiddenReason.User);
        r.Run(1);
        Assert.True(r.State.Tick > t);
    }

    [Fact(DisplayName = "Comandos aplicados no início do wake, antes dos ticks, e resultado devolvido")]
    public void CommandsBeforeTicks()
    {
        var r = new Rig();
        var hook = new Hook();
        r.Host.AddHook(hook);
        r.Host.OnWake(r.Clock);
        r.State.Credits = 100000;
        r.Host.OnCommand(new BuildTower(TowerType.Bolt, TestLayouts.X(0)));
        Assert.Equal(0, r.State.Towers.Count);
        r.Clock.AdvanceTicks(1_000_000);   // 33 ms
        r.Host.OnWake(r.Clock);
        Assert.Equal(1, r.State.Towers.Count);
        Assert.True(r.Host.TryDequeueResult(out _, out var res));
        Assert.True(res.IsOk);
    }

    [Fact(DisplayName = "LayoutChanged é aplicado entre ticks")]
    public void LayoutBetweenTicks()
    {
        var r = new Rig();
        r.Host.OnWake(r.Clock);
        var l = TestLayouts.FromCenters(900f, 94f, 10f, 880f, new[] { 200f });
        r.Host.OnCommand(new LayoutChanged(l));
        r.Run(0.1);
        Assert.Same(l, r.State.Layout);
    }

    [Fact(DisplayName = "Hooks rodam antes e depois de cada Step, em ordem")]
    public void HooksOrder()
    {
        var r = new Rig();
        var hook = new Hook();
        r.Host.AddHook(hook);
        r.Host.OnWake(r.Clock);
        r.Run(0.2);
        Assert.Equal(["B0", "A1", "B1", "A2", "B2", "A3"], hook.Log.Take(6));
        Assert.Equal(r.Host.TicksExecuted * 2, hook.Log.Count);
    }

    [Fact(DisplayName = "PresenceMultiplier da presença escrito em SimState antes do tick")]
    public void PresenceMultiplierWritten()
    {
        var r = new Rig();
        r.Host.OnWake(r.Clock);
        r.Presence.InteractionObserved(r.Clock.NowTicks);
        r.Run(0.2);
        Assert.Equal(PresenceState.Presente, r.Presence.State);
        Assert.Equal(r.Presence.Multiplier(), r.State.PresenceMultiplier);
        Assert.Equal(1.5, r.State.PresenceMultiplier);
        // Ausente (>300 s sem interação) → NotPresent
        r.Clock.AdvanceTicks(301 * 30_000_000L);
        r.Host.OnWake(r.Clock);
        r.Run(0.2);
        Assert.Equal(PresenceState.Ausente, r.Presence.State);
        Assert.Equal(0.25, r.State.PresenceMultiplier);
    }

    [Fact(DisplayName = "CA-10.3 pausado (User): 0 Presents em 30 s")]
    public void PausedNoPresents()
    {
        var r = new Rig();
        r.Host.OnWake(r.Clock);
        r.App.SetPause(PauseReason.User);
        r.Host.OnWake(r.Clock);
        int p = r.Sink.Count;
        r.Run(30, 0.05);
        Assert.Equal(p, r.Sink.Count);
    }

    [Fact(DisplayName = "CA-10.3 reducedMotion sem entidades em movimento: 0 Presents em 30 s")]
    public void ReducedMotionIdleNoPresents()
    {
        var r = new Rig { };
        r.Host.AnimationsEnabled = false;
        r.Host.OnWake(r.Clock);
        r.Run(0.1);
        int p = r.Sink.Count;
        r.Run(30, 0.05);
        Assert.Equal(p, r.Sink.Count);
    }

    [Fact(DisplayName = "CA-10.3 Presente 30 fps, Ausente 8 fps, Offline 0")]
    public void RenderRatesByPresence()
    {
        var r = new Rig();
        r.Presence.VisibilityChanged(true, r.Clock.NowTicks, byUser: true);
        r.Host.OnWake(r.Clock);
        r.Run(0.5);
        int p0 = r.Sink.Count;
        r.Run(10, 1.0 / 120);
        Assert.InRange(r.Sink.Count - p0, 295, 305);

        r.Clock.AdvanceTicks(301 * 30_000_000L);
        r.Host.OnWake(r.Clock);
        r.Run(0.5);
        Assert.Equal(PresenceState.Ausente, r.Presence.State);
        int p1 = r.Sink.Count;
        r.Run(10, 1.0 / 120);
        Assert.InRange(r.Sink.Count - p1, 78, 82);

        r.App.EnterHidden(HiddenReason.User);
        r.Host.OnWake(r.Clock);
        int p2 = r.Sink.Count;
        r.Run(10);
        Assert.Equal(p2, r.Sink.Count);
    }

    [Fact(DisplayName = "NextDeadline: próximo tick e fim das pausas")]
    public void Deadlines()
    {
        var r = new Rig();
        r.Host.OnWake(r.Clock);
        long d = r.Host.NextDeadline!.Value;
        Assert.InRange(d - r.Clock.NowTicks, 0, 1_000_000L + 1);
        r.App.SetPause(PauseReason.User);
        r.Host.OnWake(r.Clock);
        long? d2 = r.Host.NextDeadline;
        Assert.True(d2 is null || d2 > r.Clock.NowTicks);
    }

    [Fact(DisplayName = "Shutdown ignora comandos e wakes seguintes")]
    public void ShutdownIsFinal()
    {
        var r = new Rig();
        r.Host.OnWake(r.Clock);
        r.Host.Shutdown();
        r.Host.OnCommand(new BuildTower(TowerType.Bolt, TestLayouts.X(0)));
        r.Run(1);
        Assert.Equal(0, r.State.Tick);
        Assert.Null(r.Host.NextDeadline);
    }
}
