using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using StartWave = SideWar.Core.Td.StartWave;

namespace SideWar.Core.Tests;

/// <summary>TD.3b (RF-28.19): caixa-bomba, sinal de presença, coleta, explosão e restrição do MoveTower.</summary>
public sealed class TdBombCrateTests
{
    private static readonly TdContent Base = TdMechanicsContent.Load();

    /// <summary>Conteúdo com créditos fartos, integridade alta e a caixa sempre sorteada (chance 1), editável por <paramref name="bc"/>.</summary>
    private static TdContent Cfg(Func<TdBombCrateDef, TdBombCrateDef>? bc = null, int stageId = 1)
    {
        var rules = Base.Stages.Rules with { CoreIntegrity = 100_000, BombCrate = (bc ?? (x => x))(Base.Stages.Rules.BombCrate with { Chance = 1.0 }) };
        var stages = Base.Stages.Stages.Select(s => s with { StartingCredits = 1_000_000 }).ToArray();
        return Base with { Stages = Base.Stages with { Rules = rules, Stages = stages } };
    }

    private static StripLayout Full() =>
        StripLayout.Create(1200, 1200, 0f, 0f, 1200, TowerHost.Taskbar, [new FreeZone(0, 1200)]);

    private static TdMatchState New(TdContent c, int stage = 2, ulong seed = 7, bool present = true, params int[] towerX)
    {
        var s = TdMatchState.Create(c, stage, Rng.Seed(seed, 54));
        Assert.True(s.Apply(new LayoutChanged(Full())).IsOk);
        foreach (int x in towerX) Assert.True(s.Apply(new BuildTower("bolt", x)).IsOk);
        if (present) Assert.True(s.Apply(new SetPresence(true)).IsOk);
        return s;
    }

    private static bool StepUntil(TdMatchState s, Func<bool> cond, int max = 60_000)
    {
        for (int i = 0; i < max && !cond(); i++)
        {
            if (s.Phase is MatchPhase.Vitoria or MatchPhase.Derrota) return cond();
            s.Step();
        }
        return cond();
    }

    private static bool Saw(TdMatchState s, TdEventType t) => s.Events.Any(e => e.Type == t);

    [Fact(DisplayName = "RF-28.19 Eligibility: never in Preparo, never in waves 1-2 of stage 1, and from wave 3 of stage 1 on")]
    public void Eligibility_Stage1_FromWave3()
    {
        var s = New(Cfg(), stage: 1, towerX: [100, 300, 500, 700]);
        for (int wave = 1; wave <= 2; wave++)
        {
            Assert.True(s.Apply(new StartWave()).IsOk);
            while (s.Phase == MatchPhase.Onda) { s.Step(); Assert.False(s.CrateActive); }
            Assert.Equal(MatchPhase.Preparo, s.Phase);
            Assert.False(Saw(s, TdEventType.CrateSpawned));
        }
        for (int i = 0; i < 300; i++) { s.Step(); Assert.False(s.CrateActive); } // Preparo: nunca
        Assert.True(s.Apply(new StartWave()).IsOk);
        Assert.True(StepUntil(s, () => s.CrateActive));
        Assert.Equal(3, s.WaveNumber);
        Assert.Equal(8.0, s.CrateSecondsLeft, 1);
        // dentro do intervalo [24, 1176] (margem do JSON)
        Assert.InRange(s.CrateX, 24, 1176);
        // ao voltar a Preparo a caixa não existe mais
        Assert.True(StepUntil(s, () => s.Phase != MatchPhase.Onda));
        Assert.False(s.CrateActive);
    }

    [Fact(DisplayName = "RF-28.19 Other stages: eligible from wave 1; chance 0 never spawns")]
    public void Eligibility_OtherStages_And_ChanceZero()
    {
        var s = New(Cfg(), stage: 2, towerX: [100, 300, 500, 700]);
        Assert.True(s.Apply(new StartWave()).IsOk);
        Assert.True(StepUntil(s, () => s.CrateActive));
        Assert.Equal(1, s.WaveNumber);

        var z = New(Base with { Stages = Cfg().Stages with { Rules = Cfg().Stages.Rules with { BombCrate = Cfg().Stages.Rules.BombCrate with { Chance = 0.0 } } } }, stage: 2, towerX: [100, 300, 500, 700]);
        Assert.True(z.Apply(new StartWave()).IsOk);
        while (z.Phase == MatchPhase.Onda) { z.Step(); Assert.False(z.CrateActive); }
    }

    [Fact(DisplayName = "RF-28.19 Without presence the crate never spawns (the drop is lost without punishment); presence later in the wave lets it fall")]
    public void NoPresence_NoSpawn_ThenPresenceSpawns()
    {
        var s = New(Cfg(), stage: 2, present: false, towerX: [100, 300, 500, 700]);
        Assert.True(s.Apply(new StartWave()).IsOk);
        int towers = s.Towers.Count;
        Assert.False(StepUntil(s, () => s.CrateActive, 30 * 12)); // passa do instante de queda sem presença
        Assert.Equal(towers, s.Towers.Count);
        Assert.True(s.Apply(new SetPresence(true)).IsOk);
        Assert.True(StepUntil(s, () => s.CrateActive, 60)); // adiada: reavaliada a cada segundo
    }

    [Fact(DisplayName = "RF-28.19 Dropping without ever being present across the whole wave: nothing spawns, nothing explodes")]
    public void NoPresence_WholeWave()
    {
        var s = New(Cfg(), stage: 2, present: false, towerX: [100, 300, 500, 700]);
        Assert.True(s.Apply(new StartWave()).IsOk);
        while (s.Phase == MatchPhase.Onda) { s.Step(); Assert.False(s.CrateActive); }
        Assert.Equal(4, s.Towers.Count);
        Assert.DoesNotContain(s.Events, e => e.Type is TdEventType.CrateSpawned or TdEventType.CrateExploded);
    }

    [Fact(DisplayName = "RF-28.19 Collect pays crate credits (SDD-29) and cuts the longest ability cooldown by 20 s; wrong id is rejected")]
    public void Collect_PaysCreditsAndReducesCooldown()
    {
        var s = New(Cfg(), stage: 2, towerX: [100, 300, 500, 700]);
        Assert.True(s.Apply(new StartWave()).IsOk);
        Assert.True(s.Apply(new UseAbility("scale")).IsOk);
        Assert.True(StepUntil(s, () => s.CrateActive));
        int cdBefore = s.AbilityStatuses.First(a => a.Id == "scale").CooldownTicks;
        Assert.True(cdBefore > 20 * 30 - 1 || cdBefore > 0);
        long credits = s.Credits;
        Assert.Equal(CommandReason.NotAvailable, s.Apply(new CollectCrate(s.CrateId + 5)).Reason);
        int id = s.CrateId;
        Assert.True(s.Apply(new CollectCrate(id)).IsOk);
        Assert.False(s.CrateActive);
        Assert.Equal(credits + s.Content.Runes.CreditSources.CrateBase + s.Content.Runes.CreditSources.CratePerWave * s.WaveNumber, s.Credits);
        Assert.Equal(Math.Max(0, cdBefore - 20 * 30), s.AbilityStatuses.First(a => a.Id == "scale").CooldownTicks);
        Assert.Contains(s.Events, e => e.Type == TdEventType.CrateCollected && e.A == id);
        Assert.Equal(CommandReason.NotAvailable, s.Apply(new CollectCrate(id)).Reason);
        // mesmo sem recargas a pagar: créditos pagos (segunda caixa em onda posterior não é necessária; checado acima com recarga)
        Assert.Equal(4, s.Towers.Count);
    }

    [Fact(DisplayName = "RF-28.19 Collect without cooldowns running still pays credits")]
    public void Collect_NoCooldown_StillPays()
    {
        var s = New(Cfg(), stage: 2, towerX: [100, 300, 500, 700]);
        Assert.True(s.Apply(new StartWave()).IsOk);
        Assert.True(StepUntil(s, () => s.CrateActive));
        long credits = s.Credits;
        Assert.True(s.Apply(new CollectCrate(s.CrateId)).IsOk);
        Assert.Equal(credits + s.Content.Runes.CreditSources.CrateBase + s.Content.Runes.CreditSources.CratePerWave * s.WaveNumber, s.Credits);
    }

    [Fact(DisplayName = "RF-28.19 Expiry explodes and destroys the nearest PLACED tower (ties: lowest id), with no refund; unplaced towers are ignored")]
    public void Expiry_DestroysNearestPlaced_NoRefund()
    {
        var s = New(Cfg(), stage: 2, towerX: [100, 1100]);
        Assert.True(s.Apply(new StartWave()).IsOk);
        Assert.True(StepUntil(s, () => s.CrateActive));
        int cx = s.CrateX;
        int expected = s.Towers.OrderBy(t => Math.Abs(t.X - cx)).ThenBy(t => t.Id).First().Id;
        Assert.Equal(expected, s.CrateTargetTowerId);
        int id = s.CrateId;
        Assert.True(StepUntil(s, () => !s.CrateActive, 30 * 9));
        Assert.Single(s.Towers);
        Assert.DoesNotContain(s.Towers, t => t.Id == expected);
        Assert.Contains(s.Events, e => e.Type == TdEventType.CrateExploded && e.A == id && e.B == expected);
    }

    [Fact(DisplayName = "RF-28.19 The exploded tower is refunded nothing: credits do not jump; unplaced towers are never the target")]
    public void Explosion_SkipsUnplaced_And_NoRefund()
    {
        var s = New(Cfg(), stage: 2, towerX: [100, 1100]);
        Assert.True(s.Apply(new StartWave()).IsOk);
        // a barra encolhe: a torre em 100 perde a posição (SEM POSIÇÃO) e a de 1100 continua
        Assert.True(s.Apply(new LayoutChanged(StripLayout.Create(1200, 1200, 0f, 0f, 1200, TowerHost.Taskbar, [new FreeZone(900, 1200)]))).IsOk);
        Assert.Equal(1, s.UnplacedCount);
        Assert.True(StepUntil(s, () => s.CrateActive));
        int placed = s.Towers.First(t => t.Placed).Id;
        Assert.Equal(placed, s.CrateTargetTowerId);
        long before = s.Credits;
        Assert.True(StepUntil(s, () => !s.CrateActive, 30 * 9));
        Assert.Single(s.Towers);
        Assert.False(s.Towers[0].Placed);
        Assert.True(s.Credits - before < 70); // sem reembolso de 70% da torre destruída (custo do bolt > 70)
        Assert.Equal(0, s.CrateTargetTowerId);
    }

    [Fact(DisplayName = "RF-28.19 Presence dropping with the crate active makes it vanish without exploding")]
    public void PresenceDrop_VanishesWithoutExploding()
    {
        var s = New(Cfg(), stage: 2, towerX: [100, 300, 500]);
        Assert.True(s.Apply(new StartWave()).IsOk);
        Assert.True(StepUntil(s, () => s.CrateActive));
        int id = s.CrateId;
        Assert.True(s.Apply(new SetPresence(false)).IsOk);
        Assert.False(s.CrateActive);
        Assert.Contains(s.Events, e => e.Type == TdEventType.CrateVanished && e.A == id);
        for (int i = 0; i < 30 * 10; i++) s.Step();
        Assert.Equal(3, s.Towers.Count);
        Assert.DoesNotContain(s.Events, e => e.Type == TdEventType.CrateExploded);
    }

    [Fact(DisplayName = "RF-28.19 Collect outside a wave is rejected; at most one crate at a time")]
    public void Collect_InPreparo_Rejected()
    {
        var s = New(Cfg(), stage: 2, towerX: [100]);
        Assert.Equal(CommandReason.InvalidPhase, s.Apply(new CollectCrate(1)).Reason);
        Assert.True(s.Apply(new StartWave()).IsOk);
        int max = 0;
        for (int i = 0; i < 4000 && s.Phase == MatchPhase.Onda; i++) { s.Step(); max = Math.Max(max, s.CrateActive ? 1 : 0); }
        Assert.Equal(1, max);
    }

    [Fact(DisplayName = "RF-28.19 SetPresence is idempotent and does not touch the Rng")]
    public void SetPresence_Idempotent()
    {
        var s = New(Cfg(), present: false, towerX: [100]);
        ulong rng = s.RngState;
        Assert.True(s.Apply(new SetPresence(true)).IsOk);
        ulong h = s.ComputeStateHash();
        Assert.True(s.Apply(new SetPresence(true)).IsOk);
        Assert.Equal(h, s.ComputeStateHash());
        Assert.Equal(rng, s.RngState);
    }

    [Fact(DisplayName = "TD.3b MoveTower only accepts towers WITHOUT position; placed towers are rejected")]
    public void MoveTower_OnlyUnplaced()
    {
        var s = New(Cfg(), towerX: [100, 500]);
        int a = s.Towers[0].Id, b = s.Towers[1].Id;
        Assert.Equal(CommandReason.NotUnpositioned, s.Apply(new MoveTower(a, 300)).Reason);
        Assert.Equal(CommandReason.TowerNotFound, s.Apply(new MoveTower(99, 300)).Reason);
        Assert.True(s.Apply(new LayoutChanged(StripLayout.Create(1200, 1200, 0f, 0f, 1200, TowerHost.Taskbar, [new FreeZone(0, 300)]))).IsOk);
        Assert.False(s.Towers.First(t => t.Id == b).Placed);
        Assert.True(s.Apply(new MoveTower(b, 250)).IsOk);
        Assert.True(s.Towers.First(t => t.Id == b).Placed);
    }

    [Fact(DisplayName = "TD.3b Validator rejects invalid bombCrate rules")]
    public void Validator_RejectsBadBombCrate()
    {
        Assert.Empty(TdContentValidator.Validate(Base));
        var bad = Base with { Stages = Base.Stages with { Rules = Base.Stages.Rules with { BombCrate = Base.Stages.Rules.BombCrate with { Chance = 1.5, CountdownSeconds = 0 } } } };
        var errors = TdContentValidator.Validate(bad);
        Assert.Contains(errors, e => e.Contains("bombCrate.chance"));
        Assert.Contains(errors, e => e.Contains("bombCrate.countdownSeconds"));
    }

    private static TdReplayScript Script() => new(2, 42, 54,
    [
        new TdLogEntry(0, new LayoutChanged(Full())),
        new TdLogEntry(0, new SetPresence(true)),
        new TdLogEntry(0, new BuildTower("bolt", 200)),
        new TdLogEntry(0, new BuildTower("bolt", 600)),
        new TdLogEntry(0, new BuildTower("bolt", 1000)),
        new TdLogEntry(1, new StartWave()),
        new TdLogEntry(3000, new SetPresence(false)),
        new TdLogEntry(3300, new SetPresence(true)),
    ], 9000);

    [Fact(DisplayName = "RNF-28.1 Crate in the log: same log + seed → same StateHash in 5 runs (golden)")]
    public void Replay_Deterministic5Runs()
    {
        var first = TdReplayRunner.Run(Base, Script());
        for (int i = 0; i < 4; i++) Assert.Equal(first.StateHash, TdReplayRunner.Run(Base, Script()).StateHash);
        Assert.Equal(Golden, first.StateHash);
    }

    private const ulong Golden = 588953149544461392UL;
}
