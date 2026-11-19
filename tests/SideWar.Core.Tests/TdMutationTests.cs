using System.Text;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using StartWave = SideWar.Core.Td.StartWave;
using Rng = SideWar.Core.Simulation.Rng;

namespace SideWar.Core.Tests;

/// <summary>TD.37c (SDD-37 §4, CA-37.4, 37.5 e 37.7): mutações combináveis de inimigos no Core.</summary>
public sealed class TdMutationTests
{
    private static readonly TdContent Real = TdContentLoader.LoadEmbedded();

    private static TdMutationTraitDef Trait(string id, string label, double hp = 1, double speed = 1, double armor = 0, double shield = 0, bool fly = false, params string[] excl) =>
        new(id, label, label, 1, hp, speed, armor, shield, fly, excl);

    private static TdMutationTraitDef Cat(string id) => Real.Mutations.Traits.Single(t => t.Id == id);

    /// <summary>Degrau único sempre ativo (fase 1, onda 0), até 30% mutados.</summary>
    private static TdMutationsDef Mut(int maxDoubles, int gap, params TdMutationTraitDef[] traits) =>
        Real.Mutations with { Traits = traits, DoubleGapWaves = gap, Tiers = [new TdMutationTierDef(1, 0, 0.3, maxDoubles, 0)] };

    /// <summary>Todas as fases com <paramref name="waves"/> ondas iguais de <paramref name="count"/> do inimigo, integridade enorme (só vazamento).</summary>
    private static TdContent Rig(TdMutationsDef mut, string enemy, int count, int waves = 1, Func<TdEnemyDef, TdEnemyDef>? edit = null, bool rest = false)
    {
        var defs = Enumerable.Range(1, waves).Select(i => new TdWaveDef(i, rest, 1.0, [new TdWaveGroupDef(enemy, count, 0.0, 0.5)])).ToArray();
        return Real with
        {
            Mutations = mut,
            Enemies = edit is null ? Real.Enemies : Real.Enemies with { Enemies = Real.Enemies.Enemies.Select(edit).ToArray() },
            Stages = Real.Stages with
            {
                Rules = Real.Stages.Rules with { CoreIntegrity = 1_000_000 },
                Stages = Real.Stages.Stages.Select(s => s with { Waves = waves, StartingCredits = 100000 }).ToArray(),
            },
            Waves = new TdWavesDef(Real.Stages.Stages.Select(s => new TdStageWavesDef(s.Id, defs)).ToArray()),
        };
    }

    private static TdMatchState New(TdContent c, int stage = 6, ulong seed = 7, TdMatchOptions? o = null) => TdMatchState.Create(c, stage, Rng.Seed(seed, 54), null, o ?? TdMatchOptions.Default);

    private sealed record Wave(int Abs, int Stage, TdSpawn[] Agenda, IReadOnlyList<TdMutationPreview> Preview);
    private sealed record Seen(int Wave, TdEnemy Enemy);

    /// <summary>Roda até <paramref name="target"/> ondas iniciadas e todos os spawns dela nascidos; colhe agendas (com a prévia feita antes) e inimigos mutados.</summary>
    private static (List<Wave> Waves, List<Seen> Mutated, TdMatchState State) Run(TdContent c, int target, int stage = 6, ulong seed = 7, TdMatchOptions? o = null)
    {
        var s = New(c, stage, seed, o);
        var waves = new List<Wave>();
        var mutated = new List<Seen>();
        var ids = new HashSet<int>();
        IReadOnlyList<TdMutationPreview> prev = s.PreviewNextWaveMutations();
        Assert.True(s.Apply(new StartWave()).IsOk);
        int guard = 0;
        while (guard++ < 3_000_000)
        {
            if (waves.Count < s.RunWavesStarted)
            {
                var w = s.ActiveWaves[^1];
                waves.Add(new Wave(s.RunWavesStarted, s.StageNumber, [.. w.Agenda], prev));
                prev = s.PreviewNextWaveMutations();
            }
            foreach (var e in s.Enemies)
                if (e.Mutations != 0 && ids.Add(e.Id)) mutated.Add(new Seen(e.Wave, e));
            if (s.Phase == MatchPhase.Derrota || (s.RunWavesStarted >= target && s.ActiveWaves.All(w => w.SpawnsDone))) break;
            s.Step();
        }
        Assert.NotEqual(MatchPhase.Derrota, s.Phase);
        return (waves, mutated, s);
    }

    private static string Label(TdMatchState s, ushort mask) => string.Join("+", s.MutationTraitIds(mask));

    // ---------------------------------------------------------------- conteúdo

    [Fact(DisplayName = "RF-37.12 mutation content: embedded is valid, enters the ContentHash and the validator rejects bad catalogs and tiers")]
    public void Content_IsValidatedAndHashed()
    {
        Assert.Contains("td-mutations.json", TdContentLoader.FileNames);
        var files = TdContentLoader.ReadEmbedded();
        var changed = new Dictionary<string, byte[]>(files) { ["td-mutations.json"] = Encoding.UTF8.GetBytes(" " + Encoding.UTF8.GetString(files["td-mutations.json"])) };
        Assert.NotEqual(TdContentLoader.ComputeHash(files), TdContentLoader.ComputeHash(changed));
        Assert.Equal(["fast", "durable", "flying", "slow", "shielded", "armored"], Real.Mutations.Traits.Select(t => t.Id));
        Assert.Equal(3, Real.Mutations.Tiers.Length);

        static TdContentLoadResult With(string find, string replace)
        {
            var f = TdContentLoader.ReadEmbedded();
            var text = Encoding.UTF8.GetString(f["td-mutations.json"]);
            Assert.Contains(find, text);
            f["td-mutations.json"] = Encoding.UTF8.GetBytes(text.Replace(find, replace, StringComparison.Ordinal));
            return TdContentLoader.Load(f);
        }
        static void Bad(TdContentLoadResult r, string fragment) { Assert.False(r.IsValid); Assert.Contains(r.Errors, x => x.Contains(fragment, StringComparison.Ordinal)); }
        Bad(With("\"id\": \"slow\"", "\"id\": \"fast\""), "duplicado");
        Bad(With("\"excludes\": [\"slow\"]", "\"excludes\": []"), "simétrica");
        Bad(With("\"excludes\": [\"fast\"]", "\"excludes\": [\"nada\"]"), "inexistente");
        Bad(With("\"hpMult\": 1.7", "\"hpMult\": 0"), "hpMult");
        Bad(With("\"maxMutatedPct\": 0.30", "\"maxMutatedPct\": 0.5"), "70%");
        Bad(With("\"speedMax\": 120", "\"speedMax\": 10"), "speedMax");
        Bad(With("\"rewardCap\": 1.3", "\"rewardCap\": 0.5"), "rewardCap");
        Bad(With("\"doublePct\": 0.25", "\"doublePct\": 0.9"), "doublePct");
        Bad(With("\"minWaves\": 45", "\"minWaves\": 5"), "crescentes");
    }

    // ---------------------------------------------------------------- combos (CA-37.4)

    [Fact(DisplayName = "CA-37.4 Tank Rápido: fast on a tank = native tank HP, speed x1.5, same flying/armor, reward x1.15 floored")]
    public void TankFast()
    {
        var c = Rig(Mut(0, 5, Cat("fast")), "tank", 10);
        var (_, mutated, s) = Run(c, 1);
        Assert.Equal(3, mutated.Count); // 30% de 10
        var tank = c.Enemies.Enemies.Single(e => e.Id == "tank");
        double stageHp = tank.Hp * (1 + c.Stages.Rules.HpStageStep * 5); // fase 6, onda 1
        foreach (var m in mutated)
        {
            Assert.Equal(["fast"], s.MutationTraitIds(m.Enemy.Mutations));
            Assert.Equal(tank.Speed * 1.5, m.Enemy.Speed);
            Assert.Equal(stageHp, m.Enemy.MaxHp, 6); // sem durable: HP nativo do tank
            Assert.False(m.Enemy.Flying);
            Assert.Equal(tank.Armor, m.Enemy.Armor);
            Assert.Equal((long)Math.Floor(tank.Gold * 1.15), m.Enemy.Gold);
            Assert.Equal(tank.CoreDamage, m.Enemy.CoreDamage);
            Assert.Equal("Tráfego pesado Rápido", s.MutatedName(m.Enemy.DefIndex, m.Enemy.Mutations));
        }
    }

    [Fact(DisplayName = "CA-37.4 native traits are never added again: tank+durable, flyer+flying, trojan+shielded, armored+armored, and fast on a speed-capped base")]
    public void NativeTraits_NotDuplicated()
    {
        Assert.Empty(Run(Rig(Mut(0, 5, Cat("durable")), "tank", 10), 1).Mutated);
        Assert.Empty(Run(Rig(Mut(0, 5, Cat("flying")), "flyer", 10), 1).Mutated);
        Assert.Empty(Run(Rig(Mut(0, 5, Cat("shielded")), "trojan", 10), 1).Mutated);
        Assert.Empty(Run(Rig(Mut(0, 5, Cat("armored")), "armored", 10), 1).Mutated);
        Assert.Empty(Run(Rig(Mut(0, 5, Cat("fast")), "runner", 10), 1).Mutated);
        // Sem efeito: fast numa base que já está no teto de velocidade (120) não é aplicado.
        Assert.Empty(Run(Rig(Mut(0, 5, Cat("fast")), "grunt", 10, edit: e => e.Id == "grunt" ? e with { Speed = 120 } : e), 1).Mutated);
    }

    [Fact(DisplayName = "CA-37.4 Voador Tank Lento: flyer + durable + slow keeps flying, HP x1.7, speed x0.7, reward capped at x1.3")]
    public void FlyerTankSlow()
    {
        var (waves, mutated, s) = Run(Rig(Mut(1, 1, Cat("durable"), Cat("slow")), "flyer", 12, waves: 14), 14);
        var doubles = mutated.Where(m => s.MutationTraitIds(m.Enemy.Mutations).Count == 2).ToList();
        Assert.NotEmpty(doubles);
        var flyer = Real.Enemies.Enemies.Single(e => e.Id == "flyer");
        foreach (var d in doubles)
        {
            Assert.Equal(["durable", "slow"], s.MutationTraitIds(d.Enemy.Mutations));
            Assert.True(d.Enemy.Flying);
            Assert.Equal(flyer.Speed * 0.7, d.Enemy.Speed, 9);
            Assert.Equal((long)Math.Floor(flyer.Gold * 1.3), d.Enemy.Gold); // 1,15 por traço: 1,3 (teto)
            Assert.Equal("Bot aéreo Tank Lento", s.MutatedName(d.Enemy.DefIndex, d.Enemy.Mutations));
            Assert.Equal(["flying", "durable", "slow"], s.EnemyTraitUnion(d.Enemy));
            var plainHp = flyer.Hp * Math.Pow(s.Content.Stages.Rules.HpWaveGrowth, d.Wave - 1) * (1 + s.Content.Stages.Rules.HpStageStep * 5); // fase 6 (14 ondas)
            Assert.Equal(plainHp * 1.7, d.Enemy.MaxHp, 3);
        }
        Assert.Equal(14, waves.Count);
    }

    [Fact(DisplayName = "CA-37.4 fast and slow are mutually exclusive, never doubled, and at most two traits are added")]
    public void FastSlow_Exclusive()
    {
        var (_, mutated, s) = Run(Rig(Mut(-1, 1, Cat("fast"), Cat("slow"), Cat("durable")), "grunt", 20, waves: 20), 20);
        Assert.NotEmpty(mutated);
        foreach (var m in mutated)
        {
            var ids = s.MutationTraitIds(m.Enemy.Mutations);
            Assert.InRange(ids.Count, 1, 2);
            Assert.False(ids.Contains("fast") && ids.Contains("slow"));
        }
    }

    [Fact(DisplayName = "CA-37.4 shielded adds an initial shield of 35% of scaled max HP only when the base has none; armored adds +12")]
    public void Shield_Armor()
    {
        var (_, sh, _) = Run(Rig(Mut(0, 5, Cat("shielded")), "grunt", 10), 1);
        Assert.NotEmpty(sh);
        foreach (var m in sh) { Assert.Equal(m.Enemy.MaxHp * 0.35, m.Enemy.ShieldHp, 9); Assert.Equal(1, m.Enemy.Mutations); }
        var (_, ar, s) = Run(Rig(Mut(0, 5, Cat("armored")), "grunt", 10), 1);
        var grunt = Real.Enemies.Enemies.Single(e => e.Id == "grunt");
        Assert.All(ar, m => Assert.Equal(grunt.Armor + 12, m.Enemy.Armor));
        // Trojan (escudo nativo) não recebe outro escudo, mas aceita os demais traços.
        var (_, tr, st) = Run(Rig(Mut(0, 5, Cat("shielded"), Cat("durable")), "trojan", 10), 1);
        Assert.NotEmpty(tr);
        Assert.All(tr, m => Assert.Equal(["durable"], st.MutationTraitIds(m.Enemy.Mutations)));
        Assert.All(tr, m => Assert.Equal(120, m.Enemy.ShieldHp)); // escudo nativo intacto
        _ = s;
    }

    [Fact(DisplayName = "CA-37.4 speed after the factors is clamped to 15-120")]
    public void Speed_Clamped()
    {
        var (_, slow, _) = Run(Rig(Mut(0, 5, Cat("slow")), "tank", 10, edit: e => e.Id == "tank" ? e with { Speed = 20 } : e), 1);
        Assert.NotEmpty(slow);
        Assert.All(slow, m => Assert.Equal(15, m.Enemy.Speed)); // 20 x 0,7 = 14 -> 15
        var (_, fast, _) = Run(Rig(Mut(0, 5, Cat("fast")), "grunt", 10, edit: e => e.Id == "grunt" ? e with { Speed = 100 } : e), 1);
        Assert.NotEmpty(fast);
        Assert.All(fast, m => Assert.Equal(120, m.Enemy.Speed)); // 100 x 1,5 = 150 -> 120
    }

    [Fact(DisplayName = "CA-37.4 bosses are never mutated, rest waves have no mutation, and replicas of a mutated Fork are born plain")]
    public void Boss_Rest_Replica()
    {
        var all = Mut(0, 5, Cat("durable"), Cat("flying"));
        Assert.Empty(Run(Rig(all, "ransomware", 10), 1).Mutated);
        Assert.Empty(Run(Rig(all, "grunt", 10, rest: true), 1).Mutated);

        // Fork mutado morre numa defesa forte: as réplicas nascem sem mutação (e sem recompensa).
        var c = Rig(Mut(0, 5, Cat("durable")), "fork", 10, edit: e => e.Id == "fork" ? e with { Hp = 30, Speed = 20 } : e);
        var s = New(c);
        Assert.True(s.Apply(new BuildTower("bolt", 1100)).IsOk);
        Assert.True(s.Apply(new StartWave()).IsOk);
        bool sawMutatedFork = false, sawReplica = false;
        for (int i = 0; i < 3000 && !sawReplica; i++)
        {
            s.Step();
            sawMutatedFork |= s.Enemies.Any(e => e.Mutations != 0 && !e.IsReplica);
            foreach (var e in s.Enemies.Where(e => e.IsReplica)) { sawReplica = true; Assert.Equal(0, e.Mutations); Assert.Equal(0, e.Gold); }
        }
        Assert.True(sawMutatedFork);
        Assert.True(sawReplica, string.Join(",", s.Enemies.Select(e => $"{e.X:F0}/{e.Hp:F0}/{e.IsReplica}")) + " tick " + s.Tick + " " + s.Towers.Count);
    }

    [Fact(DisplayName = "TD.39d escudo nativo e divisão respeitam mutações e ouro")]
    public void NewEnemies_MutationsAndReplicas()
    {
        var (_, encrypted, state) = Run(Rig(Mut(0, 5, Cat("shielded"), Cat("durable")), "encrypted", 10), 1);
        Assert.NotEmpty(encrypted);
        Assert.All(encrypted, m => { Assert.Equal(80, m.Enemy.ShieldHp); Assert.Equal(["durable"], state.MutationTraitIds(m.Enemy.Mutations)); });
        var c = Rig(Mut(0, 5, Cat("durable")), "fragment", 10, edit: e => e.Id == "fragment" ? e with { Hp = 30, Speed = 20 } : e);
        var match = New(c);
        Assert.True(match.Apply(new BuildTower("bolt", 1100)).IsOk);
        Assert.True(match.Apply(new StartWave()).IsOk);
        bool replica = false;
        for (int i = 0; i < 3000 && !replica; i++)
        {
            match.Step();
            foreach (var e in match.Enemies.Where(e => e.IsReplica))
            {
                replica = true;
                Assert.Equal(0, e.Gold);
                Assert.Equal(0, e.Mutations);
                Assert.True(e.MaxHp > 0);
            }
            Assert.True(match.Enemies.Count <= c.Stages.Rules.MaxEnemiesOnField);
        }
        Assert.True(replica);
    }

    // ---------------------------------------------------------------- interações

    [Fact(DisplayName = "CA-37.4 a mutated flying enemy is ignored by ground-only towers and hit by Raio explosivo (both); the union of traits is exposed")]
    public void Flying_Mutation_TowerTargeting()
    {
        var c = Rig(Mut(0, 5, Cat("flying")), "grunt", 10, edit: e => e.Id == "grunt" ? e with { Hp = 100000, Speed = 30 } : e);
        c = c with { Stages = c.Stages with { Stages = c.Stages.Stages.Select(s => s with { StartingCredits = 10_000_000 }).ToArray() } };
        var s = New(c);
        Assert.True(s.Apply(new BuildTower("cannon", 850)).IsOk);
        Assert.True(s.Apply(new StartWave()).IsOk);
        for (int i = 0; i < 400; i++) s.Step();
        var inRange = s.Enemies.Where(e => Math.Abs(e.X - 850) < 90).ToList();
        Assert.Contains(inRange, e => e.Mutations != 0 && e.Flying);
        Assert.All(inRange.Where(e => e.Flying), e => Assert.Equal(e.MaxHp, e.Hp)); // canhão não atinge voador
        Assert.Contains(inRange, e => !e.Flying && e.Hp < e.MaxHp);
        var f = inRange.First(e => e.Flying);
        Assert.Equal(["basic", "flying"], s.EnemyTraitUnion(f));

        // Raio explosivo (base bolt, atinge voador) + módulo canhão: o voador mutado leva dano da base.
        var h = New(c);
        Assert.True(h.Apply(new BuildTower("bolt", 850)).IsOk);
        Assert.True(h.Apply(new FuseTower(h.Towers[0].Id, "cannon")).IsOk);
        Assert.True(h.Apply(new StartWave()).IsOk);
        for (int i = 0; i < 400; i++) h.Step();
        Assert.Contains(h.Enemies, e => e.Flying && e.Mutations != 0 && e.Hp < e.MaxHp);
    }

    [Fact(DisplayName = "CA-37.4 mutated enemies keep the alert triggers working (passed last tower, risk projection uses the mutated speed)")]
    public void Alerts_WithMutatedEnemies()
    {
        var c = Rig(Mut(0, 5, Cat("fast")), "grunt", 10);
        var s = New(c);
        Assert.True(s.Apply(new StartWave()).IsOk);
        bool sawMutated = false, sawPassed = false;
        for (int i = 0; i < 2500; i++)
        {
            s.Step();
            sawMutated |= s.Enemies.Any(e => e.Mutations != 0);
            var alerts = TdAlertCatalog.Detect(s);
            sawPassed |= alerts.Any(a => a.Type == TdAlertType.PassedLastTower);
            Assert.True(s.ProjectNextStepLeakDamage() >= 0);
        }
        Assert.True(sawMutated);
        Assert.True(sawPassed);
    }

    // ---------------------------------------------------------------- progressão (CA-37.5)

    private static TdContent Mixed()
    {
        TdWaveGroupDef G(string e, int n, double off) => new(e, n, off, 0.5);
        var defs = Enumerable.Range(1, 10).Select(i => new TdWaveDef(i, i == 5, 1.0,
            [G("grunt", 6, 0), G("runner", 3, 4), G("tank", 2, 6), G("flyer", 3, 8), G("fork", 2, 10), G("trojan", 2, 12), G("phishing", 2, 14)])).ToArray();
        return Real with
        {
            Stages = Real.Stages with
            {
                Rules = Real.Stages.Rules with { CoreIntegrity = 10_000_000 },
                Stages = Real.Stages.Stages.Select(s => s with { Waves = 10, StartingCredits = 100000 }).ToArray(),
            },
            Waves = new TdWavesDef(Real.Stages.Stages.Select(s => new TdStageWavesDef(s.Id, defs)).ToArray()),
        };
    }

    private static readonly Lazy<(List<Wave> Waves, List<Seen> Mutated, TdMatchState State)> LongRun = new(() => Run(Mixed(), 100, stage: 3));

    [Fact(DisplayName = "CA-37.5 gradual entry: none before 20 waves, tiers cap the share (10/20/30%), pairs only after tier 2 and a 5-wave debut gap, rest waves untouched")]
    public void Progression_GradualEntry()
    {
        var (waves, _, s) = LongRun.Value;
        var traits = s.Content.Mutations.Traits;
        var debut = new int[traits.Length];
        int firstMutated = int.MaxValue;
        foreach (var w in waves)
        {
            int n = w.Agenda.Length;
            int mutated = w.Agenda.Count(x => x.Mutations != 0);
            int doubles = w.Agenda.Count(x => s.MutationTraitIds(x.Mutations).Count == 2);
            Assert.Equal(20, n); // nunca muda a quantidade
            Assert.True(mutated <= Math.Floor(n * 0.3)); // pelo menos 70% sem mutação
            Assert.DoesNotContain(w.Agenda, x => x.IsBoss && x.Mutations != 0);
            if (mutated > 0) firstMutated = Math.Min(firstMutated, w.Abs);
            if (w.Abs <= 20 || w.Stage < 3) Assert.Equal(0, mutated);
            if (w.Abs % 10 == 5) Assert.Equal(0, mutated); // onda de respiro (5ª de cada fase)
            int cap = w.Stage >= 7 && w.Abs > 80 ? 6 : w.Stage >= 5 && w.Abs > 45 ? 4 : 2;
            Assert.True(mutated <= cap, $"onda {w.Abs}: {mutated} > {cap}");
            if (w.Abs <= 45 || w.Stage < 5) Assert.Equal(0, doubles); // degrau 1: só um traço
            if (w.Abs > 45 && w.Stage >= 5 && !(w.Stage >= 7 && w.Abs > 80)) Assert.True(doubles <= 1);
            if (w.Stage >= 7 && w.Abs > 80) Assert.True(doubles <= Math.Floor(mutated * 0.25));
            foreach (var x in w.Agenda.Where(x => x.Mutations != 0))
            {
                var ids = s.MutationTraitIds(x.Mutations);
                Assert.InRange(ids.Count, 1, 2);
                foreach (var id in ids)
                {
                    int k = Array.FindIndex(traits, t => t.Id == id);
                    Assert.True(w.Stage >= traits[k].MinStage, $"traço {id} antes da fase {traits[k].MinStage}");
                    if (ids.Count == 2) Assert.True(debut[k] > 0 && w.Abs - debut[k] >= 5, $"dupla com {id} sem 5 ondas de estreia");
                }
            }
            for (int k = 0; k < traits.Length; k++)
                if (debut[k] == 0 && w.Agenda.Any(x => (x.Mutations & (1 << k)) != 0)) debut[k] = w.Abs;
        }
        Assert.Equal(21, firstMutated); // G >= 20: a 21ª onda é a primeira elegível
        Assert.Equal(100, waves.Count);
        Assert.Contains(waves, w => w.Agenda.Any(x => s.MutationTraitIds(x.Mutations).Count == 2));
        Assert.All(debut.Where(d => d > 0), d => Assert.True(d > 20));
    }

    [Fact(DisplayName = "CA-37.5 mutations never change quantity, enemy, interval or main RNG consumption: same agenda ticks as a run without mutations")]
    public void Mutations_DoNotChangeAgenda()
    {
        var off = Mixed() with { Mutations = Real.Mutations with { Tiers = [new TdMutationTierDef(1, 1_000_000, 0.1, 0, 0)] } };
        var on = LongRun.Value;
        var plain = Run(off, 100, stage: 3);
        Assert.Equal(on.Waves.Count, plain.Waves.Count);
        for (int i = 0; i < on.Waves.Count; i++)
        {
            Assert.Equal(plain.Waves[i].Agenda.Select(a => (a.DueTick, a.EnemyIndex, a.IsBoss)), on.Waves[i].Agenda.Select(a => (a.DueTick, a.EnemyIndex, a.IsBoss)));
            Assert.All(plain.Waves[i].Agenda, a => Assert.Equal(0, a.Mutations));
        }
        Assert.Empty(plain.Mutated);
        Assert.NotEmpty(on.Mutated);
    }

    [Fact(DisplayName = "CA-37.5 phase 1-2 and classic matches are never mutated, even with an always-on tier")]
    public void Phases12_AndClassic_Untouched()
    {
        var c = Rig(Mut(0, 5, Cat("durable")), "grunt", 10, waves: 3);
        c = c with { Mutations = c.Mutations with { Traits = [Cat("durable") with { MinStage = 3 }] } };
        var (waves, mutated, _) = Run(c, 2, stage: 1);
        Assert.Equal(2, waves.Count);
        Assert.Empty(mutated); // fases 1 e 2: o traço só vale da fase 3
        var (_, classic, _) = Run(Rig(Mut(0, 5, Cat("durable")), "grunt", 10, waves: 3), 3, stage: 6, o: TdMatchOptions.Classic);
        Assert.Empty(classic);
    }

    // ---------------------------------------------------------------- determinismo, hash, prévia (CA-37.7)

    [Fact(DisplayName = "CA-37.7 the preview made before each wave equals the agenda that is actually executed (across stages and with a 100-wave run)")]
    public void Preview_EqualsAgenda()
    {
        var (waves, _, s) = LongRun.Value;
        int checkedWaves = 0;
        for (int i = 1; i < waves.Count; i++) // a prévia da onda i foi feita ao iniciar a onda i-1
        {
            var actual = waves[i].Agenda.Where(a => a.Mutations != 0).GroupBy(a => (a.EnemyIndex, a.Mutations)).Select(g => (g.Key.EnemyIndex, g.Key.Mutations, g.Count())).OrderBy(x => x).ToList();
            var preview = waves[i].Preview.Select(p => (p.EnemyIndex, p.Traits, p.Count)).OrderBy(x => x).ToList();
            Assert.Equal(actual, preview);
            if (actual.Count > 0) checkedWaves++;
        }
        Assert.True(checkedWaves > 20);
        _ = s;
    }

    [Fact(DisplayName = "CA-37.7 same seed, content and commands give identical agendas and StateHash in 5 runs; another seed changes the plan; the preview does not touch RNG/hash")]
    public void Determinism_FiveRuns()
    {
        var c = Mixed();
        var runs = Enumerable.Range(0, 5).Select(_ => Run(c, 60, stage: 3, seed: 11)).ToList();
        var hash = runs[0].State.ComputeStateHash();
        foreach (var r in runs)
        {
            Assert.Equal(hash, r.State.ComputeStateHash());
            Assert.Equal(runs[0].Waves.Select(w => w.Agenda.Select(a => a.Mutations).ToArray()).SelectMany(x => x), r.Waves.Select(w => w.Agenda.Select(a => a.Mutations).ToArray()).SelectMany(x => x));
            Assert.Equal(runs[0].State.MutationDebuts, r.State.MutationDebuts);
        }
        var other = Run(c, 60, stage: 3, seed: 12);
        Assert.NotEqual(runs[0].Waves.SelectMany(w => w.Agenda.Select(a => a.Mutations)), other.Waves.SelectMany(w => w.Agenda.Select(a => a.Mutations)));

        var s = New(c, 3, 11);
        ulong h = s.ComputeStateHash(), rng = s.RngState;
        for (int i = 0; i < 5; i++) Assert.NotNull(s.PreviewNextWaveMutations());
        Assert.Equal(h, s.ComputeStateHash());
        Assert.Equal(rng, s.RngState);
    }

    [Fact(DisplayName = "CA-37.7 mutations enter the StateHash (enemies with shield, spawns, debuts) and the replay runner reproduces it")]
    public void Hash_IncludesMutations()
    {
        var withMut = Run(Rig(Mut(0, 5, Cat("shielded")), "grunt", 10), 1);
        var without = Run(Rig(Mut(0, 5, Cat("shielded")) with { Tiers = [new TdMutationTierDef(1, 1_000_000, 0.1, 0, 0)] }, "grunt", 10), 1);
        Assert.NotEqual(withMut.State.ComputeStateHash(), without.State.ComputeStateHash());
        Assert.Contains(withMut.State.MutationDebuts, d => d > 0);
        var c = Rig(Mut(0, 5, Cat("shielded")), "grunt", 10);
        TdReplayResult Replay() => TdReplayRunner.Run(c, new TdReplayScript(6, 9, 54, [new TdLogEntry(1, new StartWave())], 900, TdMatchOptions.Default));
        Assert.Equal(Replay(), Replay());
    }

    [Fact(DisplayName = "CA-37.7 a rejected command and a snapshot copy do not change the mutation state")]
    public void Clone_KeepsMutationState()
    {
        var s = New(Mixed(), 3);
        var copy = s.CloneForSimulation();
        Assert.Equal(s.ComputeStateHash(), copy.ComputeStateHash());
        Assert.True(s.Apply(new StartWave()).IsOk);
        Assert.Equal(1, s.RunWavesStarted);
        Assert.Equal(0, copy.RunWavesStarted);
        Assert.Equal(s.MutationDebuts.Count, copy.MutationDebuts.Count);
    }
}
