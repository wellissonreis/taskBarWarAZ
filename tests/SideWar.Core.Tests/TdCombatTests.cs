using SideWar.Core.Content.Td;
using SideWar.Core.Td;
using Xunit;
using Rng = SideWar.Core.Simulation.Rng;
using CommandReason = SideWar.Core.Simulation.CommandReason;
using StartWave = SideWar.Core.Td.StartWave;
using BuildTower = SideWar.Core.Td.BuildTower;
using SetTargeting = SideWar.Core.Td.SetTargeting;
using SetAutoStart = SideWar.Core.Td.SetAutoStart;

namespace SideWar.Core.Tests;

/// <summary>TD.3: traços, mira, controle (slow/paralisia), habilidades, chefes e determinismo do combate.</summary>
public sealed class TdCombatTests
{
    private static readonly TdContent Content = TdMechanicsContent.Load();

    private readonly record struct G(string Enemy, int Count, double Interval = 1.0, double Offset = 0.0);

    /// <summary>Arena: a fase 6 (tudo liberado) com ondas autorais simples e muitos créditos; HP da onda 1 vale ×1,5 (fase 6).</summary>
    private static TdContent Arena(params G[][] waves)
    {
        var defs = waves.Select((w, i) => new TdWaveDef(i + 1, false, 1.0,
            w.Select(g => new TdWaveGroupDef(g.Enemy, g.Count, g.Offset, g.Interval)).ToArray())).ToArray();
        var stage = Content.Stages.Stages[5] with { Waves = waves.Length, StartingCredits = 1_000_000_000 };
        return Content with
        {
            Waves = new TdWavesDef([.. Content.Waves.Stages.Take(5), new TdStageWavesDef(6, defs)]),
            Stages = Content.Stages with { Stages = [.. Content.Stages.Stages.Take(5), stage] },
        };
    }

    private static TdContent WithBoss(TdContent c, string id, Func<TdBossDef, TdBossDef> f) =>
        c with { Enemies = c.Enemies with { Bosses = c.Enemies.Bosses.Select(b => b.Id == id ? f(b) : b).ToArray() } };

    /// <summary>Troca o HP base de um inimigo comum (para o alvo sobreviver a vários tiros nos testes de dano).</summary>
    private static TdContent WithHp(TdContent c, string id, double hp) =>
        c with { Enemies = c.Enemies with { Enemies = c.Enemies.Enemies.Select(e => e.Id == id ? e with { Hp = hp } : e).ToArray() } };

    private static TdMatchState New(TdContent c, ulong seed = 1, IEnumerable<string>? meta = null) =>
        TdMatchState.Create(c, 6, Rng.Seed(seed, 54), meta);

    private static int Build(TdMatchState s, string id, int x)
    {
        Assert.True(s.Apply(new BuildTower(id, x)).IsOk, "build " + id);
        return s.Towers[^1].Id;
    }

    private static TdTower Tw(TdMatchState s, int id) => s.Towers.First(t => t.Id == id);

    private static TdEnemy FirstEnemy(TdMatchState s) => s.Enemies[0];

    private static void Go(TdMatchState s)
    {
        Assert.True(s.Apply(new StartWave()).IsOk);
    }

    private static void Steps(TdMatchState s, int n)
    {
        for (int i = 0; i < n; i++) s.Step();
    }

    private static void Until(TdMatchState s, Func<bool> cond, int max = 6000)
    {
        int n = 0;
        while (!cond() && n++ < max) s.Step();
        Assert.True(cond(), "condição não atingida");
    }

    /// <summary>Dano do primeiro tiro no primeiro inimigo (cria a onda e avança até o HP cair).</summary>
    private static double FirstHit(TdMatchState s, int enemyIndex = 0)
    {
        Go(s);
        Until(s, () => s.Enemies.Count > enemyIndex && s.Enemies[enemyIndex].Hp < s.Enemies[enemyIndex].MaxHp);
        return s.Enemies[enemyIndex].MaxHp - s.Enemies[enemyIndex].Hp;
    }

    /// <summary>Danos sucessivos recebidos pelo primeiro inimigo (cada queda de HP vira um item).</summary>
    private static List<double> Shots(TdMatchState s, int count, int maxTicks = 3000)
    {
        var list = new List<double>();
        Go(s);
        double last = -1;
        int id = -1;
        for (int i = 0; i < maxTicks && list.Count < count; i++)
        {
            s.Step();
            if (s.Enemies.Count == 0) continue;
            var e = s.Enemies.FirstOrDefault(x => x.Id == id);
            if (e.Id == 0) { e = s.Enemies[0]; id = e.Id; last = e.MaxHp; }
            if (e.Hp < last) { list.Add(last - e.Hp); last = e.Hp; }
        }
        return list;
    }

    // ---------------------------------------------------------------- ordem do dano

    [Fact(DisplayName = "T-28.5 Damage order: level damage minus armor, max(1, floor)")]
    public void Damage_Basic()
    {
        var s = New(Arena([new G("armored", 1)]));
        Build(s, "bolt", 1100);
        Assert.Equal(8.0, FirstHit(s)); // floor(14 - 6)
        var g = New(Arena([new G("grunt", 1)]));
        Build(g, "frost", 1100);
        Assert.Equal(5.0, FirstHit(g)); // frost 5 dano
        var t = New(Arena([new G("armored", 1)]));
        Build(t, "frost", 1100);
        Assert.Equal(1.0, FirstHit(t)); // max(1, floor(5 - 6))
    }

    [Fact(DisplayName = "T-28.5 cannon: splash hits neighbours (ground only), cannot target flyers; bolt can")]
    public void Cannon_SplashAndFlyers()
    {
        var s = New(Arena([new G("tank", 4, 0.1)]));
        Build(s, "cannon", 1100);
        Go(s);
        Until(s, () => s.Enemies.Count(e => e.Hp < e.MaxHp) >= 2);
        var hit = s.Enemies.Where(e => e.Hp < e.MaxHp).OrderBy(e => e.MaxHp - e.Hp).ToList();
        Assert.True(hit[0].MaxHp - hit[0].Hp < hit[^1].MaxHp - hit[^1].Hp); // splash (x0.5) < principal

        var cannon = New(Arena([new G("flyer", 1)]));
        Build(cannon, "cannon", 1100);
        Go(cannon); Steps(cannon, 120);
        Assert.Equal(cannon.Enemies[0].MaxHp, cannon.Enemies[0].Hp);
        var bolt = New(Arena([new G("flyer", 1)]));
        Build(bolt, "bolt", 1100);
        Assert.True(FirstHit(bolt) > 0);
    }

    // ---------------------------------------------------------------- gelo: lentidão, paralisia, corrosão

    // ---------------------------------------------------------------- mira

    private static int WhoIsHit(TdTargeting mode, int towerX)
    {
        // grunt (leve) nasce antes; tanque (pesado) 2 s depois, então o tanque está mais longe do núcleo (X maior)
        var s = New(Arena([new G("grunt", 1), new G("tank", 1, 1.0, 2.0)]));
        Go(s);
        Until(s, () => s.Enemies.Count == 2);
        int id = Build(s, "bolt", towerX);
        Assert.True(s.Apply(new SetTargeting(id, mode)).IsOk);
        Steps(s, 1);
        var hit = s.Enemies.Single(e => e.Hp < e.MaxHp);
        return hit.DefIndex == s.Enemies.Min(e => e.DefIndex) ? 0 : 1; // 0 = grunt (índice menor), 1 = tank
    }

    [Fact(DisplayName = "RF-28.6 targeting modes pick First/Last/Strong/Weak/Close")]
    public void Targeting_Modes()
    {
        Assert.Equal(0, WhoIsHit(TdTargeting.First, 1150));  // menor X = grunt
        Assert.Equal(1, WhoIsHit(TdTargeting.Last, 1150));   // maior X = tank
        Assert.Equal(1, WhoIsHit(TdTargeting.Strong, 1150)); // maior HP
        Assert.Equal(0, WhoIsHit(TdTargeting.Weak, 1150));   // menor HP
        Assert.Equal(0, WhoIsHit(TdTargeting.Close, 1100));  // grunt mais perto da torre em 1100
        Assert.Equal(1, WhoIsHit(TdTargeting.Close, 1200));  // tank (X≈1200) mais perto de 1200
    }

    [Fact(DisplayName = "RF-28.6 default targeting comes from the content; SetTargeting works in Preparo and is rejected for non-attackers")]
    public void Targeting_DefaultsAndRejections()
    {
        var s = New(Arena([new G("grunt", 1)]));
        int bolt = Build(s, "bolt", 500);
        int cannon = Build(s, "cannon", 600);
        int bal = Build(s, "balancer", 700);
        int ss = Build(s, "scaleset", 800);
        Assert.Equal((byte)TdTargeting.First, Tw(s, bolt).Targeting);
        Assert.Equal((byte)TdTargeting.Strong, Tw(s, cannon).Targeting);
        Assert.True(s.Apply(new SetTargeting(bolt, TdTargeting.Close)).IsOk);
        Assert.Equal((byte)TdTargeting.Close, Tw(s, bolt).Targeting);
        Assert.Equal(CommandReason.NotAvailable, s.Apply(new SetTargeting(bal, TdTargeting.Last)).Reason);
        Assert.Equal(CommandReason.NotAvailable, s.Apply(new SetTargeting(ss, TdTargeting.Last)).Reason);
        Assert.Equal(CommandReason.TowerNotFound, s.Apply(new SetTargeting(99, TdTargeting.Last)).Reason);
    }

    // ---------------------------------------------------------------- balanceador

    [Fact(DisplayName = "T-28.5 balancer: node holds a ground enemy and damages it; node breaks and goes on cooldown")]
    public void Balancer_NodeHoldsAndBreaks()
    {
        var s = New(Arena([new G("grunt", 1)]));
        int id = Build(s, "balancer", 1100);
        Go(s);
        Until(s, () => s.Nodes.Count == 1);
        double x = s.Enemies[0].X;
        Steps(s, 20);
        Assert.Equal(x, s.Enemies[0].X); // retido
        Assert.True(s.Enemies[0].Hp < s.Enemies[0].MaxHp);
        Until(s, () => s.Nodes.Count == 0);               // quebra sob 1 inimigo (pressão 10/s em 30 de vida)
        Assert.True(Tw(s, id).NodeCdTicks > 0);
        Assert.Contains(s.Events, e => e.Type == TdEventType.NodeEnded);
    }

    [Fact(DisplayName = "T-28.5 balancer ignores flyers")]
    public void Balancer_IgnoresFlyers()
    {
        var s = New(Arena([new G("flyer", 1)]));
        Build(s, "balancer", 1100);
        Go(s);
        Steps(s, 100);
        Assert.Empty(s.Nodes);
        Assert.True(s.Enemies[0].X < 1200 - 55.0 / 30 * 90);
    }

    [Fact(DisplayName = "T-28.5 selling a balancer removes its nodes")]
    public void Balancer_SellRemovesNodes()
    {
        var s = New(Arena([new G("tank", 1)]));
        int id = Build(s, "balancer", 1100);
        Go(s);
        Until(s, () => s.Nodes.Count == 1);
        Assert.True(s.Apply(new SellTower(id)).IsOk);
        Assert.Empty(s.Nodes);
    }

    // ---------------------------------------------------------------- scaleset

    [Fact(DisplayName = "T-28.5 scaleset: base aura +5% cadence per nearby support tower; out of range gives nothing (branch/capstone effects move to runes, TD.21)")]
    public void Scaleset_BaseAura()
    {
        var plain = New(Arena([new G("grunt", 1)]));
        int ip = Build(plain, "bolt", 1100);
        FirstHit(plain);
        Assert.Equal(18, Tw(plain, ip).CooldownTicks);

        var near = New(Arena([new G("grunt", 1)]));
        int inr = Build(near, "bolt", 1100);
        Build(near, "scaleset", 1150);
        Build(near, "scaleset", 1160);
        FirstHit(near);
        Assert.Equal(17, Tw(near, inr).CooldownTicks); // ceil(0,6/1,10*30) = 17

        var far = New(Arena([new G("grunt", 1)]));
        int iff = Build(far, "bolt", 1100);
        Build(far, "scaleset", 900);
        Build(far, "scaleset", 880);
        FirstHit(far);
        Assert.Equal(18, Tw(far, iff).CooldownTicks); // fora da aura
    }

    // ---------------------------------------------------------------- traços

    [Fact(DisplayName = "CA-28.5 regen: heals 4%/s only after 2 s without damage")]
    public void Trait_Regen()
    {
        var s = New(Arena([new G("regen", 1)]));
        int id = Build(s, "bolt", 1100);
        Go(s);
        Until(s, () => s.Enemies.Count > 0 && s.Enemies[0].Hp < s.Enemies[0].MaxHp);
        Assert.True(s.Apply(new SellTower(id)).IsOk);
        double hp = s.Enemies[0].Hp;
        Steps(s, 59);
        Assert.Equal(hp, s.Enemies[0].Hp);
        Steps(s, 30);
        Assert.True(s.Enemies[0].Hp > hp);
        Assert.True(s.Enemies[0].Hp <= s.Enemies[0].MaxHp);
    }

    [Fact(DisplayName = "CA-28.5 drain: leech disables a tower within 80 units for 3 s; tower out of range is untouched")]
    public void Trait_Drain()
    {
        var s = New(Arena([new G("leech", 1)]));
        int near = Build(s, "bolt", 1150);
        int far = Build(s, "bolt", 1000);
        Go(s);
        Until(s, () => Tw(s, near).DisabledTicks > 0);
        Assert.Equal(0, Tw(s, far).DisabledTicks);
        Assert.Contains(s.Events, e => e.Type == TdEventType.TowerDisabled);
        // a torre desativada não atira durante os 3 s
        double hp = s.Enemies[0].Hp;
        Assert.True(Tw(s, near).DisabledTicks >= 85);
        Assert.Equal(0, Tw(s, near).ShotCount);
        Assert.Equal(hp, s.Enemies[0].Hp);
    }

    [Fact(DisplayName = "CA-28.5 trait matrix: every enemy has a counter and the trait flags are consistent")]
    public void Traits_Matrix()
    {
        foreach (var e in Content.Enemies.Enemies)
            Assert.NotEmpty(e.Counters);
        Assert.Contains("fast", Content.Enemies.Enemies.Single(e => e.Id == "runner").Traits);
        Assert.True(Content.Enemies.Enemies.Single(e => e.Id == "runner").Speed > Content.Enemies.Enemies.Single(e => e.Id == "grunt").Speed * 1.5);
        Assert.True(Content.Enemies.Enemies.Single(e => e.Id == "tank").Hp > Content.Enemies.Enemies.Single(e => e.Id == "grunt").Hp * 5);
    }

    // ---------------------------------------------------------------- habilidades

    [Fact(DisplayName = "RF-28.9 UseAbility: Preparo, locked, unknown and cooldown rejections; scale +50% cadence for 8 s")]
    public void Ability_Scale()
    {
        var s = New(WithHp(Arena([new G("grunt", 1)]), "grunt", 100000));
        int id = Build(s, "bolt", 1100);
        Assert.Equal(CommandReason.InvalidPhase, s.Apply(new UseAbility("scale")).Reason);
        Go(s);
        Assert.Equal(CommandReason.NotAvailable, s.Apply(new UseAbility("nope")).Reason);
        Assert.Equal(CommandReason.NotAvailable, s.Apply(new UseAbility("snapshot")).Reason); // exige nó da árvore
        Assert.True(s.Apply(new UseAbility("scale")).IsOk);
        var st = s.AbilityStatuses.Single(a => a.Id == "scale");
        Assert.Equal(240, st.ActiveTicks);
        Assert.Equal(1800, st.CooldownTicks);
        Assert.Equal(CommandReason.NotAvailable, s.Apply(new UseAbility("scale")).Reason);
        Until(s, () => Tw(s, id).CooldownTicks > 0);
        Assert.Equal(12, Tw(s, id).CooldownTicks); // 0,6/1,5*30
        Steps(s, 240);
        Assert.Equal(0, s.AbilityStatuses.Single(a => a.Id == "scale").ActiveTicks);
    }

    [Fact(DisplayName = "RF-28.9 abilities locked by stage: firewall needs stage 3")]
    public void Ability_LockedByStage()
    {
        var s = TdMatchState.Create(Content, 1, Rng.Seed(1, 1));
        s.Apply(new StartWave());
        Assert.Equal(CommandReason.NotAvailable, s.Apply(new UseAbility("firewall")).Reason);
        Assert.False(s.AbilityStatuses.Single(a => a.Id == "firewall").Unlocked);
        Assert.True(s.AbilityStatuses.Single(a => a.Id == "scale").Unlocked);
    }

    [Fact(DisplayName = "RF-28.9 firewall: 50% slow (20% on bosses; Zero-day immune)")]
    public void Ability_Firewall()
    {
        double Delta(string enemy, bool firewall)
        {
            var s = New(Arena([new G(enemy, 1)]));
            Go(s);
            Steps(s, 2);
            if (firewall) Assert.True(s.Apply(new UseAbility("firewall")).IsOk);
            double x = s.Enemies[0].X;
            s.Step();
            return x - s.Enemies[0].X;
        }
        Assert.Equal(40.0 / 30 * 0.5, Delta("grunt", true), 9);
        Assert.Equal(20.0 / 30 * 0.8, Delta("ransomware", true), 9);
        Assert.Equal(20.0 / 30, Delta("zeroday", true), 9);
        Assert.Equal(40.0 / 30, Delta("grunt", false), 9);
    }

    [Fact(DisplayName = "RF-28.9 snapshot: restores 3 integrity once per stage; needs the meta node and missing integrity")]
    public void Ability_Snapshot()
    {
        var s = New(Arena([new G("grunt", 2, 0.5)]), 1, ["operation.snapshot"]);
        Go(s);
        Assert.Equal(CommandReason.NotAvailable, s.Apply(new UseAbility("snapshot")).Reason); // integridade cheia
        Until(s, () => s.Integrity < s.IntegrityMax, 4000);
        int lost = s.IntegrityMax - s.Integrity;
        Assert.True(s.Apply(new UseAbility("snapshot")).IsOk);
        Assert.Equal(Math.Min(s.IntegrityMax, s.IntegrityMax - lost + 3), s.Integrity);
        Assert.Equal(CommandReason.AlreadyUsed, s.Apply(new UseAbility("snapshot")).Reason);
        Assert.Equal(0, s.AbilityStatuses.Single(a => a.Id == "snapshot").UsesLeft);
    }

    [Fact(DisplayName = "RF-28.3 CallNextWave applies the cooldown reduction to running ability cooldowns")]
    public void Ability_CooldownReductionOnCall()
    {
        var s = New(Arena([new G("grunt", 1)], [new G("grunt", 1)]));
        Go(s);
        Assert.True(s.Apply(new UseAbility("scale")).IsOk);
        Steps(s, 5);
        int before = s.AbilityStatuses.Single(a => a.Id == "scale").CooldownTicks;
        long red0 = s.CooldownReductionTicks;
        Assert.True(s.Apply(new CallNextWave()).IsOk);
        long delta = s.CooldownReductionTicks - red0;
        Assert.True(delta > 0);
        Assert.Equal(before - delta, s.AbilityStatuses.Single(a => a.Id == "scale").CooldownTicks);
    }

    [Fact(DisplayName = "RF-28.9 cooldowns do not run in Preparo")]
    public void Ability_CooldownFrozenInPreparo()
    {
        var s = New(Arena([new G("grunt", 1)], [new G("grunt", 1)]));
        var tower = Build(s, "bolt", 1190);
        Go(s);
        s.Apply(new UseAbility("scale"));
        Until(s, () => s.Phase == MatchPhase.Preparo);
        int cd = s.AbilityStatuses.Single(a => a.Id == "scale").CooldownTicks;
        Steps(s, 500);
        Assert.Equal(cd, s.AbilityStatuses.Single(a => a.Id == "scale").CooldownTicks);
        Assert.True(tower > 0);
    }

    // ---------------------------------------------------------------- chefes

    [Fact(DisplayName = "RF-28.8 Ransomware: encrypts the nearest tower within 120 every 10 s; towers in the area deal 30% less")]
    public void Boss_Ransomware()
    {
        var s = New(Arena([new G("ransomware", 1)]));
        int near = Build(s, "bolt", 1100);
        Assert.Equal(6.0, FirstHit(s)); // floor(14*0,7 - 3)
        Until(s, () => s.Towers.Any(t => t.DisabledTicks > 0), 400);
        Assert.True(s.Tick >= 300);
        Assert.Contains(s.Events, e => e.Type == TdEventType.TowerDisabled);
        Assert.Contains(s.Towers, t => t.Id == near);

        var out2 = New(Arena([new G("ransomware", 1)]));
        Build(out2, "bolt", 1040); // 160 do portal... dentro do alcance (150) só depois; usa 1070: 130 > 120
        out2.Apply(new SellTower(1));
        Build(out2, "bolt", 1070);
        Assert.Equal(11.0, FirstHit(out2)); // fora da área de 120: sem redução
    }

    [Fact(DisplayName = "RF-28.8 Botnet: splits into 6 swarm at 66% and 33% HP and the wave still completes")]
    public void Boss_Botnet()
    {
        var c = WithBoss(Arena([new G("botnet", 1)]), "botnet", b => b with { Hp = 400 });
        var s = New(c);
        Build(s, "bolt", 1100);
        Build(s, "cannon", 1100);
        Go(s);
        Until(s, () => s.Events.Any(e => e.Type == TdEventType.BossSplit));
        Assert.True(s.Enemies.Count(e => _swarm(s, e)) >= 6);
        Until(s, () => s.Events.Count(e => e.Type == TdEventType.BossSplit) >= 2);
        Assert.Equal(12, s.Events.Where(e => e.Type == TdEventType.BossSplit).Sum(e => e.B));
        Until(s, () => s.Phase is MatchPhase.Vitoria or MatchPhase.Derrota, 30000);
        Assert.Equal(MatchPhase.Vitoria, s.Phase);
    }

    private static bool _swarm(TdMatchState s, TdEnemy e) => !e.IsBoss && s.Content.Enemies.Enemies[e.DefIndex].Id == "swarm";

    [Fact(DisplayName = "RF-28.8 Zero-day: invulnerable for 3 s at half path, immune to slow/paralysis, regenerates between hits")]
    public void Boss_ZeroDay()
    {
        var s = New(Arena([new G("zeroday", 1)]));
        Build(s, "frost", 640);
        int b = Build(s, "bolt", 600);
        Go(s);
        Until(s, () => s.Enemies.Count > 0 && s.Enemies[0].Hp < s.Enemies[0].MaxHp);
        Assert.Equal(0, s.Enemies[0].SlowTicks);       // imune a lentidão
        Assert.Equal(0, s.Enemies[0].ParalyzeTicks);   // e a paralisia
        bool regen = false;
        double last = s.Enemies[0].Hp;
        for (int i = 0; i < 800 && !regen; i++)
        {
            s.Step();
            if (s.Enemies[0].Hp > last) regen = true;
            last = s.Enemies[0].Hp;
        }
        Assert.True(regen);
        Until(s, () => s.Enemies[0].InvulnTicks > 0, 3000);
        double hp = s.Enemies[0].Hp;
        Steps(s, 60);
        Assert.True(s.Enemies[0].Hp >= hp);            // invulnerável: nenhum dano (só regen)
        Assert.True(Tw(s, b).ShotCount > 0);
        Until(s, () => s.Enemies[0].InvulnTicks == 0, 200);
        Assert.Equal(4, s.Enemies[0].Triggers & 4);
    }

    // ---------------------------------------------------------------- determinismo

    private const ulong GoldenStage3 = 1135319241239498339UL;

    private static List<TdLogEntry> ScriptLog() => new List<TdLogEntry>
        {
            new(0, new BuildTower("bolt", 1000)),
            new(0, new BuildTower("frost", 1050)),
            new(0, new BuildTower("cannon", 900)),
            new(0, new BuildTower("balancer", 800)),
            new(0, new SetTargeting(2, TdTargeting.Last)),
            new(0, new SetAutoStart(AutoStartMode.Countdown, 5)),
            new(400, new UseAbility("scale")),
            new(300, new SetTargeting(1, TdTargeting.Strong)),
            new(800, new CallNextWave()),
            new(1500, new UseAbility("firewall")),
            new(3000, new UseAbility("firewall")),
            new(5000, new CallNextWave()),
        };

    private static TdReplayResult RunScript(ulong seed, int stage = 3)
    {
        var log = ScriptLog();
        var c = Content with
        {
            Stages = Content.Stages with
            {
                Stages = Content.Stages.Stages.Select(x => x.Id == stage ? x with { StartingCredits = 5000 } : x).ToArray(),
            },
        };
        return TdReplayRunner.Run(c, new TdReplayScript(stage, seed, 54, log, 12000));
    }

    [Fact(DisplayName = "RNF-28.1 / CA-28.2 Combat replay: same log + seed → same StateHash in 5 runs")]
    public void Replay_Deterministic5Runs()
    {
        var first = RunScript(7);
        for (int i = 0; i < 4; i++) Assert.Equal(first.StateHash, RunScript(7).StateHash);
        Assert.NotEqual(first.StateHash, RunScript(8).StateHash);
        Assert.True(first.Ticks > 3000);
        Assert.Equal(GoldenStage3, first.StateHash);
    }

    [Fact(DisplayName = "RNF-28.1 Combat state hash reacts to targeting, node and ability state")]
    public void Hash_ReactsToCombatState()
    {
        var s = New(Arena([new G("tank", 1)]));
        int id = Build(s, "bolt", 1100);
        ulong h0 = s.ComputeStateHash();
        s.Apply(new SetTargeting(id, TdTargeting.Weak));
        ulong h1 = s.ComputeStateHash();
        Assert.NotEqual(h0, h1);
        Go(s);
        ulong h2 = s.ComputeStateHash();
        s.Apply(new UseAbility("scale"));
        Assert.NotEqual(h2, s.ComputeStateHash());
    }
}
