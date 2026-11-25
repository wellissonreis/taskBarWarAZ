using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using StartWave = SideWar.Core.Td.StartWave;

namespace SideWar.Core.Tests;

/// <summary>INFINITO: fases longas (~25-40 min) e ciclos "Ciclo 2, 3, ..." sem teto, determinísticos por (ciclo, fase, onda).</summary>
public sealed class TdInfiniteTests
{
    private static readonly TdContent C = TdContentLoader.LoadEmbedded();

    private static TdMatchState New(int stage, ulong seed = 7)
    {
        var s = TdMatchState.Create(C, stage, Rng.Seed(seed, 54), null, TdMatchOptions.Default);
        Assert.True(s.Apply(new LayoutChanged(TdSanityBot.TypicalLayout())).IsOk);
        Assert.True(s.Apply(new SetPresence(false)).IsOk);
        return s;
    }

    [Fact]
    public void Every_stage_is_long_and_the_opening_of_stage_1_stays_calm()
    {
        foreach (var st in C.Stages.Stages) Assert.InRange(st.Waves, 55, 475); // fases longas (docs/balanceamento-fases-xp.md)
        var w1 = C.Waves.Stages[0].Waves;
        Assert.Equal(1, w1[0].Groups.Sum(g => g.Count)); // início calmo (RITMO-INICIO) preservado
        Assert.True(w1.Take(7).All(w => w.Groups.Sum(g => g.Count) <= 13));
        Assert.Contains(w1, w => w.Rest);
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(6, 0, 6)]
    [InlineData(7, 1, 1)]
    [InlineData(13, 2, 1)]
    [InlineData(36, 5, 6)]
    [InlineData(600, 99, 6)]
    public void Absolute_stage_maps_to_cycle_and_region_without_ceiling(int stageNumber, int cycle, int stageId)
    {
        var s = TdMatchState.Create(C, stageNumber, Rng.Seed(1, 54), null, TdMatchOptions.Default);
        Assert.Equal((stageNumber, cycle, stageId), (s.StageNumber, s.Cycle, s.StageId));
        Assert.Equal(C.Stages.Stages[stageId - 1].Waves, s.TotalWaves);
    }

    [Fact]
    public void First_lap_waves_are_untouched_and_cycles_scale_counts_cadence_and_add_mixed_extras()
    {
        var lap0 = New(3);
        var lap1 = New(9);
        var lap5 = New(33);
        for (int w = 1; w <= lap0.TotalWaves; w++) Assert.Equal(C.Waves.Stages[2].Waves[w - 1], lap0.EffectiveWaveDef(w));
        int Total(TdWaveDef d) => d.Groups.Sum(g => g.Count);
        long a0 = 0, a1 = 0, a5 = 0;
        for (int w = 1; w < lap0.TotalWaves; w++) { a0 += Total(lap0.EffectiveWaveDef(w)); a1 += Total(lap1.EffectiveWaveDef(w)); a5 += Total(lap5.EffectiveWaveDef(w)); }
        Assert.True(a1 > a0 * 1.1, $"{a1} vs {a0}");
        Assert.True(a5 > a1, $"{a5} vs {a1}");
        // a cada 3 ondas (deslocado pela fase) um grupo extra misto entra no fim
        int extras = 0;
        for (int w = 1; w < lap1.TotalWaves; w++) if (lap1.EffectiveWaveDef(w).Groups.Length > C.Waves.Stages[2].Waves[w - 1].Groups.Length) extras++;
        Assert.InRange(extras, lap1.TotalWaves / 3 - 1, lap1.TotalWaves / 3 + 1);
        // chefe: última onda segue com 1 chefe e sem grupo extra
        var last = lap5.EffectiveWaveDef(lap5.TotalWaves);
        Assert.Equal(C.Waves.Stages[2].Waves[^1].Groups.Length, last.Groups.Length);
        Assert.Equal(1, last.Groups.Single(g => g.Enemy == "ransomware").Count);
    }

    [Fact]
    public void Cycle_runs_are_deterministic_and_enemies_are_faster_and_more_numerous()
    {
        ulong Run(int stage, out int spawned, out double speed)
        {
            var s = New(stage);
            Assert.True(s.Apply(new BuildTower("bolt", 900)).IsOk);
            Assert.True(s.Apply(new StartWave()).IsOk);
            spawned = 0; speed = 0;
            for (int i = 0; i < 4000 && s.WaveNumber == 1; i++)
            {
                s.ClearEvents();
                s.Step();
                if (speed == 0 && s.Enemies.Count > 0) speed = s.Enemies[0].Speed;
                spawned += s.Events.Count(e => e.Type is TdEventType.EnemyKilled or TdEventType.EnemyLeaked);
            }
            return s.ComputeStateHash();
        }
        ulong h1 = Run(7, out int n1, out double v1), h2 = Run(7, out int n2, out double v2);
        Assert.Equal(h1, h2);
        Assert.Equal((n1, v1), (n2, v2));
        Run(1, out int n0, out double v0);
        Assert.True(n1 > n0, $"{n1} vs {n0}");
        Assert.True(v1 > v0, $"{v1} vs {v0}"); // ciclo 2 também tira o freio do início calmo
    }

    [Fact]
    public void Cycle_starts_with_catch_up_credits_only_after_the_first_lap()
    {
        long baseCredits(int st) => C.Stages.Stages[(st - 1) % 6].StartingCredits + C.Runes.CreditSources.StartingCreditsBonus;
        Assert.Equal(baseCredits(1), TdMatchState.Create(C, 1, Rng.Seed(1, 54)).Credits);
        Assert.Equal(baseCredits(6), TdMatchState.Create(C, 6, Rng.Seed(1, 54)).Credits);
        Assert.True(TdMatchState.Create(C, 7, Rng.Seed(1, 54)).Credits > baseCredits(7) + 4000);
        Assert.True(TdMatchState.Create(C, 19, Rng.Seed(1, 54)).Credits > TdMatchState.Create(C, 7, Rng.Seed(1, 54)).Credits);
    }

    [Fact]
    public void A_continuous_run_never_ends_after_stage_6_and_reports_cycle_in_the_stage_summary()
    {
        // Escala de campanha neutra e teia completa: mede o ciclo infinito, não o balanceamento (TdCampaignRulesTests).
        // Fases encurtadas para 20 ondas (mantendo a do chefe): as três fases completas estouram o teto de passos do bot.
        var waves = C.Waves.Stages.Select(st => new TdStageWavesDef(st.Stage, [.. st.Waves.Take(19), st.Waves[^1] with { Number = 20 }])).ToArray();
        var neutral = C with
        {
            Waves = new TdWavesDef(waves),
            Stages = C.Stages with { Stages = C.Stages.Stages.Select(s => s with { Waves = 20, HpStart = 0.25, HpEnd = 0.25, BossHp = 1, StartingCredits = 20_000 }).ToArray() },
        };
        var r = TdSanityBot.RunContinuous(neutral, 6, TdBotProfile.Medio, 1, 3, stopAtCollapse: false, initialAwakened: UInt128.MaxValue); // fase 6 -> 7 (ciclo 2) -> 8
        Assert.Equal(3, r.StagesCleared);
        Assert.Equal([6, 7, 8], r.Stages.Select(x => x.StageNumber));
        Assert.Equal([0, 1, 1], r.Stages.Select(x => x.Cycle));
        Assert.Equal([6, 1, 2], r.Stages.Select(x => x.StageId));
        Assert.All(r.Stages, x => Assert.True(x.CombatSeconds > 0));
    }
}
