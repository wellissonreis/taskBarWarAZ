using SideWar.Core;
using SideWar.Core.Simulation;
using SideWar.Runtime.Composition;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Hosting;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Power;
using SideWar.Runtime.Presence;
using SideWar.Runtime.Taskbar;
using Xunit;

namespace SideWar.Runtime.Tests.Composition;

/// <summary>T4.10: grafo headless da raiz de composição, RenderPolicy por presença com relógio fake e ordem de início/fim.</summary>
public sealed class CompositionTests
{
    private sealed class CountingSink : IGameFrameSink
    {
        public int Count;
        public void Present(SimState state, double presentationSeconds) => Count++;
    }

    private sealed class RecordingEffects : IAppEffectHandler
    {
        public readonly List<string> Log = [];
        public void Show(HiddenReason cause) => Log.Add("Show:" + cause);
        public void Hide(HiddenReason cause) => Log.Add("Hide:" + cause);
        public void Save(SaveTrigger trigger) => Log.Add("Save:" + trigger);
        public void ReleaseCaches() => Log.Add("ReleaseCaches");
        public void RestoreContext() => Log.Add("RestoreContext");
        public void SetEcoQos(bool enabled) => Log.Add("EcoQos:" + enabled);
    }

    private sealed record AppCommand(string Name) : IGameCommand;

    private sealed class Rig
    {
        public readonly FakeClock Clock = new();
        public readonly CountingSink Sink = new();
        public readonly RecordingEffects Effects = new();
        public readonly GameRuntime Runtime;
        public readonly CompositeLoopHost Loop;
        public readonly List<string> Intercepted = [];

        public Rig(bool reducedMotion = false)
        {
            Runtime = new GameRuntime(new GameRuntimeOptions
            {
                Clock = Clock, FrameSink = Sink, ReducedMotion = reducedMotion, UtcNow = () => new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc),
            }) { Effects = Effects };
            Loop = new CompositeLoopHost(Runtime.Host);
            Loop.AddBefore(Runtime);
            Loop.Intercept = c => { if (c is AppCommand a) { Intercepted.Add(a.Name); return true; } return false; };
        }

        /// <summary>Simula a T-Game: acorda no próximo deadline (ou a cada passo fixo) até <paramref name="seconds"/>.</summary>
        public void Run(double seconds)
        {
            long end = Clock.NowTicks + (long)(seconds * Clock.TicksPerSecond);
            long maxStep = (long)(Clock.TicksPerSecond / 240);
            while (Clock.NowTicks < end)
            {
                long next = Loop.NextDeadline is { } d ? Math.Max(d, Clock.NowTicks + 1) : end;
                Clock.AdvanceTicks(Math.Min(Math.Min(next, end), Clock.NowTicks + maxStep) - Clock.NowTicks);
                Loop.OnWake(Clock);
            }
        }

        public int PresentsOver(double seconds)
        {
            int before = Sink.Count;
            Run(seconds);
            return Sink.Count - before;
        }
    }

    [Fact(DisplayName = "T4.10 grafo headless: simulação, host, presença e layout montados sem janelas")]
    public void GraphBuildsHeadless()
    {
        var r = new Rig();
        Assert.Same(r.Runtime.State, r.Runtime.Host.State);
        Assert.False(r.Runtime.App.IsVisible); // Starting
        Assert.Equal(SideWar.Core.Simulation.TowerHost.Band, r.Runtime.State.Layout.Host); // towersInBar = false por padrão
        Assert.Same(r.Runtime.State.Layout, r.Runtime.LayoutWatcher.CurrentLayout);
        Assert.True(r.Runtime.Host.AnimationsEnabled);
        Assert.Equal(30, r.Runtime.Host.ActiveFps);
        Assert.Equal(8, r.Runtime.Host.AbsentFps);
    }

    [Fact(DisplayName = "T4.10 CompleteStartup emite RestoreContext e Show na ordem da máquina")]
    public void StartupShowsThroughEffects()
    {
        var r = new Rig();
        r.Runtime.CompleteStartup();
        Assert.True(r.Runtime.App.IsVisible);
        Assert.Contains("RestoreContext", r.Effects.Log);
        Assert.Equal("Show:Starting", r.Effects.Log.Single(l => l.StartsWith("Show", StringComparison.Ordinal)));
        Assert.True(r.Effects.Log.IndexOf("RestoreContext") < r.Effects.Log.IndexOf("Show:Starting"));
    }

    [Fact(DisplayName = "CA-10.3 Presente 30 fps, Ausente 8 fps, oculto 0 (FakeClock, laço composto)")]
    public void RenderPolicyFollowsPresence()
    {
        var r = new Rig();
        r.Runtime.CompleteStartup();
        r.Run(0.1);
        r.Runtime.NotifyInteraction();
        r.Run(0.5);
        Assert.Equal(PresenceState.Presente, r.Runtime.Presence.State);
        Assert.InRange(r.PresentsOver(10), 298, 304);

        r.Run(PresenceTracker.AbsentAfterSeconds); // sem interação → Ausente
        Assert.Equal(PresenceState.Ausente, r.Runtime.Presence.State);
        Assert.InRange(r.PresentsOver(10), 79, 83);

        r.Runtime.ToggleVisibilityByUser(); // oculto = Offline
        r.Run(0.1);
        Assert.Equal(PresenceState.Offline, r.Runtime.Presence.State);
        Assert.Equal(0, r.PresentsOver(30));
    }

    [Fact(DisplayName = "T4.10 interação em Ausente volta a 30 fps")]
    public void InteractionRestoresActiveFps()
    {
        var r = new Rig();
        r.Runtime.CompleteStartup();
        r.Run(0.1);
        Assert.Equal(PresenceState.Ausente, r.Runtime.Presence.State); // início sem interação (SDD-25)
        Assert.InRange(r.PresentsOver(10), 79, 83);
        r.Runtime.NotifyInteraction();
        r.Run(0.2);
        Assert.InRange(r.PresentsOver(10), 298, 304);
    }

    [Fact(DisplayName = "CA-08.4 pausado (User): 0 Presents em 30 s sem eventos")]
    public void PausedHasNoPresents()
    {
        var r = new Rig();
        r.Runtime.CompleteStartup();
        r.Runtime.NotifyInteraction();
        r.Run(0.5);
        r.Runtime.TogglePause();
        r.Run(0.5); // frame da mudança de estado
        Assert.Equal(0, r.PresentsOver(30));
    }

    [Fact(DisplayName = "T4.10 Saver limita a 15 fps e desliga animações; Normal volta a 30")]
    public void SaverCapsFps()
    {
        var r = new Rig();
        r.Runtime.CompleteStartup();
        r.Runtime.NotifyInteraction();
        r.Run(0.5);
        r.Runtime.SetPowerProfile(PowerProfile.Saver);
        Assert.False(r.Runtime.Host.AnimationsEnabled);
        Assert.Contains("EcoQos:True", r.Effects.Log);
        r.Run(0.5);
        Assert.InRange(r.PresentsOver(10), 0, 152);
        r.Runtime.SetPowerProfile(PowerProfile.Normal);
        Assert.True(r.Runtime.Host.AnimationsEnabled);
    }

    [Fact(DisplayName = "RF-07.3 Hide salva e agenda ReleaseCaches para 60 s; Show cancela")]
    public void HideSchedulesReleaseCaches()
    {
        var r = new Rig();
        r.Runtime.CompleteStartup();
        r.Runtime.ToggleVisibilityByUser();
        Assert.Contains("Hide:User", r.Effects.Log);
        Assert.Contains("Save:Hidden", r.Effects.Log);
        r.Run(59);
        Assert.DoesNotContain("ReleaseCaches", r.Effects.Log);
        r.Run(2);
        Assert.Single(r.Effects.Log, "ReleaseCaches");

        r.Runtime.ToggleVisibilityByUser();
        r.Runtime.ToggleVisibilityByUser();
        r.Runtime.ToggleVisibilityByUser(); // Hide → Show cancela o timer
        r.Run(120);
        Assert.Single(r.Effects.Log, "ReleaseCaches");
    }

    [Fact(DisplayName = "T4.10 comandos do App são interceptados; os da simulação chegam ao GameHost entre ticks")]
    public void CommandsRouted()
    {
        var r = new Rig();
        r.Runtime.CompleteStartup();
        r.Loop.OnCommand(new AppCommand("ToggleInteractive"));
        Assert.Equal(["ToggleInteractive"], r.Intercepted);
        r.Loop.OnCommand(new BuildTower(TowerType.Bolt, 300));
        r.Run(0.1);
        Assert.True(r.Runtime.Host.TryDequeueResult(out var cmd, out _));
        Assert.IsType<BuildTower>(cmd);
    }

    [Fact(DisplayName = "T4.10 barra observada vira LayoutChanged aplicado entre ticks")]
    public void TaskbarLayoutReachesSimulation()
    {
        var r = new Rig();
        r.Runtime.CompleteStartup();
        var monitor = new MonitorInfo(@"\\.\DISPLAY1", new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1392), 96, true, 0);
        var update = r.Runtime.ObserveLayout(monitor, default, new BuildZoneOptions());
        Assert.NotNull(update);
        Assert.True(update.Value.LayoutPosted);
        r.Run(0.1);
        Assert.Equal(2560, r.Runtime.State.Layout.Width);
        Assert.Null(r.Runtime.ObserveLayout(monitor, default, new BuildZoneOptions())); // nada mudou
    }

    [Fact(DisplayName = "T4.10 Shutdown: laço lógico primeiro, recursos em ordem reversa, falhas isoladas")]
    public void ShutdownOrder()
    {
        var r = new Rig();
        var order = new List<string>();
        var errors = new List<Exception>();
        var loop = new CompositeLoopHost(r.Runtime.Host, errors.Add);
        loop.Resources.Started("windows", () => order.Add("windows"));
        loop.Resources.Started("hotkeys", () => throw new InvalidOperationException("boom"));
        loop.Resources.Started("taskbar-hook", () => order.Add("taskbar-hook"));
        loop.Shutdown();
        loop.Shutdown();
        Assert.True(r.Runtime.Host.IsShutdown);
        Assert.Equal(["taskbar-hook", "windows"], order);
        Assert.Single(errors);
        Assert.Null(loop.NextDeadline);
        loop.OnCommand(new BuildTower(TowerType.Bolt, 300)); // rejeitado sem lançar
    }

    [Fact(DisplayName = "T4.10 LifecycleSequence: início em ordem, parada reversa, falha no início desfaz")]
    public void LifecycleOrder()
    {
        var seq = new LifecycleSequence();
        var log = new List<string>();
        seq.Add("settings", () => log.Add("+settings"), () => log.Add("-settings"))
           .Add("game-thread", () => log.Add("+game-thread"), () => log.Add("-game-thread"))
           .Add("tray", () => log.Add("+tray"), () => log.Add("-tray"));
        seq.StartAll();
        Assert.Empty(seq.StopAll());
        Assert.Equal(["+settings", "+game-thread", "+tray", "-tray", "-game-thread", "-settings"], log);

        var failing = new LifecycleSequence();
        log.Clear();
        failing.Add("a", () => log.Add("+a"), () => log.Add("-a"))
               .Add("b", () => throw new InvalidOperationException("b"), () => log.Add("-b"))
               .Add("c", () => log.Add("+c"), () => log.Add("-c"));
        Assert.Throws<InvalidOperationException>(failing.StartAll);
        Assert.Equal(["+a", "-a"], log);
        Assert.Equal(["start:a", "fail:b", "stop:a"], failing.Journal);
    }
}
