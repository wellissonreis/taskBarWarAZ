using SideWar.Core.Content;
using SideWar.Core.Events;
using SideWar.Core.Simulation;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class EventSchedulerTests
{
    private const double Dt = 0.05;
    private static readonly CrateBounds Bounds = new(380f, 980f);

    /// <summary>Rng de teste que registra a ordem de consumo (limite exclusivo) e devolve valores programados.</summary>
    private sealed class ScriptRng(params int[] values) : IQuestionRng
    {
        private int _i;
        public List<int> Limits { get; } = new();
        public int NextInt(int exclusiveMax)
        {
            Limits.Add(exclusiveMax);
            int v = _i < values.Length ? values[_i++] : 0;
            return v >= exclusiveMax ? exclusiveMax - 1 : v;
        }
    }

    private static void Run(EventScheduler s, double seconds, bool present, IQuestionRng rng, List<SchedulerEvent>? log = null)
    {
        int n = (int)Math.Round(seconds / Dt);
        for (int i = 0; i < n; i++)
        {
            s.Advance(Dt, present, Bounds, rng);
            log?.AddRange(s.Events);
        }
    }

    [Fact]
    public void Start_DrawsPeakThenCrateInterval_InRanges()
    {
        var rng = new ScriptRng(0, 0);
        var s = EventScheduler.Start(rng);
        Assert.Equal(2, rng.Limits.Count);
        Assert.Equal(421, rng.Limits[0]);
        Assert.Equal(181, rng.Limits[1]);
        Assert.Equal(480, s.PeakCountdown);
        Assert.Equal(180, s.CrateCountdown);
        var rng2 = new ScriptRng(int.MaxValue, int.MaxValue);
        var s2 = EventScheduler.Start(rng2);
        Assert.Equal(900, s2.PeakCountdown);
        Assert.Equal(360, s2.CrateCountdown);
    }

    [Fact]
    public void Peak_OfferLastsThirtySeconds_ExtrasAtMultiplesOfTwelve_IgnoredNoBonus()
    {
        var rng = new ScriptRng(0, 1000); // pico 480 s, caixa 360 s
        var s = EventScheduler.Start(rng);
        var log = new List<SchedulerEvent>();
        Run(s, 479.9, true, rng, log);
        Assert.False(s.PeakOffered);
        Run(s, 0.1, true, rng, log);
        Assert.True(s.PeakOffered);
        double tOffer = 480;
        // extras: contar o tempo em que cada um nasce
        var spawnTimes = new List<double>();
        var s2 = EventScheduler.Start(new ScriptRng(0, 1000));
        var r2 = new ScriptRng(0, 0, 0);
        double t = 0; bool offered = false; double tOff = 0;
        for (int i = 0; i < 20000 && spawnTimes.Count < 6; i++)
        {
            s2.Advance(Dt, true, Bounds, r2); t += Dt;
            foreach (var e in s2.Events)
            {
                if (e.Kind == SchedulerEventKind.PeakOffered) { offered = true; tOff = t; }
                if (e.Kind == SchedulerEventKind.PeakExtraSpawn) spawnTimes.Add(t - tOff);
            }
        }
        Assert.True(offered);
        Assert.Equal(6, spawnTimes.Count);
        for (int i = 0; i < 6; i++) Assert.InRange(spawnTimes[i], i * 12.0 - 0.001, i * 12.0 + Dt + 0.001);
        // ignorar: some em 30 s, sem bônus, extras seguem
        Run(s, 29.9, true, rng, log);
        Assert.True(s.PeakOffered);
        Run(s, 0.1, true, rng, log);
        Assert.False(s.PeakOffered);
        Assert.False(s.BonusActive);
        Assert.Equal(1.0, s.DamageFactor);
        Assert.Equal(0, s.PeaksClicked);
        Assert.Contains(log, e => e.Kind == SchedulerEventKind.PeakExpired);
        Run(s, 40, true, rng, log);
        Assert.Equal(6, log.Count(e => e.Kind == SchedulerEventKind.PeakExtraSpawn));
        Assert.Equal(0, s.ExtrasPending);
        Assert.InRange(tOffer, 480, 480);
    }

    [Fact]
    public void Peak_Click_ActivatesTripleDamageForSixtySeconds_AndRaisesSimDamage()
    {
        var content = ContentLoader.LoadEmbedded();
        var layout = TestLayouts.FromCenters(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 380f });
        var sim = SimState.CreateInitial(content, layout, Rng.Seed(42, 54));
        var rng = new ScriptRng(0, 1000);
        var s = EventScheduler.Start(rng);
        Run(s, 480, true, rng);
        Run(s, 10, true, rng);
        Assert.True(s.ClickPeak());
        Assert.False(s.ClickPeak()); // não clicável de novo
        Assert.True(s.BonusActive);
        Assert.Equal(1, s.PeaksClicked);
        s.ApplyTo(sim);
        Assert.Equal(3.0, sim.PeakDamageFactor);
        Run(s, 59.8, true, rng);
        Assert.True(s.BonusActive);
        Run(s, 0.2, true, rng);
        Assert.False(s.BonusActive);
        s.ApplyTo(sim);
        Assert.Equal(1.0, sim.PeakDamageFactor);
        Assert.InRange(s.PeakCountdown, 480, 900);
    }

    [Fact]
    public void PeakInterval_NotDecreasedWhenAbsent_AndBonusEndsOnAbsence()
    {
        var rng = new ScriptRng(0, 1000, 5, 0);
        var s = EventScheduler.Start(rng);
        Run(s, 100, true, rng);
        double before = s.PeakCountdown;
        Run(s, 500, false, rng);
        Assert.Equal(before, s.PeakCountdown, 6);
        Assert.Equal(2, rng.Limits.Count); // sem sorteio em Ausente

        Run(s, before, true, rng);
        Assert.True(s.PeakOffered);
        s.ClickPeak();
        Assert.True(s.BonusActive);
        int n0 = rng.Limits.Count;
        s.Advance(Dt, false, Bounds, rng);
        Assert.False(s.BonusActive);
        Assert.Equal(1.0, s.DamageFactor);
        Assert.Equal(n0, rng.Limits.Count); // sem sorteio em Ausente
        s.Advance(Dt, true, Bounds, rng);
        Assert.True(rng.Limits.Count > n0); // sorteios só ao voltar
        Assert.InRange(s.PeakCountdown, 479, 900);
    }

    [Fact]
    public void AbsentWithExtrasQueued_ExtrasStillSpawn_WithoutBonus()
    {
        var rng = new ScriptRng(0, 1000);
        var s = EventScheduler.Start(rng);
        Run(s, 480, true, rng);
        Assert.True(s.PeakOffered);
        Run(s, 5, false, rng);
        Assert.False(s.PeakOffered);
        var log = new List<SchedulerEvent>();
        Run(s, 70, false, rng, log);
        Assert.Equal(0, s.ExtrasPending);
        Assert.False(s.BonusActive);
    }

    [Fact]
    public void Boss_DiscardsOffer_NoMission_ExtrasRemain()
    {
        var rng = new ScriptRng(0, 1000, 0, 7);
        var s = EventScheduler.Start(rng);
        Run(s, 480, true, rng);
        Run(s, 13, true, rng);
        Assert.True(s.PeakOffered);
        int pending = s.ExtrasPending;
        s.BossBannerShown(rng);
        Assert.Contains(s.Events, e => e.Kind == SchedulerEventKind.PeakDiscarded);
        Assert.False(s.PeakOffered);
        Assert.True(s.BannerVisible);
        Assert.Equal(0, s.PeaksClicked);
        Assert.False(s.ClickPeak());
        Assert.Equal(pending, s.ExtrasPending);
    }

    [Fact]
    public void Boss_ActiveBonusContinues_AndDuePeakAppearsAfterBoss()
    {
        var rng = new ScriptRng(0, 1000, 0, 0);
        var s = EventScheduler.Start(rng);
        Run(s, 480, true, rng);
        s.ClickPeak();
        s.BossBannerShown(rng);
        Assert.True(s.BonusActive);
        Assert.Equal(3.0, s.DamageFactor);
        Run(s, 60, true, rng); // bônus termina, novo intervalo sorteado (480) com chefe ativo
        Assert.False(s.BonusActive);
        Run(s, 480, true, rng);
        Assert.True(s.PeakDue);
        Assert.False(s.PeakOffered); // nunca 2 banners
        Run(s, 100, true, rng);
        Assert.False(s.PeakOffered);
        s.BossResolved(true);
        Assert.True(s.PeakOffered);
        Assert.False(s.PeakDue);
    }

    [Fact]
    public void DuePeak_OfferedOnReturnIfBossCancelledInAbsence()
    {
        var rng = new ScriptRng(0, 1000, 0, 0);
        var s = EventScheduler.Start(rng);
        s.BossBannerShown(rng);
        Run(s, 480, true, rng);
        Assert.True(s.PeakDue);
        s.BossResolved(false);
        Run(s, 10, false, rng);
        Assert.False(s.PeakOffered);
        Run(s, 1, true, rng);
        Assert.True(s.PeakOffered);
    }

    [Fact]
    public void Crate_SpawnsInRange_Lasts45s_NoWarning_MaxOne_AndCollectGain()
    {
        // pico 900 (longe), caixa 180; x = índice 0 → lo
        var rng = new ScriptRng(1000, 0, 0, 5, 0, 0);
        var s = EventScheduler.Start(rng);
        Run(s, 179.9, true, rng);
        Assert.False(s.CrateActive);
        Run(s, 0.1, true, rng);
        Assert.True(s.CrateActive);
        Assert.Equal(380 + 24, s.CrateX);
        Run(s, 44.8, true, rng);
        Assert.True(s.CrateActive);
        Run(s, 0.2, true, rng);
        Assert.False(s.CrateActive);
        // próxima: sorteio de intervalo na expiração (5 → 185 s)
        Assert.Equal(185, s.CrateCountdown, 6);
        Run(s, 185, true, rng);
        Assert.True(s.CrateActive);
        var gain = s.ClickCrate(0.5, rng);
        Assert.Equal(50.0, gain); // max(50, 30)
        Assert.Equal(1, s.CratesCollected);
        Assert.False(s.CrateActive);
        Assert.Null(s.ClickCrate(1, rng));
    }

    [Fact]
    public void Crate_GainIsOneMinuteOfIncome_WhenAboveMinimum()
    {
        var rng = new ScriptRng(1000, 0);
        var s = EventScheduler.Start(rng);
        Run(s, 180, true, rng);
        Assert.Equal(120.0, s.ClickCrate(2.0, rng));
    }

    [Fact]
    public void Crate_X_UpperBoundAndNarrowRangeMidpoint()
    {
        var rng = new ScriptRng(1000, 0, int.MaxValue);
        var s = EventScheduler.Start(rng);
        Run(s, 180, true, rng);
        Assert.Equal(980 - 24, s.CrateX);

        var rng2 = new ScriptRng(1000, 0, 3);
        var s2 = EventScheduler.Start(rng2);
        var narrow = new CrateBounds(400f, 440f); // 40 < 48 → ponto médio 420
        for (int i = 0; i < 3600; i++) s2.Advance(Dt, true, narrow, rng2);
        Assert.True(s2.CrateActive);
        Assert.Equal(420, s2.CrateX);
        Assert.Equal(2, rng2.Limits.Count); // ponto médio não consome o Rng
    }

    [Fact]
    public void Crate_RemovedWhenAbsent_AndLayoutChangeClampsX()
    {
        var rng = new ScriptRng(1000, 0, 999, 0);
        var s = EventScheduler.Start(rng);
        Run(s, 180, true, rng);
        Assert.True(s.CrateActive);
        int x = s.CrateX;
        s.OnLayoutChanged(new CrateBounds(380f, 600f));
        Assert.Equal(600 - 24, s.CrateX);
        Assert.True(s.CrateX < x);
        s.Advance(Dt, false, Bounds, rng);
        Assert.False(s.CrateActive);
    }

    [Fact]
    public void RngConsumptionOrder_PeakBeforeCrate_AndXAfterCrateInterval()
    {
        var rng = new ScriptRng(0, 0, 0);
        var s = EventScheduler.Start(rng);
        Run(s, 180, true, rng);
        // 421 (pico), 181 (caixa), 601 (x: 980-24 - 404 + 1)
        Assert.Equal(3, rng.Limits.Count);
        Assert.Equal(421, rng.Limits[0]);
        Assert.Equal(181, rng.Limits[1]);
        Assert.Equal(553, rng.Limits[2]);
    }

    [Fact]
    public void SnapshotRestore_ContinuesIdentically()
    {
        var a = new ScriptRng(0, 40, 3, 9, 4, 2, 1, 1);
        var s = EventScheduler.Start(a);
        Run(s, 200, true, a);
        var snap = s.Snapshot();
        var b = new ScriptRng(3, 9, 4, 2, 1, 1);
        var c = new ScriptRng(3, 9, 4, 2, 1, 1);
        var s1 = EventScheduler.Restore(snap);
        var s2 = EventScheduler.Restore(snap);
        var l1 = new List<SchedulerEvent>(); var l2 = new List<SchedulerEvent>();
        Run(s1, 700, true, b, l1);
        Run(s2, 700, true, c, l2);
        Assert.Equal(l1, l2);
        Assert.Equal(s1.Snapshot(), s2.Snapshot());
    }

    [Fact]
    public void SimRngAdapter_IsDeterministicAndConsumesSimRng()
    {
        var content = ContentLoader.LoadEmbedded();
        var layout = TestLayouts.FromCenters(1000f, 94f, 10f, 980f, new[] { 200f, 260f });
        SimState Make() => SimState.CreateInitial(content, layout, Rng.Seed(42, 54));
        var a = Make(); var b = Make();
        var ra = new SimRngAdapter(a); var rb = new SimRngAdapter(b);
        var sa = EventScheduler.Start(ra); var sb = EventScheduler.Start(rb);
        Assert.Equal(sa.PeakCountdown, sb.PeakCountdown);
        Assert.InRange(sa.PeakCountdown, 480, 900);
        Assert.InRange(sa.CrateCountdown, 180, 360);
        Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
        Assert.NotEqual(Make().ComputeStateHash(), a.ComputeStateHash());
    }

    [Fact]
    public void Statistical_Draws_StayInRangeOverSeeds()
    {
        var content = ContentLoader.LoadEmbedded();
        var layout = TestLayouts.FromCenters(1000f, 94f, 10f, 980f, new[] { 200f, 260f });
        for (ulong seed = 0; seed < 300; seed++)
        {
            var sim = SimState.CreateInitial(content, layout, Rng.Seed(seed, 54));
            var s = EventScheduler.Start(new SimRngAdapter(sim));
            Assert.InRange(s.PeakCountdown, 480, 900);
            Assert.InRange(s.CrateCountdown, 180, 360);
        }
    }
}
