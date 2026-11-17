using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using StartWave = SideWar.Core.Td.StartWave;
using BuildTower = SideWar.Core.Td.BuildTower;
using UpgradeTower = SideWar.Core.Td.UpgradeTower;

namespace SideWar.Core.Tests;

public sealed class TdMatchTests
{
    private static readonly TdContent Content = TdMechanicsContent.Load();

    private static TdMatchState New(int stage = 1, TdContent? content = null, ulong seed = 42) =>
        TdMatchState.Create(content ?? Content, stage, Rng.Seed(seed, 54));

    private static long Ceil(double v) => (long)Math.Ceiling(v * (1.0 - 1e-12));

    private static void Steps(TdMatchState s, int n)
    {
        for (int i = 0; i < n; i++) s.Step();
    }

    /// <summary>Conteúdo com a fase 1 trocada por ondas simples sem torres: <paramref name="counts"/> grunts por onda.</summary>
    private static TdContent Variant(int[] counts, long credits = 150, int maxTowers = 6, double interval = 1.1)
    {
        var waves = counts.Select((c, i) => new TdWaveDef(i + 1, false, 1.0, [new TdWaveGroupDef("grunt", c, 0.0, interval)])).ToArray();
        var stage1 = Content.Stages.Stages[0] with { Waves = counts.Length, StartingCredits = credits, MaxTowers = maxTowers };
        return Content with
        {
            Waves = new TdWavesDef([new TdStageWavesDef(1, waves), .. Content.Waves.Stages.Skip(1)]),
            Stages = Content.Stages with { Stages = [stage1, .. Content.Stages.Stages.Skip(1)] },
        };
    }

    private static void RunUntilPhase(TdMatchState s, MatchPhase phase, int maxFrames = 200_000)
    {
        while (s.Phase != phase && s.Frame < maxFrames)
        {
            if (s.Phase == MatchPhase.Preparo) Assert.True(s.Apply(new StartWave()).IsOk);
            s.Step();
        }
        Assert.Equal(phase, s.Phase);
    }

    [Fact(DisplayName = "CA-28.1 / T-28.1 Initial state is Preparo with stage credits and full integrity")]
    public void Create_Initial()
    {
        var s = New(3);
        Assert.Equal(MatchPhase.Preparo, s.Phase);
        Assert.Equal(200, s.Credits);
        Assert.Equal(20, s.Integrity);
        Assert.Equal(0, s.WaveNumber);
        Assert.Equal(12, s.TotalWaves);
        Assert.Equal(AutoStartMode.Off, s.AutoStart);
        Assert.Throws<ArgumentOutOfRangeException>(() => New(9));
    }

    [Fact(DisplayName = "CA-28.1 / T-28.1 Wave never starts by itself by default; Preparo does not advance the world clock")]
    public void Preparo_DoesNotStartWave()
    {
        var s = New();
        Assert.True(s.Apply(new BuildTower("bolt", 300)).IsOk);
        Steps(s, 5000);
        Assert.Equal(MatchPhase.Preparo, s.Phase);
        Assert.Equal(0, s.Tick);
        Assert.Equal(0, s.WaveNumber);
        Assert.Empty(s.Enemies);
        Assert.Equal(5000, s.Frame);
        Assert.Equal(0, s.Towers[0].CooldownTicks);
    }

    [Fact(DisplayName = "CA-28.1 StartWave starts wave 1 and runs the 30 Hz clock; a second StartWave in Onda is rejected")]
    public void StartWave_StartsOnda()
    {
        var s = New();
        Assert.True(s.Apply(new StartWave()).IsOk);
        Assert.Equal(MatchPhase.Onda, s.Phase);
        Assert.Equal(1, s.WaveNumber);
        Steps(s, 40);
        Assert.Equal(40, s.Tick);
        Assert.NotEmpty(s.Enemies);
        Assert.Equal(CommandReason.InvalidPhase, s.Apply(new StartWave()).Reason);
        Assert.Equal(CommandReason.InvalidPhase, New().Apply(new CallNextWave()).Reason);
    }

    [Fact(DisplayName = "RF-28.2 Spawn timing jitter stays within ±10% of the interval and is seed dependent")]
    public void Spawn_JitterWithinTenPercent()
    {
        var a = New(seed: 1);
        var b = New(seed: 2);
        a.Apply(new StartWave());
        b.Apply(new StartWave());
        var wa = a.ActiveWaves[0];
        var wb = b.ActiveWaves[0];
        Assert.Equal(6, wa.SpawnCount);
        Assert.NotEqual(a.ComputeStateHash(), b.ComputeStateHash());
        // O i-ésimo inimigo nasce no tick esperado (i × 33) com folga de ±10% do intervalo (3,3 ticks) mais o arredondamento.
        var seen = new List<long>();
        for (int i = 0; i < 400 && seen.Count < 6; i++)
        {
            int spawned = wa.NextSpawn;
            a.Step();
            if (wa.NextSpawn > spawned) seen.Add(a.Tick);
        }
        for (int i = 0; i < seen.Count; i++)
            Assert.InRange(seen[i], Math.Max(1, i * 33 - 4), i * 33 + 4);
        Assert.Equal(6, seen.Count);
        Assert.Equal(6, wb.SpawnCount);
    }

    [Fact(DisplayName = "CA-28.1 / RF-28.3 Auto-start when enabled starts after the countdown; clamps seconds to 5..60")]
    public void AutoStart_Countdown()
    {
        var s = New();
        Assert.True(s.Apply(new SetAutoStart(AutoStartMode.Countdown, 15)).IsOk);
        Steps(s, 449);
        Assert.Equal(MatchPhase.Preparo, s.Phase);
        s.Step();
        Assert.Equal(MatchPhase.Onda, s.Phase);
        Assert.Equal(1, s.WaveNumber);
        Assert.Equal(150, s.Credits); // contagem esgotada: sem bônus

        s.Apply(new SetAutoStart(AutoStartMode.Countdown, 1));
        Assert.Equal(5, s.AutoStartSeconds);
        s.Apply(new SetAutoStart(AutoStartMode.Countdown, 600));
        Assert.Equal(60, s.AutoStartSeconds);
        s.Apply(new SetAutoStart(AutoStartMode.Off, 15));
        Assert.Equal(AutoStartMode.Off, s.AutoStart);
    }

    [Fact(DisplayName = "RF-28.3 Starting early during the auto-start countdown pays ceil(remaining × (0.6 + 0.05 w))")]
    public void AutoStart_EarlyStartPaysBonus()
    {
        var s = New();
        s.Apply(new SetAutoStart(AutoStartMode.Countdown, 15));
        Steps(s, 30);
        Assert.Equal(14.0, s.CountdownRemainingSeconds);
        Assert.True(s.Apply(new StartWave()).IsOk);
        Assert.Equal(150 + 10, s.Credits); // ceil(14 × 0.65) = 10
    }

    [Fact(DisplayName = "RF-28.3 After a wave ends the countdown restarts, and Vitória/Derrota never auto-restart")]
    public void AutoStart_RestartsAfterWave_NeverAfterEnd()
    {
        var s = New(content: Variant([1, 1]), seed: 7);
        s.Apply(new SetAutoStart(AutoStartMode.Countdown, 5));
        Steps(s, 150); // inicia a onda 1
        Assert.Equal(1, s.WaveNumber);
        while (s.Phase == MatchPhase.Onda) s.Step();
        Assert.Equal(MatchPhase.Preparo, s.Phase); // onda 1 acabou; contagem recomeçou
        Assert.Equal(5.0, s.CountdownRemainingSeconds);
        Steps(s, 150);
        Assert.Equal(2, s.WaveNumber);
        while (s.Phase == MatchPhase.Onda) s.Step();
        Assert.Equal(MatchPhase.Derrota == s.Phase ? MatchPhase.Derrota : MatchPhase.Vitoria, s.Phase);
        long frame = s.Frame;
        Steps(s, 1000);
        Assert.Equal(2, s.WaveNumber);
        Assert.NotEqual(MatchPhase.Preparo, s.Phase);
        Assert.Equal(frame + 1000, s.Frame);
    }

    [Fact(DisplayName = "CA-28.2 / T-28.2 Build, upgrade and sell work during the wave (sell refunds 70% of the total invested)")]
    public void BuildUpgradeSell_DuringWave()
    {
        var s = New();
        s.Apply(new StartWave());
        Steps(s, 10);
        Assert.True(s.Apply(new BuildTower("bolt", 300)).IsOk);
        Assert.Equal(150 - 70, s.Credits);
        int id = s.Towers[0].Id;
        Assert.True(s.Apply(new UpgradeTower(id)).IsOk);
        Assert.Equal(80 - 63, s.Credits); // 0,9 × 70
        Assert.Equal(2, s.Towers[0].Level);
        Assert.Equal(133, s.Towers[0].Invested);
        Assert.True(s.Apply(new SellTower(id)).IsOk);
        Assert.Equal(17 + 93, s.Credits); // floor(133 × 0,7)
        Assert.Empty(s.Towers);
        Assert.Equal(CommandReason.TowerNotFound, s.Apply(new SellTower(id)).Reason);
        Assert.Equal(MatchPhase.Onda, s.Phase);
    }

    [Fact(DisplayName = "RF-28.4 Sell also works in Preparo and the linear upgrade stops at level 3 (branch is TD.3)")]
    public void Sell_Preparo_And_MaxLinearLevel()
    {
        var s = New(content: Variant([2], credits: 10_000));
        s.Apply(new BuildTower("bolt", 100));
        int id = s.Towers[0].Id;
        Assert.True(s.Apply(new UpgradeTower(id)).IsOk);
        Assert.True(s.Apply(new UpgradeTower(id)).IsOk);
        Assert.Equal(3, s.Towers[0].Level);
        Assert.Equal(70 + 63 + 112, s.Towers[0].Invested); // 1,6 × 70 = 112
        Assert.Equal(CommandReason.MaxLevel, s.Apply(new UpgradeTower(id)).Reason);
        long before = s.Credits;
        Assert.True(s.Apply(new SellTower(id)).IsOk);
        Assert.Equal(before + (long)Math.Floor(245 * 0.7), s.Credits);
    }

    [Fact(DisplayName = "RF-28.4 Build/upgrade rejections: locked tower, credits, limit, out of range, unknown; state unchanged")]
    public void Build_Rejections()
    {
        var s = New();
        var hash = s.ComputeStateHash();
        Assert.Equal(CommandReason.TowerLocked, s.Apply(new BuildTower("balancer", 100)).Reason);
        Assert.Equal(CommandReason.TowerNotFound, s.Apply(new BuildTower("nope", 100)).Reason);
        Assert.Equal(CommandReason.OutOfZone, s.Apply(new BuildTower("bolt", -1)).Reason);
        Assert.Equal(CommandReason.OutOfZone, s.Apply(new BuildTower("bolt", 1201)).Reason);
        Assert.Equal(CommandReason.UnknownCommand, s.Apply(null).Reason);
        Assert.Equal(CommandReason.TowerNotFound, s.Apply(new UpgradeTower(99)).Reason);
        Assert.Equal(hash, s.ComputeStateHash());
        s.Apply(new BuildTower("bolt", 100));
        s.Apply(new BuildTower("bolt", 200));
        var r = s.Apply(new BuildTower("bolt", 300)); // 150 - 140 = 10 créditos
        Assert.Equal(CommandReason.InsufficientCredits, r.Reason);
        Assert.Equal(60, r.Missing);
        Assert.Equal(CommandReason.InsufficientCredits, s.Apply(new UpgradeTower(s.Towers[0].Id)).Reason);

        var rich = New(content: Variant([1], credits: 100_000, maxTowers: 2));
        rich.Apply(new BuildTower("bolt", 1));
        rich.Apply(new BuildTower("bolt", 2));
        Assert.Equal(CommandReason.TowerLimit, rich.Apply(new BuildTower("bolt", 3)).Reason);
    }

    [Fact(DisplayName = "CA-28.3 / T-28.3 CallNextWave is rejected before spawns end, then pays a capped bonus and overlaps waves")]
    public void CallNextWave_PaysBonus()
    {
        var s = New();
        s.Apply(new StartWave());
        Steps(s, 20);
        Assert.Equal(CommandReason.NotAvailable, s.Apply(new CallNextWave()).Reason);
        while (!s.ActiveWaves[0].SpawnsDone) s.Step();
        double eta = s.EtaSeconds();
        Assert.True(eta > 0);
        long expected = Math.Min(Ceil(eta * 0.7), 2 * s.WaveBonusFor(2));
        long before = s.Credits;
        Assert.True(s.Apply(new CallNextWave()).IsOk);
        Assert.Equal(before + expected, s.Credits);
        Assert.True(expected > 0);
        Assert.Equal(2, s.WaveNumber);
        Assert.Equal(2, s.ActiveWaves.Count);
        Assert.True(s.CooldownReductionTicks > 0);
        Assert.Equal(MatchPhase.Onda, s.Phase);
    }

    [Fact(DisplayName = "RF-28.3 Call bonus is capped at 2 × the called wave's wave bonus")]
    public void CallNextWave_BonusCapped()
    {
        // Onda de 1 grunt a 1000 s de intervalo… usa velocidade baixa via conteúdo: um tank/chefe lento daria ETA enorme.
        var waves = new TdWaveDef[]
        {
            new(1, false, 1.0, [new TdWaveGroupDef("ransomware", 1, 0.0, 1.0)]),
            new(2, false, 1.0, [new TdWaveGroupDef("grunt", 1, 0.0, 1.0)]),
        };
        var stage1 = Content.Stages.Stages[0] with { Waves = 2 };
        var c = Content with
        {
            Waves = new TdWavesDef([new TdStageWavesDef(1, waves), .. Content.Waves.Stages.Skip(1)]),
            Stages = Content.Stages with { Stages = [stage1, .. Content.Stages.Stages.Skip(1)] },
        };
        var s = New(content: c);
        s.Apply(new StartWave());
        Steps(s, 40); // chefe a 20 un/s → ETA ≈ 59 s → bônus bruto ≈ 41 > teto 2 × 28 = 56? (0,7 × 59 = 41): usa teto real
        double eta = s.EtaSeconds();
        long raw = Ceil(eta * 0.7);
        long cap = 2 * s.WaveBonusFor(2);
        long before = s.Credits;
        Assert.True(s.Apply(new CallNextWave()).IsOk);
        Assert.Equal(Math.Min(raw, cap), s.Credits - before);
    }

    [Fact(DisplayName = "RF-28.4 Wave bonus (20 + 4w) × factor is paid when a wave ends")]
    public void WaveBonus_PaidOnCompletion()
    {
        var s = New(content: Variant([1, 1], credits: 0));
        Assert.Equal(0, s.Credits);
        RunUntilPhase(s, MatchPhase.Vitoria);
        // Sem torres: o grunt vaza (sem ouro de morte); bônus onda 1 = 24, onda 2 = 28.
        Assert.Equal(24 + 28, s.Credits);
    }

    [Fact(DisplayName = "CA-28.1 Preparo freezes towers: no cooldown recovery between waves")]
    public void Preparo_FreezesTowers()
    {
        var s = New(content: Variant([3, 3], credits: 1000));
        s.Apply(new BuildTower("bolt", 1100));
        s.Apply(new BuildTower("bolt", 1050));
        s.Apply(new StartWave());
        while (s.Phase == MatchPhase.Onda) s.Step();
        Assert.Equal(MatchPhase.Preparo, s.Phase);
        var towers = s.Towers.ToArray();
        long tick = s.Tick;
        Steps(s, 500);
        Assert.Equal(towers, s.Towers.ToArray());
        Assert.Equal(tick, s.Tick);
        Assert.Empty(s.Enemies);
    }

    [Fact(DisplayName = "CA-28.1 Towers kill enemies, pay gold and may never leak (bolt vs grunts)")]
    public void Towers_KillAndPayGold()
    {
        var s = New(content: Variant([3], credits: 1000));
        s.Apply(new BuildTower("bolt", 1100));
        s.Apply(new BuildTower("bolt", 900));
        s.Apply(new StartWave());
        long credits = s.Credits;
        while (s.Phase == MatchPhase.Onda) s.Step();
        Assert.Equal(MatchPhase.Vitoria, s.Phase);
        Assert.Equal(20, s.Integrity);
        Assert.Equal(3, s.Stars);
        Assert.Equal(credits + 3 * 6 + 24, s.Credits);
    }

    [Fact(DisplayName = "CA-28.1 Enemy cap on the field is respected (44); excess spawns wait")]
    public void EnemyCap()
    {
        var s = New(content: Variant([80], interval: 0.0));
        s.Apply(new StartWave());
        int max = 0;
        for (int i = 0; i < 300; i++)
        {
            s.Step();
            max = Math.Max(max, s.Enemies.Count);
        }
        Assert.Equal(44, max);
    }

    [Fact(DisplayName = "CA-28.1 Defeat when integrity reaches 0; commands afterwards are rejected and stars are 0")]
    public void Defeat_WhenIntegrityZero()
    {
        var s = New(content: Variant([30]));
        RunUntilPhase(s, MatchPhase.Derrota);
        Assert.Equal(0, s.Integrity);
        Assert.Equal(0, s.Stars);
        long frame = s.Frame;
        s.Step();
        Assert.Equal(frame + 1, s.Frame);
        Assert.Equal(MatchPhase.Derrota, s.Phase);
        Assert.Equal(CommandReason.InvalidPhase, s.Apply(new StartWave()).Reason);
        Assert.Equal(CommandReason.InvalidPhase, s.Apply(new BuildTower("bolt", 10)).Reason);
        Assert.Equal(CommandReason.InvalidPhase, s.Apply(new SellTower(1)).Reason);
        Assert.Contains(s.Events, e => e.Type == TdEventType.Defeat);
    }

    [Theory(DisplayName = "CA-28.1 / DP-28.8 Victory stars by integrity lost (≤2 → 3★, ≤6 → 2★, else 1★)")]
    [InlineData(0, 3)]
    [InlineData(2, 3)]
    [InlineData(3, 2)]
    [InlineData(6, 2)]
    [InlineData(7, 1)]
    [InlineData(19, 1)]
    public void Victory_Stars(int leaks, int stars)
    {
        var s = New(content: Variant([leaks == 0 ? 0 : leaks]));
        RunUntilPhase(s, MatchPhase.Vitoria);
        Assert.Equal(leaks, s.IntegrityLost);
        Assert.Equal(stars, s.Stars);
        Assert.Equal(stars, s.Result.Stars);
        Assert.Contains(s.Events, e => e.Type == TdEventType.Victory);
    }

    [Fact(DisplayName = "CA-28.1 Real stage 1 content is winnable by the player with towers built and upgraded during the waves")]
    public void RealStage1_Winnable()
    {
        var s = New();
        s.Apply(new BuildTower("bolt", 150));
        s.Apply(new BuildTower("bolt", 330));
        int guard = 0;
        while (s.Phase is MatchPhase.Preparo or MatchPhase.Onda && guard++ < 400_000)
        {
            if (s.Phase == MatchPhase.Preparo) s.Apply(new StartWave());
            if (s.Frame % 30 == 0)
            {
                for (int i = 0; i < s.Towers.Count; i++) s.Apply(new UpgradeTower(s.Towers[i].Id));
                if (s.Towers.Count < 6 && s.Credits >= 70) s.Apply(new BuildTower("bolt", 60 + s.Towers.Count * 120));
            }
            s.Step();
        }
        Assert.Equal(MatchPhase.Vitoria, s.Phase);
        Assert.True(s.Stars >= 1);
    }

    [Fact(DisplayName = "RNF-28.1 / CA-28.2 Same log + seed → same StateHash in 5 runs; different seed differs")]
    public void Replay_IsDeterministic()
    {
        ulong first = RunScript(42).StateHash;
        for (int i = 0; i < 4; i++) Assert.Equal(first, RunScript(42).StateHash);
        Assert.NotEqual(first, RunScript(43).StateHash);
        Assert.Equal(GoldenReplay, first);
    }

    [Fact(DisplayName = "CA-28.2 Replay applies build/upgrade/sell/call during the wave and rejects nothing in the script")]
    public void Replay_ScriptOutcome()
    {
        var r = RunScript(42);
        Assert.Equal(0, r.CommandsRejected);
        Assert.True(r.Ticks > 0);
    }

    private const ulong GoldenReplay = 16820935973378525438UL;

    private static TdReplayResult RunScript(ulong seed)
    {
        var log = new List<TdLogEntry>
        {
            new(0, new BuildTower("bolt", 300)),
            new(0, new BuildTower("bolt", 600)),
            new(0, new StartWave()),
            new(30, new BuildTower("frost", 900)),
            new(190, new CallNextWave()),
            new(200, new UpgradeTower(1)),
            new(210, new SellTower(3)),
            new(900, new UpgradeTower(2)),
        };
        return TdReplayRunner.Run(Variant([6, 6, 6], credits: 1000), new TdReplayScript(1, seed, 54, log, 3000));
    }
}
