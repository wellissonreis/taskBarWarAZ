using SideWar.Core.Content;
using SideWar.Core.Prestige;
using SideWar.Core.Simulation;
using Xunit;

namespace SideWar.Core.Tests;

/// <summary>T6.7 / T-27.11 / CA-27.8: prestígio "Migrar", regiões e Reservas.</summary>
public sealed class PrestigeTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);
    private static readonly int[] Unlocked01 = [0, 1];
    private static EconomyDef Eco => Content.Economy;
    private static RegionDef Brazil => Content.Regions[0];
    private static RegionDef EastUs => Content.Regions[1];
    private static RegionDef WestEurope => Content.Regions[2];

    private static StripLayout Layout() =>
        StripLayout.Create(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 380f });

    private static SimState Mid(int waveMax = 37)
    {
        var s = SimState.CreateInitial(Content, Layout(), Rng.Seed(42, 54));
        s.Stage = 3;
        s.Wave = waveMax;
        s.StageWaveMax = waveMax;
        s.UnlockedStage = 3;
        s.Level = 7;
        s.Xp = 123;
        s.Credits = 99999;
        s.StageFalls = 2;
        s.BossQuestionsCorrect = 1;
        s.RestoreStageProgress(1, 20, 3);
        s.RestoreStageProgress(3, 20, 1);
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, 0));
        s.Apply(Ctx, new BuildTower(TowerType.Frost, 1));
        return s;
    }

    [Theory]
    [InlineData(14, false)]
    [InlineData(15, true)]
    [InlineData(60, true)]
    public void Availability_Threshold15(int waveMax, bool expected) =>
        Assert.Equal(expected, PrestigeService.IsAvailable(Eco, waveMax));

    [Theory]
    [InlineData(14, 0)]
    [InlineData(15, 1)]    // 225/170
    [InlineData(37, 8)]    // 1369/170 (exemplo da SDD)
    [InlineData(100, 58)]  // 10000/170
    public void ReservesGain_Brazil(int waveMax, long expected) =>
        Assert.Equal(expected, PrestigeService.ReservesGain(Eco, Brazil, waveMax));

    [Theory]
    [InlineData(15, 1, 2)]     // floor(1×1,5)=1 ; floor(1×2,2)=2
    [InlineData(37, 12, 17)]   // floor(8×1,5)=12 ; floor(8×2,2)=17 (17,6)
    [InlineData(100, 87, 127)] // floor(58×1,5)=87 ; floor(58×2,2)=127 (127,6)
    public void ReservesGain_RegionMultipliers(int waveMax, long east, long west)
    {
        Assert.Equal(east, PrestigeService.ReservesGain(Eco, EastUs, waveMax));
        Assert.Equal(west, PrestigeService.ReservesGain(Eco, WestEurope, waveMax));
    }

    [Fact]
    public void RegionUnlock_Rules()
    {
        Assert.True(PrestigeService.IsRegionUnlocked(Brazil, 0, 0));
        Assert.False(PrestigeService.IsRegionUnlocked(EastUs, 100, 0));
        Assert.True(PrestigeService.IsRegionUnlocked(EastUs, 0, 1));
        Assert.False(PrestigeService.IsRegionUnlocked(WestEurope, 29, 5));
        Assert.True(PrestigeService.IsRegionUnlocked(WestEurope, 30, 0));
    }

    [Fact]
    public void Migrate_BelowWave15_NotAvailable_AndStateUntouched()
    {
        var s = Mid(14);
        ulong hash = s.ComputeStateHash();
        var r = s.Apply(Ctx, new MigrateTo(0));
        Assert.Equal(CommandReason.NotAvailable, r.Reason);
        Assert.Equal(hash, s.ComputeStateHash());
    }

    [Fact]
    public void Migrate_ResetsWavesTowersCredits_KeepsLevelXpStarsAndAddsReserves()
    {
        var s = Mid();
        int attempt = s.StageAttempt;
        long cursor = s.Events.Written;
        var r = s.Apply(Ctx, new MigrateTo(0));
        Assert.True(r.IsOk);
        // reiniciado
        Assert.Equal(1, s.Wave);
        Assert.Equal(1, s.StageWaveMax);
        Assert.Equal(0, s.Towers.Count);
        Assert.Equal(Eco.StartingCredits, s.Credits);
        Assert.Equal(0, s.StageFalls);
        Assert.Equal(0, s.BossQuestionsCorrect);
        Assert.Equal(0, s.LastBossWaveBeaten(3));
        // mantido
        Assert.Equal(3, s.Stage);
        Assert.Equal(3, s.UnlockedStage);
        Assert.Equal(7, s.Level);
        Assert.Equal(123, s.Xp);
        Assert.Equal(3, s.StarsOf(1));
        Assert.Equal(1, s.StarsOf(3));
        Assert.Equal(20, s.LastBossWaveBeaten(1));
        Assert.Equal(8, s.Reservas);
        Assert.Equal(1, s.Migrations);
        Assert.Equal(attempt + 1, s.StageAttempt);
        Assert.Equal(attempt + 1, s.CurrentBossKey.Attempt);
        Assert.NotNull(s.ActivePlan);
        var evs = new List<SimEvent>();
        while (s.Events.TryRead(ref cursor, out var e)) evs.Add(e);
        Assert.Contains(evs, e => e.Type == SimEventType.Migrated && e.A == 8 && e.B == 0);
    }

    [Fact]
    public void Migrate_KeepsLedgerAnswers_ButNewAttemptKey()
    {
        var s = Mid();
        var key = new SideWar.Core.Events.LedgerKey(3, s.StageAttempt, 20);
        Assert.Equal(SideWar.Core.Events.LedgerStatus.Ok, s.Ledger.Record(key, "q1", 0, true));
        s.Apply(Ctx, new MigrateTo(0));
        Assert.True(s.Ledger.Has(key));
        Assert.False(s.Ledger.Has(new SideWar.Core.Events.LedgerKey(3, s.StageAttempt, 20)));
    }

    [Fact]
    public void Migrate_RegionLocked_UntilFirstMigrationAndReserves()
    {
        var s = Mid();
        Assert.Equal(CommandReason.RegionLocked, s.Apply(Ctx, new MigrateTo(1)).Reason);
        Assert.Equal(CommandReason.RegionLocked, s.Apply(Ctx, new MigrateTo(2)).Reason);
        Assert.Equal(CommandReason.RegionLocked, s.Apply(Ctx, new MigrateTo(99)).Reason);
        Assert.Equal(CommandReason.RegionLocked, s.Apply(Ctx, new MigrateTo(-1)).Reason);
        Assert.True(s.Apply(Ctx, new MigrateTo(0)).IsOk);   // 8 Reservas, 1 migração
        Assert.Equal(Unlocked01, PrestigeService.UnlockedRegions(s, Content));
        s.StageWaveMax = 37;
        Assert.Equal(CommandReason.RegionLocked, s.Apply(Ctx, new MigrateTo(2)).Reason);   // 8 < 30
        Assert.True(s.Apply(Ctx, new MigrateTo(1)).IsOk);
        Assert.Equal(1, s.RegionIndex);
    }

    [Fact]
    public void Migrate_UsesRegionOfEndingRun_ForReserves()   // DP-27.12
    {
        var s = Mid();
        s.Apply(Ctx, new MigrateTo(0));
        s.StageWaveMax = 37;
        Assert.True(s.Apply(Ctx, new MigrateTo(1)).IsOk);
        s.StageWaveMax = 37;
        Assert.Equal(12, PrestigeService.PreviewGain(s, Ctx));   // East US (partida que termina)
        long before = s.Reservas;
        Assert.True(s.Apply(Ctx, new MigrateTo(0)).IsOk);
        Assert.Equal(before + 12, s.Reservas);
    }

    [Fact]
    public void Region_ScalesEnemyHp()
    {
        double SpawnHp(int region)
        {
            var s = Mid();
            s.Apply(Ctx, new MigrateTo(0));
            if (region != 0) { s.StageWaveMax = 37; s.Apply(Ctx, new MigrateTo(region)); }
            for (int i = 0; i < 200 && s.Enemies.Count == 0; i++) s.Step(Ctx);
            for (int i = 0; i < s.Enemies.Capacity; i++) if (s.Enemies.IsAlive(i)) return s.Enemies.At(i).MaxHp;
            throw new InvalidOperationException("sem inimigo");
        }
        double b = SpawnHp(0), e = SpawnHp(1);
        Assert.Equal(b * 1.4, e, 6);
    }

    [Fact]
    public void Reserves_BoostCreditsOfKills_Immediately()
    {
        long Gain(long reservas)
        {
            var s = SimState.CreateInitial(Content, Layout(), Rng.Seed(42, 54));
            s.Reservas = reservas;
            s.Credits = 0;
            s.StartWave(Ctx);
            s.Apply(Ctx, new BuildTower(TowerType.Bolt, 0));
            s.Credits = 0;
            for (int i = 0; i < 600 && s.Credits == 0; i++) s.Step(Ctx);
            return s.Credits;
        }
        Assert.True(Gain(100) >= 2 * Gain(0) - 1);   // +100% com 100 Reservas
    }

    [Fact]
    public void RestoreRegion_AppliesHpAndAffectsHash()
    {
        var a = Mid(); var b = Mid();
        b.RestoreRegion(Ctx, 2, 3);
        Assert.Equal(2, b.RegionIndex);
        Assert.Equal(3, b.Migrations);
        Assert.NotEqual(a.ComputeStateHash(), b.ComputeStateHash());
    }
}
