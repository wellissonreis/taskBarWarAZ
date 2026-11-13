using SideWar.Core.Simulation;
using SideWar.Runtime.Composition;
using SideWar.Runtime.Diagnostics;
using SideWar.Runtime.Hosting;
using SideWar.Runtime.Presence;
using Xunit;

namespace SideWar.Runtime.Tests.Diagnostics;

/// <summary>T4.11: montagem e parametrização dos cenários do --bench com o SanityBot, sem criar janelas (FakeClock).</summary>
public sealed class BenchScenarioTests
{
    private sealed class CountingSink : IGameFrameSink
    {
        public int Count;
        public void Present(SimState state, double presentationSeconds) => Count++;
    }

    private sealed class Rig
    {
        public readonly FakeClock Clock = new();
        public readonly CountingSink Sink = new();
        public readonly BenchScenarioRig Bench;

        public Rig(string name)
        {
            Bench = BenchScenarioRig.Create(BenchScenarios.Find(name)!, Clock, Sink);
            Bench.Start();
        }

        public void Run(double seconds)
        {
            long end = Clock.NowTicks + (long)(seconds * Clock.TicksPerSecond);
            long maxStep = (long)(Clock.TicksPerSecond / 240);
            while (Clock.NowTicks < end)
            {
                long next = Bench.Loop.NextDeadline is { } d ? Math.Max(d, Clock.NowTicks + 1) : end;
                Clock.AdvanceTicks(Math.Min(Math.Min(next, end), Clock.NowTicks + maxStep) - Clock.NowTicks);
                Bench.Loop.OnWake(Clock);
            }
        }

        public int PresentsOver(double seconds)
        {
            int before = Sink.Count;
            Run(seconds);
            return Sink.Count - before;
        }
    }

    [Theory]
    [InlineData("presente-onda", "presente-onda-media")]
    [InlineData("strip-away", "ausente")]
    [InlineData("varias-janelas", "varias-janelas")]
    public void ParseAcceptsAliasesAndCanonicalizes(string given, string canonical)
    {
        var (o, e) = BenchOptions.Parse(["--bench", given, "--seconds", "5"]);
        Assert.Null(e);
        Assert.Equal(canonical, o!.Scenario);
        Assert.Equal($"bench-{canonical}.csv", o.OutPath);
        Assert.Contains(canonical, BenchOptions.Implemented);
        Assert.DoesNotContain(canonical, BenchOptions.Deferred);
    }

    [Fact]
    public void CatalogMatchesRf162()
    {
        var media = BenchScenarios.Find("presente-onda")!;
        Assert.Equal((BenchPresence.Present, 20, 6), (media.Presence, media.TargetEnemies, media.TargetTowers));
        var pesada = BenchScenarios.Find("presente-onda-pesada")!;
        Assert.Equal((BenchPresence.Present, 44, 12), (pesada.Presence, pesada.TargetEnemies, pesada.TargetTowers));
        Assert.True(pesada.Windows.HasFlag(BenchWindows.Banner));
        var ausente = BenchScenarios.Find("strip-away")!;
        Assert.Equal((BenchPresence.Away, 24, 12), (ausente.Presence, ausente.TargetEnemies, ausente.TargetTowers));
        Assert.True(ausente.Windows.HasFlag(BenchWindows.Seal));
        Assert.False(ausente.Windows.HasFlag(BenchWindows.Panel));
        var varias = BenchScenarios.Find("varias-janelas")!;
        Assert.Equal(BenchWindows.All, varias.Windows);
        Assert.All(BenchScenarios.Game, s => Assert.False(s.TowersInBar)); // faixa sobre a barra (PLANO §8.2): padrão Band
        Assert.Null(BenchScenarios.Find("hidden"));
        Assert.Null(BenchScenarios.Find(null));
    }

    [Theory]
    [InlineData("presente-onda-media", 20, 6)]
    [InlineData("presente-onda-pesada", 44, 12)]
    [InlineData("varias-janelas", 24, 12)]
    public void SanityBotBuildsTowersAndWaveHoldsPopulation(string name, int enemies, int towers)
    {
        var r = new Rig(name);
        r.Run(20);
        var state = r.Bench.Runtime.State;
        Assert.Equal(SideWar.Core.Simulation.TowerHost.Band, state.Layout.Host);
        Assert.InRange(state.Towers.Count, towers, towers + 1); // o bot pode concluir a rodada com uma torre a mais
        Assert.True(state.Enemies.Count >= Math.Min(enemies, 1));
        Assert.True(r.Bench.Driver.Spawned >= enemies);
        Assert.Equal(state.MaxBaseHp, state.BaseHp); // base invulnerável
        Assert.Equal(0, state.Falls);
    }

    [Fact]
    public void EnemyPopulationIsRefilledAfterKills()
    {
        var r = new Rig("presente-onda-media");
        r.Run(60);
        Assert.True(r.Bench.Driver.Spawned > 20, "inimigos mortos/vazados devem ser repostos pela onda infinita");
        Assert.InRange(r.Bench.Runtime.State.Enemies.Count, 20, 64);
    }

    [Fact]
    public void PresenteStaysPresentAt30FpsBeyondFiveMinutes()
    {
        var r = new Rig("presente-onda-media");
        r.Run(2);
        Assert.Equal(PresenceState.Presente, r.Bench.Runtime.Presence.State);
        Assert.InRange(r.PresentsOver(10), 296, 306);
        r.Run(PresenceTracker.AbsentAfterSeconds + 30); // a interação sintética impede a queda para Ausente
        Assert.Equal(PresenceState.Presente, r.Bench.Runtime.Presence.State);
        Assert.InRange(r.PresentsOver(10), 296, 306);
    }

    [Fact]
    public void AusenteIsForcedWithoutWaitingFiveMinutesAndRendersAt8Fps()
    {
        var r = new Rig("ausente");
        r.Run(2);
        Assert.Equal(PresenceState.Ausente, r.Bench.Runtime.Presence.State);
        Assert.InRange(r.PresentsOver(10), 79, 83); // 8 ± 1 por segundo (P-18)
        Assert.Equal(30, r.Bench.Runtime.Host.ActiveFps);
        Assert.True(r.Bench.Runtime.State.Tick > 0); // a simulação segue a 30 Hz
        r.Run(PresenceTracker.AbsentAfterSeconds + 30);
        Assert.Equal(PresenceState.Ausente, r.Bench.Runtime.Presence.State);
        Assert.InRange(r.Bench.Runtime.State.Towers.Count, 12, 13);
    }

    [Fact]
    public void RigAppliesBenchSettingsOnly()
    {
        var r = new Rig("varias-janelas");
        Assert.False(r.Bench.Runtime.Host.AutoScaleEnabled);
        Assert.Null(r.Bench.Runtime.Restored); // sem sessão do usuário
        Assert.Null(r.Bench.Runtime.Effects); // nada de plataforma na parte pura
    }
}
