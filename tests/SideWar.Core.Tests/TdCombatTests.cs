using SideWar.Core.Content.Td;
using SideWar.Core.Td;
using Xunit;
using Rng = SideWar.Core.Simulation.Rng;
using CommandReason = SideWar.Core.Simulation.CommandReason;
using StartWave = SideWar.Core.Td.StartWave;
using BuildTower = SideWar.Core.Td.BuildTower;
using UpgradeTower = SideWar.Core.Td.UpgradeTower;
using SetTargeting = SideWar.Core.Td.SetTargeting;
using SetAutoStart = SideWar.Core.Td.SetAutoStart;

namespace SideWar.Core.Tests;

/// <summary>TD.3: traços, ramos/capstones, mira, controle (slow/paralisia), habilidades, chefes e determinismo do combate.</summary>
public sealed class TdCombatTests
{
    private static readonly TdContent Content = TdMechanicsContent.Load();

    private readonly record struct G(string Enemy, int Count, double Interval = 1.0, double Offset = 0.0);

    /// <summary>Arena: a fase 6 (tudo liberado) com ondas autorais simples e muitos créditos; HP da onda 1 vale ×1,5 (fase 6).</summary>
    private static TdContent Arena(params G[][] waves)
    {
        var defs = waves.Select((w, i) => new TdWaveDef(i + 1, false, 1.0,
            w.Select(g => new TdWaveGroupDef(g.Enemy, g.Count, g.Offset, g.Interval)).ToArray())).ToArray();
        var stage = Content.Stages.Stages[5] with { Waves = waves.Length, StartingCredits = 1_000_000, MaxTowers = 12 };
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

    private static void Branch(TdMatchState s, int id, string branch, bool capstone = false)
    {
        Assert.True(s.Apply(new UpgradeTower(id)).IsOk);
        Assert.True(s.Apply(new UpgradeTower(id)).IsOk);
        Assert.True(s.Apply(new ChooseBranch(id, branch)).IsOk);
        if (capstone) Assert.True(s.Apply(new UpgradeTower(id)).IsOk);
    }

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

    // ---------------------------------------------------------------- ordem do dano e ramos

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

    [Fact(DisplayName = "T-28.5 bolt branch A: x2.6 damage, 50% armor penetration, 1.5x slower cadence")]
    public void Bolt_BranchA()
    {
        var s = New(Arena([new G("armored", 1)]));
        int id = Build(s, "bolt", 1100);
        Branch(s, id, "A");
        Assert.Equal(77.0, FirstHit(s)); // floor(14*2.2*2.6 - 6*0.5)
        Assert.Equal(27, Tw(s, id).CooldownTicks); // ceil(0.6*1.5*30)
    }

    [Fact(DisplayName = "T-28.5 bolt branch A capstone: every 5th shot deals 6x")]
    public void Bolt_CapstoneA_SuperHit()
    {
        var s = New(WithHp(Arena([new G("tank", 1)]), "tank", 5000));
        int id = Build(s, "bolt", 1100);
        Branch(s, id, "A", capstone: true);
        var shots = Shots(s, 5);
        Assert.Equal(5, shots.Count);
        Assert.Equal(79.0, shots[0]); // floor(80.08 - 2*0.5)
        Assert.Equal(479.0, shots[4]); // floor(480.48 - 1)
    }

    [Fact(DisplayName = "T-28.5 bolt branch B: focus stacks +8% per consecutive hit; capstone keeps it between targets")]
    public void Bolt_BranchB_Focus()
    {
        var s = New(Arena([new G("tank", 1)]));
        int id = Build(s, "bolt", 1100);
        Branch(s, id, "B");
        var shots = Shots(s, 3);
        Assert.Equal([28.0, 31.0, 33.0], shots); // floor(30.8*(1+.08k) - 2)

        var arena = Arena([new G("grunt", 3, 0.3)]);
        var a = New(arena);
        int ia = Build(a, "bolt", 1100);
        Branch(a, ia, "B");
        var b = New(arena);
        int ib = Build(b, "bolt", 1100);
        Branch(b, ib, "B", capstone: true);
        Go(a); Go(b);
        Until(a, () => a.Phase != MatchPhase.Onda); Until(b, () => b.Phase != MatchPhase.Onda);
        Assert.True(Tw(a, ia).FocusHits < Tw(a, ia).ShotCount);   // trocou de alvo: zerou
        Assert.Equal(Tw(b, ib).ShotCount, Tw(b, ib).FocusHits);   // Sobrecarga: nunca zera
    }

    [Fact(DisplayName = "T-28.5 bolt branch B has a weak per-shot damage against armored (weakness is real)")]
    public void Bolt_BranchB_WeakVsArmored()
    {
        var a = New(Arena([new G("armored", 1)]));
        int ia = Build(a, "bolt", 1100); Branch(a, ia, "A");
        var b = New(Arena([new G("armored", 1)]));
        int ib = Build(b, "bolt", 1100); Branch(b, ib, "B");
        Assert.True(FirstHit(a) > FirstHit(b) * 2);
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

    [Fact(DisplayName = "T-28.5 cannon branch B: x2.2 vs armored, no splash; capstone ignores 100% armor")]
    public void Cannon_BranchB()
    {
        var arena = WithHp(Arena([new G("armored", 3, 0.2)]), "armored", 10000);
        var b = New(arena);
        Go(b);
        Until(b, () => b.Enemies.Count == 3);
        int id = Build(b, "cannon", 1100);
        Branch(b, id, "B");
        b.Step();
        Assert.Equal(211.0, b.Enemies.Max(e => e.MaxHp - e.Hp)); // floor(45*2.2*2.2 - 6)
        Assert.Equal(1, b.Enemies.Count(e => e.Hp < e.MaxHp)); // sem área
        var c = New(arena);
        int ic = Build(c, "cannon", 1100);
        Branch(c, ic, "B", capstone: true);
        Assert.Equal(217.0, FirstHit(c)); // perfuração total
    }

    [Fact(DisplayName = "T-28.5 cannon branch A: barrage splash 0.7 with larger radius; capstone leaves a burn zone")]
    public void Cannon_BranchA()
    {
        var s = New(Arena([new G("swarm", 8, 0.1)]));
        int id = Build(s, "cannon", 1100);
        Branch(s, id, "A", capstone: true);
        Go(s);
        Until(s, () => s.Zones.Count > 0);
        Assert.Equal(64.0, s.Zones[0].Radius); // 40 x 1,6
        Steps(s, 3);
        Assert.Contains(s.Enemies, e => e.Hp < e.MaxHp);
    }

    // ---------------------------------------------------------------- gelo: lentidão, paralisia, corrosão

    [Fact(DisplayName = "T-28.5 frost slows (0.7) and branch B corrodes armor, marks vulnerable (+25% / capstone +40%)")]
    public void Frost_SlowCorrodeVulnerable()
    {
        var s = New(Arena([new G("armored", 1)]));
        Build(s, "frost", 1100);
        FirstHit(s);
        Assert.Equal(0.7, s.Enemies[0].SlowMult);
        Assert.True(s.Enemies[0].SlowTicks > 0);

        var b = New(Arena([new G("armored", 1)]));
        int id = Build(b, "frost", 1100);
        Branch(b, id, "B");
        // dano: 5*2.2 = 11 - (6-... armadura ainda cheia no primeiro golpe) = 5
        Assert.Equal(5.0, FirstHit(b));
        var e = b.Enemies[0];
        Assert.Equal(6.0, e.CorrodeAmount);
        Assert.Equal(0.25, e.VulnBonus);
        Assert.Equal(0.85, e.SlowMult);

        var c = New(Arena([new G("armored", 1)]));
        int ic = Build(c, "frost", 1100);
        Branch(c, ic, "B", capstone: true);
        FirstHit(c);
        Assert.Equal(0.4, c.Enemies[0].VulnBonus);
    }

    [Fact(DisplayName = "T-28.5 vulnerable mark raises damage of ALL towers (step 3 of the damage order)")]
    public void Vulnerable_AffectsOtherTowers()
    {
        var s = New(WithHp(Arena([new G("tank", 1)]), "tank", 5000));
        int f = Build(s, "frost", 1100);
        Branch(s, f, "B", capstone: true);
        Build(s, "bolt", 1100);
        var shots = Shots(s, 4);
        // sem a marca o bolt nível 1 daria floor(14 - 2) = 12; marcado (+40%) e com a armadura corroída: floor(14 * 1,4) = 19
        Assert.Contains(19.0, shots);
        Assert.DoesNotContain(12.0, shots.Skip(1));
    }

    [Fact(DisplayName = "T-28.5 frost branch A paralyzes (1.2 s); bosses are immune; grace window after paralysis (cap)")]
    public void Frost_Paralysis()
    {
        var s = New(Arena([new G("grunt", 1)]));
        int id = Build(s, "frost", 1100);
        Branch(s, id, "A");
        FirstHit(s);
        Assert.Equal(36, s.Enemies[0].ParalyzeTicks);
        double x = s.Enemies[0].X;
        Steps(s, 10);
        Assert.Equal(x, s.Enemies[0].X); // parado

        var boss = New(Arena([new G("ransomware", 1)]));
        int ib = Build(boss, "frost", 1100);
        Branch(boss, ib, "A");
        FirstHit(boss);
        Assert.Equal(0, boss.Enemies[0].ParalyzeTicks);
    }

    [Fact(DisplayName = "T-28.5 paralysis cap: a second source cannot re-paralyze during the grace window")]
    public void Frost_ParalysisGrace()
    {
        var s = New(Arena([new G("tank", 1)]));
        int a = Build(s, "frost", 1100);
        Branch(s, a, "A");
        Go(s);
        Until(s, () => s.Enemies.Count > 0 && s.Enemies[0].ParalyzeTicks > 0);
        Until(s, () => s.Enemies[0].ParalyzeTicks == 0);
        Assert.True(s.Enemies[0].ParalyzeGraceTicks > 0);
        int b = Build(s, "frost", 1100);
        Branch(s, b, "A");
        Steps(s, 3);
        Assert.Equal(0, s.Enemies[0].ParalyzeTicks); // segunda fonte barrada pela janela
    }

    [Fact(DisplayName = "T-28.5 frost capstone A: paralysis in area")]
    public void Frost_CapstoneArea()
    {
        var s = New(Arena([new G("tank", 3, 0.5)]));
        int id = Build(s, "frost", 1100);
        Branch(s, id, "A", capstone: true);
        Go(s);
        Until(s, () => s.Enemies.Count(e => e.ParalyzeTicks > 0) >= 3, 600);
    }

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

    // ---------------------------------------------------------------- upgrades e ramos (comandos)

    [Fact(DisplayName = "RF-28.5 ChooseBranch / capstone: costs, rejections and sell refund")]
    public void Branch_CostsAndRejections()
    {
        var s = New(Arena([new G("grunt", 1)]), 1);
        int id = Build(s, "bolt", 500);
        Assert.Equal(CommandReason.NotAvailable, s.Apply(new ChooseBranch(id, "A")).Reason); // nível 1
        Assert.True(s.Apply(new UpgradeTower(id)).IsOk);
        Assert.True(s.Apply(new UpgradeTower(id)).IsOk);
        Assert.Equal(CommandReason.MaxLevel, s.Apply(new UpgradeTower(id)).Reason); // nível 3 exige o ramo
        Assert.Equal(CommandReason.NotAvailable, s.Apply(new ChooseBranch(id, "Z")).Reason);
        long before = s.Credits;
        Assert.True(s.Apply(new ChooseBranch(id, "B")).IsOk);
        Assert.Equal(168, before - s.Credits); // 2,4 x 70
        Assert.Equal(4, Tw(s, id).Level);
        Assert.Equal(2, Tw(s, id).Branch);
        Assert.Equal(CommandReason.AlreadyUsed, s.Apply(new ChooseBranch(id, "A")).Reason);
        before = s.Credits;
        Assert.True(s.Apply(new UpgradeTower(id)).IsOk);
        Assert.Equal(252, before - s.Credits); // 3,6 x 70
        Assert.Equal(5, Tw(s, id).Level);
        Assert.Equal(CommandReason.MaxLevel, s.Apply(new UpgradeTower(id)).Reason);
        long invested = 70 + 63 + 112 + 168 + 252;
        Assert.Equal(invested, Tw(s, id).Invested);
        long c0 = s.Credits;
        Assert.True(s.Apply(new SellTower(id)).IsOk);
        Assert.Equal((long)Math.Floor(invested * 0.7 + 1e-9), s.Credits - c0);
    }

    [Fact(DisplayName = "RF-28.5 ChooseBranch rejects insufficient credits without changing state")]
    public void Branch_InsufficientCredits()
    {
        var c = Arena([new G("grunt", 1)]);
        c = c with { Stages = c.Stages with { Stages = [.. c.Stages.Stages.Take(5), c.Stages.Stages[5] with { StartingCredits = 70 + 63 + 112 + 100 }] } };
        var s = New(c);
        int id = Build(s, "bolt", 500);
        s.Apply(new UpgradeTower(id)); s.Apply(new UpgradeTower(id));
        ulong h = s.ComputeStateHash();
        var r = s.Apply(new ChooseBranch(id, "A"));
        Assert.Equal(CommandReason.InsufficientCredits, r.Reason);
        Assert.Equal(68, r.Missing);
        Assert.Equal(h, s.ComputeStateHash());
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

    [Fact(DisplayName = "T-28.5 balancer branch A: 3 nodes holding 2 each; capstone Anycast spawns at the front edge")]
    public void Balancer_BranchA()
    {
        var s = New(Arena([new G("swarm", 12, 0.15)]));
        int id = Build(s, "balancer", 1100);
        Branch(s, id, "A");
        Go(s);
        Until(s, () => s.Nodes.Count == 3, 600);
        Assert.All(s.Nodes, n => Assert.Equal(2, n.Holds));

        var c = New(Arena([new G("tank", 1)]));
        int ic = Build(c, "balancer", 1100);
        Branch(c, ic, "A", capstone: true);
        Go(c);
        Until(c, () => c.Nodes.Count == 1);
        Assert.Equal(1010.0, c.Nodes[0].X); // X da torre − alcance (90)
    }

    [Fact(DisplayName = "T-28.5 balancer branch B: nodes explode at the end; capstone Blue/green renews a broken node at once")]
    public void Balancer_BranchB()
    {
        var plain = New(Arena([new G("grunt", 1)]));
        int ip = Build(plain, "balancer", 1100);
        Go(plain);
        Until(plain, () => plain.Events.Any(e => e.Type == TdEventType.NodeEnded));
        Assert.Contains(plain.Enemies, e => e.Hp > 0);   // 4 dano/s por 3 s: ainda vivo
        Assert.Contains(plain.Towers, t => t.Id == ip);

        var s = New(Arena([new G("grunt", 1)]));
        int id = Build(s, "balancer", 1100);
        Branch(s, id, "B");
        Go(s);
        Until(s, () => s.Events.Any(e => e.Type == TdEventType.EnemyKilled)); // a explosão mata o grunt
        Assert.Contains(s.Events, e => e.Type == TdEventType.NodeEnded);

        var bg = New(Arena([new G("tank", 1)]));
        int ib = Build(bg, "balancer", 1100);
        Branch(bg, ib, "B", capstone: true);
        Go(bg);
        Until(bg, () => bg.Nodes.Count == 1);
        int nodeId = bg.Nodes[0].Id;
        Until(bg, () => bg.Events.Any(e => e.Type == TdEventType.NodeEnded));
        Assert.Single(bg.Nodes);                       // renasceu na hora
        Assert.Equal(nodeId, bg.Nodes[0].Id);
        Assert.Equal(0, Tw(bg, ib).NodeCdTicks);
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

    [Fact(DisplayName = "T-28.5 scaleset A: +25% cadence nearby; B: +25% damage; capacity capstone: +1 maxTowers; elasticity during Escalar")]
    public void Scaleset_Auras()
    {
        var plain = New(Arena([new G("grunt", 1)]));
        int ip = Build(plain, "bolt", 1100);
        FirstHit(plain);
        Assert.Equal(18, Tw(plain, ip).CooldownTicks);

        var a = New(Arena([new G("grunt", 1)]));
        int ia = Build(a, "bolt", 1100);
        int sa = Build(a, "scaleset", 1150);
        Branch(a, sa, "A");
        FirstHit(a);
        Assert.Equal(15, Tw(a, ia).CooldownTicks); // ceil(0,6/1,25*30)

        var b = New(Arena([new G("grunt", 1)]));
        Build(b, "bolt", 1100);
        int sb = Build(b, "scaleset", 1150);
        Branch(b, sb, "B");
        Assert.Equal(17.0, FirstHit(b)); // floor(14*1,25)

        var far = New(Arena([new G("grunt", 1)]));
        int iff = Build(far, "bolt", 1100);
        int sf = Build(far, "scaleset", 900);
        Branch(far, sf, "A");
        FirstHit(far);
        Assert.Equal(18, Tw(far, iff).CooldownTicks); // fora da aura

        var cap = New(Arena([new G("grunt", 1)]));
        int sc = Build(cap, "scaleset", 800);
        int max0 = cap.MaxTowers;
        Branch(cap, sc, "B", capstone: true);
        Assert.Equal(max0 + 1, cap.MaxTowers);

        var el = New(Arena([new G("grunt", 1)]));
        int ie = Build(el, "bolt", 1100);
        int se = Build(el, "scaleset", 1150);
        Branch(el, se, "A", capstone: true);
        Go(el);
        Assert.True(el.Apply(new UseAbility("scale")).IsOk);
        Until(el, () => Tw(el, ie).CooldownTicks > 0);
        Assert.Equal(10, Tw(el, ie).CooldownTicks); // 0,6/(1+0,5+0,25*1,5)*30 = 9,6 → 10
    }

    [Fact(DisplayName = "T-28.5 a disabled scaleset loses its aura")]
    public void Scaleset_DisabledNoAura()
    {
        var s = New(Arena([new G("leech", 1)]));
        int ib = Build(s, "bolt", 1100);
        int ss = Build(s, "scaleset", 1150);
        Branch(s, ss, "A");
        Go(s);
        Until(s, () => Tw(s, ss).DisabledTicks > 0 && Tw(s, ib).CooldownTicks > 0, 200);
        Assert.Equal(18, Tw(s, ib).CooldownTicks);
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
        int id = Build(s, "bolt", 1100);
        Branch(s, id, "A", capstone: true);
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
        int f = Build(s, "frost", 640);
        Branch(s, f, "A");
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

    private const ulong GoldenStage3 = 6829842889198948226UL;

    private static List<TdLogEntry> ScriptLog() => new List<TdLogEntry>
        {
            new(0, new BuildTower("bolt", 1000)),
            new(0, new BuildTower("frost", 1050)),
            new(0, new BuildTower("cannon", 900)),
            new(0, new BuildTower("balancer", 800)),
            new(0, new UpgradeTower(1)),
            new(0, new UpgradeTower(1)),
            new(0, new ChooseBranch(1, "A")),
            new(0, new UpgradeTower(3)),
            new(0, new UpgradeTower(3)),
            new(0, new ChooseBranch(3, "A")),
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
                Stages = Content.Stages.Stages.Select(x => x.Id == stage ? x with { StartingCredits = 5000, MaxTowers = 8 } : x).ToArray(),
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

    [Fact(DisplayName = "RNF-28.1 Combat state hash reacts to branch, targeting, node and ability state")]
    public void Hash_ReactsToCombatState()
    {
        var s = New(Arena([new G("tank", 1)]));
        int id = Build(s, "bolt", 1100);
        ulong h0 = s.ComputeStateHash();
        s.Apply(new SetTargeting(id, TdTargeting.Weak));
        ulong h1 = s.ComputeStateHash();
        Assert.NotEqual(h0, h1);
        Branch(s, id, "A");
        Assert.NotEqual(h1, s.ComputeStateHash());
        Go(s);
        ulong h2 = s.ComputeStateHash();
        s.Apply(new UseAbility("scale"));
        Assert.NotEqual(h2, s.ComputeStateHash());
    }
}
