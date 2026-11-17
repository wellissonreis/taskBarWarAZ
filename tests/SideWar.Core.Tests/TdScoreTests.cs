using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using StartWave = SideWar.Core.Td.StartWave;
using SellTower = SideWar.Core.Td.SellTower;
using CallNextWave = SideWar.Core.Td.CallNextWave;

namespace SideWar.Core.Tests;

/// <summary>TD.13 (RF-28.21): coroa, medalhas, pontuação determinística e patente local.</summary>
public sealed class TdScoreTests
{
    private static readonly TdContent Mech = TdMechanicsContent.Load();
    private static readonly TdContent Real = TdContentLoader.LoadEmbedded();

    private static TdContent Variant(int[] counts, long credits = 150)
    {
        var waves = counts.Select((c, i) => new TdWaveDef(i + 1, false, 1.0, [new TdWaveGroupDef("grunt", c, 0.0, 1.1)])).ToArray();
        var stage1 = Mech.Stages.Stages[0] with { Waves = counts.Length, StartingCredits = credits };
        return Mech with
        {
            Waves = new TdWavesDef([new TdStageWavesDef(1, waves), .. Mech.Waves.Stages.Skip(1)]),
            Stages = Mech.Stages with { Stages = [stage1, .. Mech.Stages.Stages.Skip(1)] },
        };
    }

    private static TdMatchState New(int[] counts, long credits = 150) => TdMatchState.Create(Variant(counts, credits), 1, Rng.Seed(42, 54));

    private static void Finish(TdMatchState s)
    {
        int guard = 0;
        while (s.Phase is MatchPhase.Preparo or MatchPhase.Onda && guard++ < 400_000)
        {
            if (s.Phase == MatchPhase.Preparo) s.Apply(new StartWave());
            s.Step();
        }
    }

    [Fact(DisplayName = "RF-28.21 Score formula: documented example (3★ + coroa = 150%)")]
    public void Score_FormulaExample()
    {
        // (1000 + 50×20 + (1000−300) + 100×0 + 144/4=36) × 150% = 2736 × 1,5
        Assert.Equal(4104, TdScoring.Compute(20, 300.0, 0, 144, 3, crown: true));
        // 1★ sem coroa: 100%; 2★: 110%; 3★: 125%
        Assert.Equal(1000, TdScoring.Compute(0, 1500.0, 0, 0, 1, false));
        Assert.Equal(1000 + 600 + 700 + 200 + 500, TdScoring.Compute(12, 300.4, 2, 99999, 1, false));
        Assert.Equal(3000 * 110 / 100, TdScoring.Compute(20, 0.0, 0, 0, 2, false));
        Assert.Equal(3000 * 125 / 100, TdScoring.Compute(20, 0.0, 0, 0, 3, false));
    }

    [Fact(DisplayName = "RF-28.21 Score is monotonic: more integrity, less time, more early calls, more credits")]
    public void Score_Monotonic()
    {
        int b = TdScoring.Compute(10, 400, 1, 100, 2, false);
        Assert.True(TdScoring.Compute(11, 400, 1, 100, 2, false) > b);
        Assert.True(TdScoring.Compute(10, 300, 1, 100, 2, false) > b);
        Assert.True(TdScoring.Compute(10, 400, 2, 100, 2, false) > b);
        Assert.True(TdScoring.Compute(10, 400, 1, 200, 2, false) > b);
        Assert.True(TdScoring.Compute(10, 400, 1, 100, 3, false) > b);
        Assert.True(TdScoring.Compute(10, 400, 1, 100, 2, true) > b);
        Assert.Equal(TdScoring.Compute(10, 4000, 1, 100, 2, false), TdScoring.Compute(10, 1000, 1, 100, 2, false)); // tempo não é negativo
    }

    [Fact(DisplayName = "RF-28.21 Flawless win: crown, Sem vender, Sem habilidades; no Pressa without early calls")]
    public void Flawless_CrownAndMedals()
    {
        var s = New([0, 0]);
        Finish(s);
        Assert.Equal(MatchPhase.Vitoria, s.Phase);
        var r = s.Result;
        Assert.True(r.Crown);
        Assert.Equal(3, r.Stars);
        Assert.Equal(TdMedals.SemVender | TdMedals.SemHabilidades, r.Medals & ~TdMedals.Desafio);
        Assert.False(r.Medals.HasFlag(TdMedals.Pressa));
        Assert.True(r.Score > 0);
        Assert.Equal(r, s.Result); // determinístico
    }

    [Fact(DisplayName = "RF-28.21 Any integrity lost removes the crown (but keeps stars)")]
    public void Leak_RemovesCrown()
    {
        var s = New([1]);
        Finish(s);
        Assert.Equal(MatchPhase.Vitoria, s.Phase);
        Assert.False(s.Result.Crown);
        Assert.True(s.IntegrityLost > 0);
        Assert.Equal(3, s.Result.Stars); // ≤ 2 perdidas ainda dá 3★
    }

    [Fact(DisplayName = "RF-28.21 Selling a tower removes Sem vender")]
    public void Sell_RemovesMedal()
    {
        var s = New([0], 500);
        Assert.True(s.Apply(new BuildTower("bolt", 900)).IsOk);
        Assert.True(s.Apply(new SellTower(s.Towers[0].Id)).IsOk);
        Finish(s);
        Assert.False(s.Result.Medals.HasFlag(TdMedals.SemVender));
        Assert.True(s.Result.Medals.HasFlag(TdMedals.SemHabilidades));
    }

    [Fact(DisplayName = "RF-28.21 Pressa needs every wave after the first called early")]
    public void Pressa_AllCalledEarly()
    {
        var s = New([2, 2, 2]);
        s.Apply(new StartWave());
        int guard = 0;
        while (s.Phase is MatchPhase.Preparo or MatchPhase.Onda && guard++ < 400_000)
        {
            if (s.Phase == MatchPhase.Onda) s.Apply(new CallNextWave());
            else s.Apply(new StartWave());
            s.Step();
        }
        Assert.Equal(MatchPhase.Vitoria, s.Phase);
        Assert.Equal(2, s.EarlyCalls);
        Assert.True(s.Result.Medals.HasFlag(TdMedals.Pressa));

        var slow = New([0, 0, 0]);
        Finish(slow);
        Assert.False(slow.Result.Medals.HasFlag(TdMedals.Pressa));
    }

    [Fact(DisplayName = "RF-28.21 Challenge medal follows the stage challenge (Frugal: at most 4 towers)")]
    public void Challenge_Frugal()
    {
        var ok = New([0], 2000);
        for (int i = 0; i < 4; i++) ok.Apply(new BuildTower("bolt", 100 + i * 150));
        Finish(ok);
        Assert.True(ok.Result.Medals.HasFlag(TdMedals.Desafio));

        var over = New([0], 2000);
        for (int i = 0; i < 5; i++) over.Apply(new BuildTower("bolt", 100 + i * 150));
        Finish(over);
        Assert.Equal(5, over.PeakTowers);
        Assert.False(over.Result.Medals.HasFlag(TdMedals.Desafio));
    }

    [Fact(DisplayName = "RF-28.21 Defeat has no crown, medals or score")]
    public void Defeat_NoRewards()
    {
        var r = TdSanityBot.Run(Real, 1, TdBotProfile.SemContra, 100);
        Assert.Equal(MatchPhase.Derrota, r.Phase);
        Assert.False(r.Match.Crown);
        Assert.Equal(TdMedals.None, r.Match.Medals);
        Assert.Equal(0, r.Match.Score);
    }

    [Fact(DisplayName = "RF-28.21 Bot: optimized stage 1 sells for runes and earns crown, Sem habilidades and Pressa")]
    public void Bot_Medals()
    {
        var o = TdSanityBot.Run(Real, 1, TdBotProfile.Otimizado, 100);
        Assert.True(o.Match.Crown);
        Assert.Equal(TdMedals.SemHabilidades | TdMedals.Pressa, o.Match.Medals);
        var m = TdSanityBot.Run(Real, 3, TdBotProfile.Medio, 100);
        Assert.True(m.Won);
        Assert.True(m.Match.Medals.HasFlag(TdMedals.SemVender));
        Assert.False(m.Match.Medals.HasFlag(TdMedals.SemHabilidades));
    }

    [Fact(DisplayName = "RF-28.21 Tracking stays out of the state hash")]
    public void Tracking_NotInHash()
    {
        var a = New([0], 500);
        var b = New([0], 500);
        Finish(a); Finish(b);
        Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
        Assert.Equal(a.Result, b.Result);
    }

    // ---------------------------------------------------------------- patente

    [Fact(DisplayName = "RF-28.21 Rank thresholds are ascending and map score to the right level")]
    public void Rank_Levels()
    {
        Assert.Equal(TdRankLevel.Estagiario, TdRank.For(0));
        Assert.Equal(TdRankLevel.Estagiario, TdRank.For(4_999));
        Assert.Equal(TdRankLevel.Junior, TdRank.For(5_000));
        Assert.Equal(TdRankLevel.Pleno, TdRank.For(12_000));
        Assert.Equal(TdRankLevel.Senior, TdRank.For(22_000));
        Assert.Equal(TdRankLevel.Arquiteto, TdRank.For(29_000));
        Assert.Equal(TdRankLevel.Mvp, TdRank.For(34_000));
        Assert.Equal(TdRankLevel.Mvp, TdRank.For(int.MaxValue));
        for (var l = TdRankLevel.Junior; l <= TdRankLevel.Mvp; l++) Assert.True(TdRank.Threshold(l) > TdRank.Threshold(l - 1));
    }

    [Fact(DisplayName = "RF-28.21 Rank progress: fraction to the next level; MVP is full")]
    public void Rank_Progress()
    {
        var p = TdRank.Progress(8_500);
        Assert.Equal(TdRankLevel.Junior, p.Current);
        Assert.Equal(TdRankLevel.Pleno, p.Next);
        Assert.Equal(0.5, p.Fraction, 3);
        var m = TdRank.Progress(40_000);
        Assert.Null(m.Next);
        Assert.Equal(1.0, m.Fraction);
        Assert.Equal(TdRankLevel.Estagiario, TdRank.Progress(-5).Current);
    }

    private static int Record(int stage, TdBotProfile p)
    {
        int best = 0;
        for (int i = 0; i < 20; i++)
        {
            var r = TdSanityBot.Run(Real, stage, p, (ulong)(100 + i));
            if (r.Won) best = Math.Max(best, r.Match.Score);
        }
        return best;
    }

    [Fact(DisplayName = "RF-28.21 Calibration: optimized bot reaches MVP, medium stays Pleno/Senior, no-counter stays Estagiario")]
    public void Rank_BotCalibration()
    {
        int Total(TdBotProfile p) => Enumerable.Range(1, 6).Sum(st => Record(st, p));
        Assert.Equal(TdRankLevel.Mvp, TdRank.For(Total(TdBotProfile.Otimizado)));
        var medio = TdRank.For(Total(TdBotProfile.Medio));
        Assert.InRange(medio, TdRankLevel.Pleno, TdRankLevel.Senior);
        Assert.Equal(TdRankLevel.Estagiario, TdRank.For(Total(TdBotProfile.SemContra)));
        Assert.True(TdRank.For(Total(TdBotProfile.Novato)) < TdRankLevel.Arquiteto);
    }
}
