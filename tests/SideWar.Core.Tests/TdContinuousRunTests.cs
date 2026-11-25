using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using StartWave = SideWar.Core.Td.StartWave;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Core.Tests;

/// <summary>TD.28a: ondas contínuas (Preparo de 0 s) e corrida persistente entre fases, com infinito depois da última (SDD-29 §20).</summary>
public sealed class TdContinuousRunTests
{
    private static readonly TdContent Base = TdMechanicsContent.Load();

    /// <summary>Conteúdo em que a fase <paramref name="stage"/> tem <paramref name="waves"/> ondas de 1 grunt (fácil de zerar) e créditos iniciais fartos.</summary>
    private static TdContent Tiny(int waves = 2, int stage = 1, long credits = 5000)
    {
        var defs = Enumerable.Range(1, waves).Select(i => new TdWaveDef(i, false, 1.0, [new TdWaveGroupDef("grunt", 1, 0.0, 1.0)])).ToArray();
        var stages = Base.Stages.Stages.Select(s => s.Id == stage ? s with { Waves = waves, StartingCredits = credits } : s).ToArray();
        var waveStages = Base.Waves.Stages.Select(s => s.Stage == stage ? new TdStageWavesDef(stage, defs) : s).ToArray();
        return Base with { Waves = new TdWavesDef(waveStages), Stages = Base.Stages with { Stages = stages } };
    }

    private static TdMatchState New(TdContent c, int stage, TdMatchOptions? o = null, ulong seed = 42) =>
        TdMatchState.Create(c, stage, Rng.Seed(seed, 54), null, o ?? TdMatchOptions.Default);

    private static void Armed(TdMatchState s)
    {
        Assert.True(s.Apply(new LayoutChanged(TdSanityBot.TypicalLayout())).IsOk);
        Assert.True(s.Apply(new SetPresence(false)).IsOk);
    }

    private static void StartWithTemporaryTowerIfNeeded(TdMatchState s)
    {
        bool temporary = !s.HasPlacedTower;
        if (temporary) Assert.True(s.Apply(new BuildTower("bolt", 900)).IsOk);
        Assert.True(s.Apply(new StartWave()).IsOk);
        if (temporary) Assert.True(s.Apply(new SellTower(s.Towers[^1].Id)).IsOk);
    }

    /// <summary>Passa até o evento <paramref name="type"/>; devolve os eventos do passo em que ocorreu.</summary>
    private static List<TdEvent> StepUntil(TdMatchState s, TdEventType type, int max = 100_000)
    {
        for (int i = 0; i < max; i++)
        {
            s.ClearEvents();
            s.Step();
            if (s.Events.Any(e => e.Type == type)) return [.. s.Events];
        }
        throw new Xunit.Sdk.XunitException("evento " + type + " não ocorreu");
    }

    [Fact]
    public void Default_options_are_continuous_and_persistent_and_legacy_create_is_classic()
    {
        Assert.True(TdMatchOptions.Default.ContinuousWaves);
        Assert.True(TdMatchOptions.Default.PersistentRun);
        Assert.Equal(5, TdMatchOptions.Default.StageClearHeal);
        Assert.True(TdMatchState.Create(Base, 1, Rng.Seed(1, 54)).Options.IsClassic);
    }

    [Fact]
    public void Next_wave_starts_in_the_same_step_the_previous_one_ends()
    {
        var s = New(Tiny(3), 1);
        Armed(s);
        StartWithTemporaryTowerIfNeeded(s);
        var ev = StepUntil(s, TdEventType.WaveCompleted);
        Assert.Contains(ev, e => e is { Type: TdEventType.WaveCompleted, A: 1 });
        Assert.Contains(ev, e => e is { Type: TdEventType.WaveStarted, A: 2 });
        Assert.Equal(MatchPhase.Onda, s.Phase); // nunca passa por Preparo
        Assert.Equal(2, s.WaveNumber);
        Assert.Single(s.ActiveWaves);
    }

    [Fact]
    public void Classic_mode_still_returns_to_manual_preparo()
    {
        var s = New(Tiny(3), 1, TdMatchOptions.Classic);
        Armed(s);
        StartWithTemporaryTowerIfNeeded(s);
        StepUntil(s, TdEventType.WaveCompleted);
        Assert.Equal(MatchPhase.Preparo, s.Phase);
        Assert.Equal(1, s.WaveNumber);
    }

    [Fact]
    public void Clearing_stage_keeps_towers_runes_and_credits_and_heals_core()
    {
        var s = New(Tiny(2), 1);
        Armed(s);
        Assert.True(s.Apply(new BuildTower("bolt", 900)).IsOk);
        var towers = s.Towers.ToArray();
        long fragments = s.Fragments;
        long creditsBefore = s.Credits;
        StartWithTemporaryTowerIfNeeded(s);
        var ev = StepUntil(s, TdEventType.StageCleared);
        var cleared = ev.Single(e => e.Type == TdEventType.StageCleared);
        Assert.Equal(1, cleared.A);
        Assert.Equal(1, s.StagesCleared);
        Assert.Equal(2, s.StageNumber);
        Assert.Equal(2, s.StageId);
        Assert.Equal(0, s.Cycle);
        Assert.Equal(MatchPhase.Onda, s.Phase);
        Assert.Equal(1, s.WaveNumber); // onda 1 da fase 2 no mesmo passo
        Assert.Contains(ev, e => e is { Type: TdEventType.WaveStarted, A: 1 });
        Assert.Equal(towers.Select(t => (t.Id, t.X, t.DefIndex, t.Placed)), s.Towers.Select(t => (t.Id, t.X, t.DefIndex, t.Placed)));
        Assert.Equal(fragments + 2, s.Fragments); // a teia e os fragmentos acompanham a corrida (+1 por onda vencida, 2 ondas)
        Assert.True(s.Credits >= creditsBefore); // só bônus de onda entram; nada é recriado
        Assert.True(s.Credits > Base.Stages.Stages[1].StartingCredits);
        Assert.Equal(0, s.ReputationLostTotal); // coroa: nada vazou
        var sum = s.StageResults.Single();
        Assert.Equal((1, 1, 0, 3), (sum.StageNumber, sum.StageId, sum.Cycle, sum.Stars));
        Assert.True(sum.Crown);
        Assert.Equal(sum.Score, cleared.B);
        Assert.True(sum.Score > 0);
    }

    [Fact]
    public void Stage_clear_heals_up_to_five_after_leaks()
    {
        var s = New(Tiny(2), 1);
        Armed(s); // sem torres: o grunt vaza
        StartWithTemporaryTowerIfNeeded(s);
        StepUntil(s, TdEventType.StageCleared);
        var sum = s.StageResults.Single();
        Assert.True(sum.IntegrityLost > 0);
        Assert.False(sum.Crown);
        Assert.True(s.ReputationLostTotal > 0); // vazou: perdeu XP, sem game over
    }

    [Fact]
    public void Last_stage_continues_into_scaled_infinite_cycle()
    {
        var s = New(Tiny(1, stage: 6), 6);
        Armed(s);
        StartWithTemporaryTowerIfNeeded(s);
        StepUntil(s, TdEventType.StageCleared);
        Assert.Equal((7, 1, 1), (s.StageNumber, s.StageId, s.Cycle));
        Assert.Equal(MatchPhase.Onda, s.Phase);
        Assert.Equal(6, s.StageResults.Single().StageId);
        while (s.Enemies.Count == 0) s.Step();
        double grunt = Base.Enemies.Enemies[s.Enemies[0].DefIndex].Hp;
        Assert.Equal(grunt * (1.0 + Base.Stages.Rules.HpStageStep * 6) * TdMatchState.CycleHpGrowth, s.Enemies[0].MaxHp, 6);
        Assert.True(s.Towers.Count == 0 && s.Phase == MatchPhase.Onda);
    }

    [Fact]
    public void Persistent_without_continuous_returns_to_preparo_of_next_stage()
    {
        var s = New(Tiny(1), 1, new TdMatchOptions(false, true, 5));
        Armed(s);
        StartWithTemporaryTowerIfNeeded(s);
        StepUntil(s, TdEventType.StageCleared);
        Assert.Equal((MatchPhase.Preparo, 2, 0), (s.Phase, s.StageNumber, s.WaveNumber));
    }

    [Fact]
    public void Bonus_question_can_be_answered_in_any_phase_of_any_mode_per_offered_question()
    {
        var s = New(Tiny(3), 1);
        Armed(s);
        StartWithTemporaryTowerIfNeeded(s);
        long before = s.Credits;
        Assert.True(s.Apply(new AnswerBonus(1, true)).IsOk);
        Assert.Equal(before + s.BonusCreditsFor(1), s.Credits);
        Assert.True(s.Apply(new AnswerBonus(2, true)).IsOk); // TD.30b: não é mais 1 por onda
        StepUntil(s, TdEventType.WaveStarted); // onda 2
        Assert.True(s.Apply(new AnswerBonus(3, false)).IsOk);
        var classic = New(Tiny(3), 1, TdMatchOptions.Classic);
        StartWithTemporaryTowerIfNeeded(classic);
        Assert.True(classic.Apply(new AnswerBonus(1, true)).IsOk); // clássico também em Onda
    }

    [Fact]
    public void Stage_cleared_event_survives_in_continuous_run()
    {
        var s = New(Tiny(2), 1);
        Armed(s);
        Assert.True(s.Apply(new SetPresence(true)).IsOk);
        StartWithTemporaryTowerIfNeeded(s);
        StepUntil(s, TdEventType.StageCleared);
        Assert.Contains(s.Events, e => e.Type == TdEventType.StageCleared);
    }

    private static TdReplayResult Replay(TdMatchOptions? o, int stage = 1)
    {
        var log = new List<TdLogEntry>
        {
            new(0, new LayoutChanged(TdSanityBot.TypicalLayout())),
            new(0, new BuildTower("bolt", 900), 1),
            new(0, new StartWave(), 2),
            new(40, new AnswerBonus(1, true)),
        };
        return TdReplayRunner.Run(Tiny(2), new TdReplayScript(stage, 7, 54, log, 4000, o));
    }

    [Fact]
    public void Replay_is_deterministic_and_options_change_the_hash()
    {
        var a = Replay(TdMatchOptions.Default);
        Assert.Equal(a, Replay(TdMatchOptions.Default));
        Assert.Equal(0, a.CommandsRejected);
        Assert.Equal(MatchPhase.Onda, a.Phase); // infinito: nunca vence, segue na fase 2+
        Assert.NotEqual(a.StateHash, Replay(TdMatchOptions.Classic).StateHash);
        Assert.Equal(Replay(null), Replay(TdMatchOptions.Classic)); // log antigo = clássico
    }

    [Fact]
    public void Classic_hash_does_not_include_run_fields()
    {
        var c = New(Base, 1, TdMatchOptions.Classic);
        var legacy = TdMatchState.Create(Base, 1, Rng.Seed(42, 54));
        Assert.Equal(legacy.ComputeStateHash(), c.ComputeStateHash());
        Assert.NotEqual(legacy.ComputeStateHash(), New(Base, 1).ComputeStateHash());
    }

    [Fact]
    public void Sanity_bot_run_is_deterministic_and_clears_stage_one_with_kept_towers()
    {
        var content = TdContentLoader.LoadEmbedded();
        // BAL-1: o roteiro do bot (torres sem fusão) já não zera a campanha; mede a primeira fase, com chefe.
        var a = TdSanityBot.RunContinuous(content, 1, TdBotProfile.Otimizado, 100, 1, stopAtCollapse: false);
        var b = TdSanityBot.RunContinuous(content, 1, TdBotProfile.Otimizado, 100, 1, stopAtCollapse: false);
        Assert.Equal(a.StateHash, b.StateHash);
        Assert.Equal(1, a.StagesCleared);
        Assert.Single(a.Stages);
        Assert.True(a.TowersAtEnd >= 8, $"{a.TowersAtEnd} torres");
        Assert.Equal(2, a.StageNumber);
    }

    [Fact]
    public void Sanity_bot_run_novato_cannot_sustain_the_run()
    {
        var content = TdContentLoader.LoadEmbedded();
        var r = TdSanityBot.RunContinuous(content, 1, TdBotProfile.SemContra, 100, 6);
        Assert.True(r.Defeated);
        Assert.True(r.StagesCleared < 6);
    }

    [Fact]
    public void Sanity_bot_run_novato_falls_early()
    {
        var content = TdContentLoader.LoadEmbedded();
        var r = TdSanityBot.RunContinuous(content, 1, TdBotProfile.Novato, 100, 6);
        Assert.True(r.Defeated);
        Assert.InRange(r.StagesCleared, 0, 3); // BAL-1: sem escolher torres nem fundir, não passa das fases iniciais
    }
}
