using SideWar.Core.Content;
using SideWar.Core.Simulation;
using SideWar.Runtime.Presence;
using Xunit;

namespace SideWar.Runtime.Tests.Presence;

public sealed class OfflineCalculatorTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly DateTime T0 = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private static OfflineCalculator Calc(OfflineConfig? cfg = null) => new(Content, cfg);

    private static OfflineSnapshot Snap(
        int wave = 1, int stage = 1, bool auto = false, long credits = 0, double accum = 0, long reservas = 0,
        params OfflineTower[] towers)
        => new(T0, stage, wave, wave, "brazil-south", reservas, 1, 0, credits, 0, towers, auto, accum);

    private static OfflineTower Bolt(int id, int slot, int level = 1) => new(id, TowerType.Bolt, slot, level);

    private static OfflineResult Run(OfflineSnapshot s, double seconds, OfflineConfig? cfg = null)
        => Calc(cfg).Compute(s, s.SavedUtc.AddSeconds(seconds));

    [Fact]
    public void ZeroSeconds_NoGains_AndLogsSkew()
    {
        var r = Run(Snap(1, towers: Bolt(1, 0)), 0);
        Assert.Equal(0, r.Report.Creditos);
        Assert.Equal(1, r.Report.OndaPara);
        Assert.False(r.ShowCard);
        Assert.Contains("offline: clock skew", r.Logs);
    }

    [Fact]
    public void Negative_And_FarFutureSnapshot_NoGains_WithLog()
    {
        foreach (var secs in new[] { -1.0, -13 * 3600.0 })
        {
            var r = Run(Snap(5, towers: Bolt(1, 0)), secs);
            Assert.Equal(0, r.Report.Creditos);
            Assert.Equal(0, r.Report.TempoAcumuladoS);
            Assert.Contains("offline: clock skew", r.Logs);
        }
    }

    [Fact]
    public void FiveMinutes_GainsWithoutCard_TenMinutesShowCard()
    {
        var s = Snap(1, towers: Bolt(1, 0));
        var five = Run(s, 300);
        Assert.True(five.Report.Creditos > 0);
        Assert.False(five.ShowCard);
        Assert.False(Run(s, 599).ShowCard);
        Assert.True(Run(s, 600).ShowCard);
    }

    [Fact]
    public void Cap_30h_EqualsCap_12h()
    {
        var s = Snap(1, towers: Bolt(1, 0));
        var a = Run(s, 12 * 3600);
        var b = Run(s, 30 * 3600);
        Assert.Equal(12 * 3600, a.Report.TempoAcumuladoS);
        Assert.Equal(a.Report, b.Report with { MelhoriasAuto = a.Report.MelhoriasAuto });
        Assert.Equal(a.State.Credits, b.State.Credits);
    }

    [Fact]
    public void FirstWave_HandComputed()
    {
        // 6 grunts HP 42 = 252; duração 6×0,85+10 = 15,1; consumo 16,5; ouro 54×0,25 = 13,5; XP 30×0,25 = 7,5.
        var r = Run(Snap(1, towers: Bolt(1, 0)), 17);
        Assert.Equal(2, r.Report.OndaPara);
        Assert.Equal(1, r.Report.OndaDe);
        Assert.Equal(13, r.Report.Creditos); // 13,5 + 0,5/16,5×13,5 (farm da onda 1) = 13,9 → floor
        Assert.Equal(7, r.Report.Xp);
        Assert.False(r.Report.ParouNoChefe);
        Assert.Equal(0, r.Report.Quedas);
    }

    [Fact]
    public void ReservesMultiplyCredits_NotXp()
    {
        var a = Run(Snap(1, towers: Bolt(1, 0)), 16.6);
        var b = Run(Snap(1, reservas: 100, towers: Bolt(1, 0)), 16.6);
        Assert.Equal(27, b.Report.Creditos); // 13,5 × 2
        Assert.Equal(a.Report.Xp, b.Report.Xp);
    }

    [Fact]
    public void StopsAtBossWave_AndFarmsPreviousWave()
    {
        var r = Run(Snap(19, towers: new[] { Bolt(1, 0, 400), Bolt(2, 1, 400) }), 3600);
        Assert.Equal(20, r.Report.OndaPara);
        Assert.True(r.Report.ParouNoChefe);
        Assert.Equal(1, r.Report.PerdaChefes);
        Assert.Equal(20, r.State.WaveMax);
        Assert.True(r.Report.Creditos > 0);
    }

    [Fact]
    public void BossWave_AsStart_FarmsWaveBefore()
    {
        var r = Run(Snap(20, towers: new[] { Bolt(1, 0, 400) }), 600);
        Assert.Equal(20, r.Report.OndaPara);
        Assert.True(r.Report.ParouNoChefe);
        Assert.True(r.Report.Creditos > 0);
    }

    [Fact]
    public void StopsAtFirstUnwonWave_AndFarmsLastWon()
    {
        var r = Run(Snap(1, towers: Bolt(1, 0)), 12 * 3600);
        Assert.InRange(r.Report.OndaPara, 2, 19);
        Assert.False(r.Report.ParouNoChefe);
        Assert.Equal(0, r.Report.PerdaChefes);
        var calc = Calc();
        Assert.True(r.Report.Creditos > 0);
    }

    [Fact]
    public void NoTowers_NoGains_LogsAndStaysOnWave()
    {
        var r = Run(Snap(5), 3600);
        Assert.Equal(0, r.Report.Creditos);
        Assert.Equal(5, r.Report.OndaPara);
        Assert.Contains("offline: no wave won", r.Logs);
    }

    [Fact]
    public void TowerWithoutSlot_DoesNotCountForDps()
    {
        var withSlot = Run(Snap(1, towers: Bolt(1, 0)), 100);
        var noSlot = Run(Snap(1, towers: new OfflineTower(1, TowerType.Bolt, null, 1)), 100);
        Assert.True(withSlot.Report.Creditos > 0);
        Assert.Equal(0, noSlot.Report.Creditos);
    }

    [Fact]
    public void WonBoundary_EqualWins_JustBelowLoses()
    {
        var c = Calc();
        Assert.True(c.IsWon(10, 100, 800));
        Assert.False(c.IsWon(10, 100, 800.0000001));
        Assert.True(c.IsWon(10, 100, 799.9999999));
    }

    [Fact]
    public void WonBoundary_ThroughRealWave()
    {
        // 1 Raio nível 1: DPS = 18/0,7. Onda 1 vence (310,6 ≥ 252).
        var c = Calc();
        var dps = c.ComputeDps([Bolt(1, 0)], [1]);
        Assert.Equal(18 / 0.7, dps, 12);
        Assert.True(c.IsWon(dps, 6 * 0.85 + 10, 252));
        Assert.False(c.IsWon(dps, 6 * 0.85 + 10, 312));
    }

    [Fact]
    public void ScaleSet_UsesBoltBonus_AndNoSlotBoltDoesNotCount()
    {
        var c = Calc();
        var ss = new OfflineTower(9, TowerType.ScaleSet, 2, 1);
        var plain = c.ComputeDps([ss], [1]);
        Assert.Equal(14 / 0.7, plain, 12);
        var two = c.ComputeDps([Bolt(1, 0), Bolt(2, 1), ss], [1, 1, 1]);
        Assert.Equal(2 * 18 / 0.7 + 14 * 1.3 / 0.7, two, 12);
        var ghost = c.ComputeDps([new OfflineTower(1, TowerType.Bolt, null, 1), ss], [1, 1]);
        Assert.Equal(plain, ghost, 12);
    }

    [Fact]
    public void AutoScale_SpendsHalf_OnLowestCostTower_AndUnlocksNextWave()
    {
        var s = Snap(1, auto: true, credits: 100_000, towers: new[] { Bolt(1, 0), new OfflineTower(2, TowerType.Frost, 1, 1) });
        var on = Run(s, 3 * 3600);
        var off = Run(s with { AutoScaleOn = false }, 3 * 3600);
        Assert.True(on.Report.NMelhorias > 0);
        Assert.True(on.Report.GastoAuto > 0);
        Assert.True(on.Report.GastoAuto <= 100_000 + on.Report.Creditos);
        Assert.True(on.Report.OndaPara > off.Report.OndaPara);
        Assert.True(on.State.Credits >= 0);
        Assert.All(on.Report.MelhoriasAuto, u => Assert.True(u.ParaNivel > u.DeNivel));
    }

    [Fact]
    public void AutoScale_ExcessOverBalance_IsDeductedFromPending()
    {
        var s = Snap(1, auto: true, credits: 0, accum: 890, towers: Bolt(1, 0));
        var r = Run(s, 3 * 3600);
        Assert.True(r.Report.GastoAuto > 0);
        Assert.Equal(0, r.State.Credits);
        Assert.Equal(r.Report.Creditos - r.Report.GastoAuto, r.PendingCredits);
    }

    [Fact]
    public void AutoScale_Off_OrNoTowers_DoesNotSpend()
    {
        var off = Run(Snap(1, auto: false, credits: 1_000_000, towers: Bolt(1, 0)), 3600);
        Assert.Equal(0, off.Report.GastoAuto);
        Assert.Equal(1_000_000, off.State.Credits);
        var none = Run(Snap(1, auto: true, credits: 1_000_000), 3600);
        Assert.Equal(0, none.Report.GastoAuto);
    }

    [Fact]
    public void WaveLimit_ExcessTimeBecomesFarm()
    {
        var s = Snap(1, towers: new[] { Bolt(1, 0, 300), Bolt(2, 1, 300) });
        var r = Run(s, 12 * 3600, new OfflineConfig(MaxWaves: 3));
        Assert.Equal(4, r.Report.OndaPara);
        Assert.True(r.Report.Creditos > 0);
    }

    [Fact]
    public void LossEstimates_UseFloorOfMinutesOverDivisors()
    {
        var r = Run(Snap(1, towers: Bolt(1, 0)), 3 * 3600 + 12 * 60);
        Assert.Equal((int)Math.Floor(192 / 11.5), r.Report.PerdaPicos);
        Assert.Equal((int)Math.Floor(192 / 4.5), r.Report.PerdaCaixas);
        Assert.Equal(16, r.Report.PerdaPicos);
        Assert.Equal(42, r.Report.PerdaCaixas);
    }

    [Fact]
    public void Level_ProjectedFromPendingXp()
    {
        var s = Snap(1, towers: new[] { Bolt(1, 0, 400), Bolt(2, 1, 400) }) with { Level = 1, Xp = 40 };
        var r = Run(s, 2 * 3600);
        Assert.Equal(1, r.Report.NivelDe);
        Assert.True(r.Report.NivelPara > 1);
        Assert.Equal(40, r.State.Xp);
        Assert.Equal(1, r.State.Level);
        Assert.Equal(r.Report.Xp, r.PendingXp);
    }

    [Fact]
    public void Deterministic_SameInputSameOutput()
    {
        var s = Snap(7, auto: true, credits: 5000, towers: new[] { Bolt(1, 0, 5), new OfflineTower(2, TowerType.Cannon, 1, 3) });
        var a = Run(s, 7 * 3600 + 13);
        var b = Run(s, 7 * 3600 + 13);
        Assert.Equal(a.Report.Creditos, b.Report.Creditos);
        Assert.Equal(a.Report.Xp, b.Report.Xp);
        Assert.Equal(a.Report.OndaPara, b.Report.OndaPara);
        Assert.Equal(a.Report.GastoAuto, b.Report.GastoAuto);
        Assert.Equal(a.State.Carry, b.State.Carry);
        Assert.Equal(a.State.Towers.Select(t => t.Level), b.State.Towers.Select(t => t.Level));
    }

    [Fact]
    public void CarryFromSnapshot_IsConsumedAndReturned()
    {
        var s = Snap(1, towers: Bolt(1, 0)) with { Carry = 0.9 };
        var r = Run(s, 16.6);
        Assert.Equal(14, r.Report.Creditos); // 13,5 + 0,9 + 0,08 = 14,48
        Assert.InRange(r.State.Carry, 0.47, 0.49); // 13,5 + 0,9 + farm 0,08
    }

    [Fact]
    public void InvalidSnapshot_Throws()
    {
        Assert.Throws<ArgumentException>(() => Run(Snap(0), 10));
        Assert.Throws<ArgumentException>(() => Run(Snap(1, stage: 11), 10));
        Assert.Throws<ArgumentException>(() => Run(Snap(1) with { RegionId = "x" }, 10));
    }

    [Fact]
    public void Perf_12h_IsFast()
    {
        var s = Snap(1, auto: true, credits: 1_000_000, towers: new[] { Bolt(1, 0), Bolt(2, 1), Bolt(3, 2) });
        var calc = Calc();
        calc.Compute(s, T0.AddHours(1));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        calc.Compute(s, T0.AddHours(12));
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 500, $"{sw.ElapsedMilliseconds} ms");
    }
}
