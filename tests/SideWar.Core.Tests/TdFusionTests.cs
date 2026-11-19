using System.Reflection;
using System.Text;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using SellTower = SideWar.Core.Td.SellTower;
using StartWave = SideWar.Core.Td.StartWave;
using FreeZone = SideWar.Core.Simulation.FreeZone;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using Rng = SideWar.Core.Simulation.Rng;

namespace SideWar.Core.Tests;

/// <summary>TD.37a (SDD-37, CA-37.1 a 3 e 7): fusão de torres no Core. Conteúdo embarcado, fase 6, inimigo parado e com muito HP.</summary>
public sealed class TdFusionTests
{
    private static TdContent Rig(string enemy, int count, double armor, bool flying = false)
    {
        var c = TdContentLoader.LoadEmbedded();
        return c with
        {
            Enemies = c.Enemies with { Enemies = c.Enemies.Enemies.Select(e => e.Id == enemy ? e with { Hp = 10000, Armor = armor, Speed = 30 } : e).ToArray() },
            Stages = c.Stages with { Stages = c.Stages.Stages.Select(s => s.Id == 6 ? s with { StartingCredits = 1000000, Waves = 1 } : s).ToArray() },
            Waves = new TdWavesDef([.. c.Waves.Stages.Take(5), new TdStageWavesDef(6, [new TdWaveDef(1, false, 1, [new TdWaveGroupDef(enemy, count, 0, 0.01)])])]),
        };
    }

    private static TdMatchState Arena(string enemy = "grunt", int count = 1, double armor = 0, int stage = 6, ulong seed = 1) =>
        TdMatchState.Create(Rig(enemy, count, armor), stage, Rng.Seed(seed, 54));

    private static int Build(TdMatchState s, string id, int x)
    {
        Assert.True(s.Apply(new BuildTower(id, x)).IsOk);
        return s.Towers[^1].Id;
    }

    private static void Fuse(TdMatchState s, int baseId, string module) => Assert.True(s.Apply(new FuseTower(baseId, module)).IsOk);
    private static void Steps(TdMatchState s, int n) { for (int i = 0; i < n; i++) s.Step(); }

    /// <summary>Inicia a onda e fixa os inimigos parados em <paramref name="x"/> (sem deslocar a distância entre eles: +1 por inimigo).</summary>
    private static void Start(TdMatchState s, double x)
    {
        Assert.True(s.Apply(new StartWave()).IsOk);
        Steps(s, 1);
        var list = (List<TdEnemy>)s.Enemies;
        for (int i = 0; i < list.Count; i++) { var e = list[i]; e.X = x + i; e.Speed = 0; list[i] = e; }
    }

    private static double Lost(TdMatchState s) => s.Enemies.Sum(e => e.MaxHp - e.Hp + (e.ShieldHp < 0 ? 0 : 0));

    [Fact(DisplayName = "CA-37.1 fusion creates exactly one two-component entity, charges NextTowerCost and keeps the Id")]
    public void Fuse_CreatesOneEntityWithTwoComponents()
    {
        var s = Arena();
        int id = Build(s, "bolt", 1100);
        long before = s.Credits, cost = s.NextTowerCost(s.Towers[0].DefIndex == 0 ? 1 : 0);
        int frost = Array.FindIndex(s.Content.Towers, t => t.Id == "frost");
        cost = s.NextTowerCost(frost);
        Fuse(s, id, "frost");
        Assert.Single(s.Towers);
        var t = s.Towers[0];
        Assert.Equal(id, t.Id);
        Assert.True(t.HasModule);
        Assert.Equal(frost, t.ModuleDefIndex);
        Assert.Equal(cost, t.ModulePrice);
        Assert.Equal(before - cost, s.Credits);
        Assert.Equal("Raio gélido", s.TowerDisplayName(t));
        Assert.Contains(s.Events, e => e.Type == TdEventType.TowerFused && e.A == id && e.B == cost);
    }

    [Fact(DisplayName = "CA-37.1 order matters: Gelo+Raio and Raio+Gelo give different named hybrids; same type is allowed")]
    public void Fuse_IsOrderedAndAllowsSameType()
    {
        var a = Arena(); int fa = Build(a, "frost", 1100); Fuse(a, fa, "bolt");
        var b = Arena(); int fb = Build(b, "bolt", 1100); Fuse(b, fb, "frost");
        Assert.Equal("Gelo elétrico", a.TowerDisplayName(a.Towers[0]));
        Assert.Equal("Raio gélido", b.TowerDisplayName(b.Towers[0]));
        var c = Arena(); int fc = Build(c, "bolt", 1100); Fuse(c, fc, "bolt");
        Assert.Equal("Raio + Raio", c.TowerDisplayName(c.Towers[0]));
        Assert.Equal(2, c.TowerCount(0));
    }

    [Fact(DisplayName = "CA-37.1 rejected fusions (third component, locked, unknown, unplaced, credits) change nothing")]
    public void Fuse_RejectionsAreAtomic()
    {
        var s = Arena();
        int id = Build(s, "bolt", 1100);
        Fuse(s, id, "frost");
        ulong h = s.ComputeStateHash(); long credits = s.Credits; ulong rng = s.RngState;
        Assert.Equal(CommandReason.AlreadyFused, s.Apply(new FuseTower(id, "cannon")).Reason);
        Assert.Equal(CommandReason.TowerNotFound, s.Apply(new FuseTower(999, "cannon")).Reason);
        Assert.Equal(CommandReason.TowerNotFound, s.Apply(new FuseTower(id, "nada")).Reason);
        Assert.Equal(CommandReason.TowerNotFound, s.Apply(new FuseTower(id, null!)).Reason);
        Assert.Equal(h, s.ComputeStateHash()); Assert.Equal(credits, s.Credits); Assert.Equal(rng, s.RngState);

        var poor = Arena(); int pid = Build(poor, "bolt", 1000);
        var field = typeof(TdMatchState).GetProperty("Credits")!; field.SetValue(poor, 5L);
        ulong ph = poor.ComputeStateHash();
        var r = poor.Apply(new FuseTower(pid, "cannon"));
        Assert.Equal(CommandReason.InsufficientCredits, r.Reason); Assert.True(r.Missing > 0);
        Assert.Equal(ph, poor.ComputeStateHash());

        var early = Arena(stage: 1);
        int eid = Build(early, "bolt", 1100);
        ulong eh = early.ComputeStateHash();
        Assert.Equal(CommandReason.FusionLocked, early.Apply(new FuseTower(eid, "frost")).Reason);
        Assert.Equal(eh, early.ComputeStateHash());
        Assert.False(early.FusionUnlocked);
    }

    [Fact(DisplayName = "CA-37.1 fusion needs a placed base; layout change keeps the hybrid and repositioning restores it")]
    public void Fuse_NeedsPlacedBase_LayoutKeepsHybrid()
    {
        var s = Arena();
        int id = Build(s, "bolt", 600);
        Fuse(s, id, "frost");
        var wide = StripLayout.Create(1200, 1200, 0f, 0f, 1200, TowerHost.Taskbar, [new FreeZone(0, 1200)]);
        Assert.True(s.Apply(new LayoutChanged(wide)).IsOk);
        Assert.True(s.Towers[0].Placed);
        var gone = StripLayout.Create(1200, 1200, 0f, 0f, 1200, TowerHost.Taskbar, [new FreeZone(0, 100)]);
        Assert.True(s.Apply(new LayoutChanged(gone)).IsOk);
        Assert.False(s.Towers[0].Placed); Assert.True(s.Towers[0].HasModule);
        ulong h = s.ComputeStateHash();
        Assert.Equal(CommandReason.NotPlaced, s.Apply(new FuseTower(id, "cannon")).Reason);
        Assert.Equal(h, s.ComputeStateHash());
        Assert.True(s.Apply(new LayoutChanged(wide)).IsOk);
        Assert.True(s.Towers[0].Placed); Assert.True(s.Towers[0].HasModule);
        var open = Arena(); int oid = Build(open, "bolt", 700);
        var snap = Arena(); int sid = Build(snap, "bolt", 700);
        var unplaced = StripLayout.Create(1200, 1200, 0f, 0f, 1200, TowerHost.Taskbar, [new FreeZone(0, 100)]);
        Assert.True(snap.Apply(new LayoutChanged(unplaced)).IsOk);
        Assert.Equal(CommandReason.NotPlaced, snap.Apply(new FuseTower(sid, "frost")).Reason);
        Assert.NotEqual(0, oid);
    }

    [Fact(DisplayName = "CA-37.2 limit of 48 entities does not block fusion on an existing entity")]
    public void Fuse_AllowedAt48Entities()
    {
        var s = Arena();
        for (int i = 0; i < TdMatchState.TechnicalTowerLimit; i++) { var r = s.Apply(new BuildTower(i % 2 == 0 ? "bolt" : "frost", 20 + i * 20)); Assert.True(r.IsOk, $"{i}: {r.Reason} {r.Missing}"); }
        Assert.Equal(CommandReason.TowerLimit, s.Apply(new BuildTower("bolt", 1190)).Reason);
        Fuse(s, s.Towers[0].Id, "frost");
        Assert.Equal(TdMatchState.TechnicalTowerLimit, s.Towers.Count);
    }

    [Fact(DisplayName = "CA-37.2 at most two Cofre components in total, including hybrids and A+A")]
    public void Fuse_VaultLimitCountsComponents()
    {
        var s = Arena();
        int vault = Array.FindIndex(s.Content.Towers, t => t.Id == "vault");
        int a = Build(s, "bolt", 1000);
        Fuse(s, a, "vault");
        int b = Build(s, "vault", 900);
        Assert.Equal(2, s.TowerCount(vault));
        Assert.Equal(CommandReason.TowerTypeLimit, s.Apply(new BuildTower("vault", 800)).Reason);
        int c = Build(s, "frost", 700);
        ulong h = s.ComputeStateHash();
        Assert.Equal(CommandReason.TowerTypeLimit, s.Apply(new FuseTower(c, "vault")).Reason);
        Assert.Equal(CommandReason.TowerTypeLimit, s.Apply(new FuseTower(b, "vault")).Reason);
        Assert.Equal(h, s.ComputeStateHash());
        var d = Arena(); int v = Build(d, "vault", 1000);
        Fuse(d, v, "vault"); // A+A conta duas vezes
        Assert.Equal(CommandReason.TowerTypeLimit, d.Apply(new BuildTower("vault", 900)).Reason);
    }

    [Fact(DisplayName = "CA-37.2 sell is indivisible, per component min(paid, value), counts A+A as n and n-1 and never profits")]
    public void Sell_HybridIsIndivisibleAndNoArbitrage()
    {
        var s = Arena();
        long start = s.Credits;
        int id = Build(s, "bolt", 1100);
        Fuse(s, id, "bolt");
        var t = s.Towers[0];
        long expected = Math.Min(t.PurchasePrice, s.SellValueAt(t.DefIndex, 2)) + Math.Min(t.ModulePrice, s.SellValueAt(t.DefIndex, 1));
        Assert.Equal(expected, s.SellValue(id));
        long mid = s.Credits;
        Assert.True(s.Apply(new SellTower(id)).IsOk);
        Assert.Empty(s.Towers);
        Assert.Equal(mid + expected, s.Credits);
        Assert.True(s.Credits < start);
        Assert.Equal(0, s.TowerCount(t.DefIndex));
    }

    [Fact(DisplayName = "CA-37.3 Raio gélido: the Raio shot applies the Gelo slow; a plain Raio does not")]
    public void NamedBoltFrost_Slows()
    {
        var plain = Arena(); Build(plain, "bolt", 1100); Start(plain, 1100); Steps(plain, 2);
        var s = Arena(); int id = Build(s, "bolt", 1100); Fuse(s, id, "frost"); Start(s, 1100); Steps(s, 2);
        Assert.Equal(0, plain.Enemies[0].SlowTicks);
        Assert.True(s.Enemies[0].SlowTicks > 0);
        Assert.Equal(0.7, s.Enemies[0].SlowMult, 6);
        Assert.Equal(plain.Enemies[0].Hp, s.Enemies[0].Hp, 6);
    }

    [Fact(DisplayName = "CA-37.3 Gelo elétrico: Gelo shot adds 35% of the Raio base damage (8) and keeps the slow")]
    public void NamedFrostBolt_AddsElectricDamage()
    {
        var plain = Arena(); Build(plain, "frost", 1100); Start(plain, 1100); Steps(plain, 2);
        var s = Arena(); int id = Build(s, "frost", 1100); Fuse(s, id, "bolt"); Start(s, 1100); Steps(s, 2);
        Assert.Equal(9, plain.Enemies[0].MaxHp - plain.Enemies[0].Hp);
        Assert.Equal(9 + 8, s.Enemies[0].MaxHp - s.Enemies[0].Hp);
        Assert.True(s.Enemies[0].SlowTicks > 0);
    }

    [Fact(DisplayName = "CA-37.3 Canhão elétrico: +35% of Raio only on the main target, not on the splash")]
    public void NamedCannonBolt_MainTargetOnly()
    {
        var s = Arena("grunt", 2); int id = Build(s, "cannon", 1100); Fuse(s, id, "bolt"); Start(s, 1100); Steps(s, 2);
        var loss = s.Enemies.Select(e => e.MaxHp - e.Hp).OrderDescending().ToArray();
        Assert.Equal(108, loss[0]);
        Assert.Equal(50, loss[1]);
    }

    [Fact(DisplayName = "CA-37.3 Raio explosivo: Raio impact splashes ground enemies with the Canhão radius/factor; main target hit once; flyers spared")]
    public void NamedBoltCannon_SplashGroundOnly()
    {
        var s = Arena("grunt", 2); int id = Build(s, "bolt", 1100); Fuse(s, id, "cannon"); Start(s, 1100); Steps(s, 2);
        var loss = s.Enemies.Select(e => e.MaxHp - e.Hp).OrderDescending().ToArray();
        Assert.Equal(24, loss[0]);
        Assert.Equal(12, loss[1]);
        var f = Arena("flyer", 2); int fid = Build(f, "bolt", 1100); Fuse(f, fid, "cannon"); Start(f, 1100); Steps(f, 2);
        var floss = f.Enemies.Select(e => e.MaxHp - e.Hp).OrderDescending().ToArray();
        Assert.Equal(24, floss[0]); Assert.Equal(0, floss[1]);
    }

    [Fact(DisplayName = "CA-37.3 Raio encadeado: exactly one extra jump with 40% of the shot, nearest by distance then Id")]
    public void NamedBoltCosmos_OneJump()
    {
        var s = Arena("grunt", 3); int id = Build(s, "bolt", 1100); Fuse(s, id, "cosmos"); Start(s, 1100); Steps(s, 2);
        var loss = s.Enemies.Select(e => e.MaxHp - e.Hp).ToArray();
        Assert.Equal(24, loss[0]); // mais perto do núcleo? X maior = id menor: o primeiro (First = menor X)
        Assert.Equal(1, loss.Count(l => l == 24));
        Assert.Equal(1, loss.Count(l => l == 9));
        Assert.Equal(1, loss.Count(l => l == 0));
    }

    [Fact(DisplayName = "CA-37.3 Canhão de ruptura: 25% armor corrosion for 3 s on the main target, without stacking")]
    public void NamedCannonPolicy_CorrodesWithoutStacking()
    {
        var s = Arena("armored", 1, 12); int id = Build(s, "cannon", 1100); Fuse(s, id, "policy"); Start(s, 1100); Steps(s, 2);
        var e = s.Enemies[0];
        Assert.Equal(3.0, e.CorrodeAmount, 6);
        Assert.InRange(e.CorrodeTicks, 85, 90);
        Steps(s, 70); // 2º tiro do canhão (2,2 s): renova, não soma
        Assert.Equal(3.0, s.Enemies[0].CorrodeAmount, 6);
    }

    [Fact(DisplayName = "CA-37.3 Bloqueio gelado: node-held ground enemies get the Gelo slow")]
    public void NamedBalancerFrost_SlowsHeldEnemies()
    {
        var plain = Arena(); Build(plain, "balancer", 1100); Start(plain, 1100); Steps(plain, 3);
        var s = Arena(); int id = Build(s, "balancer", 1100); Fuse(s, id, "frost"); Start(s, 1100); Steps(s, 3);
        Assert.NotEmpty(s.Nodes);
        Assert.Equal(0, plain.Enemies[0].SlowTicks);
        Assert.True(s.Enemies[0].SlowTicks > 0);
    }

    [Fact(DisplayName = "CA-37.3 Aura elétrica and Cofre gélido act every 3 s on an eligible target; no target keeps the timer")]
    public void NamedPeriodicModules()
    {
        var s = Arena(); int id = Build(s, "scaleset", 1100); Fuse(s, id, "bolt"); Start(s, 1100);
        Steps(s, 88); Assert.Equal(0, s.Enemies[0].MaxHp - s.Enemies[0].Hp);
        Steps(s, 4); Assert.Equal(8, s.Enemies[0].MaxHp - s.Enemies[0].Hp);
        var v = Arena(); int vid = Build(v, "vault", 1100); Fuse(v, vid, "frost"); Start(v, 1100);
        Steps(v, 88); Assert.Equal(0, v.Enemies[0].SlowTicks);
        Steps(v, 4); Assert.True(v.Enemies[0].SlowTicks > 0);
        var far = Arena(); int fid = Build(far, "scaleset", 100); Fuse(far, fid, "bolt"); Start(far, 1100);
        Steps(far, 200);
        Assert.Equal(0, far.Towers[0].ModuleCdTicks); // sem alvo, não consome a recarga
        Assert.Equal(0, far.Enemies[0].MaxHp - far.Enemies[0].Hp);
    }

    [Fact(DisplayName = "CA-37.3 generic attack module: independent 35% shot every 3 s, module range and target rules (ground-only Canhão spares flyers)")]
    public void GenericAttackModule()
    {
        var plain = Arena(); Build(plain, "frost", 1100); Start(plain, 1100); Steps(plain, 100);
        var s = Arena(); int id = Build(s, "frost", 1100); Fuse(s, id, "cannon"); Start(s, 1100); Steps(s, 100);
        Assert.Equal(35, Lost(s) - Lost(plain));
        var fp = Arena("flyer"); Build(fp, "frost", 1100); Start(fp, 1100); Steps(fp, 100);
        var f = Arena("flyer"); int fid = Build(f, "frost", 1100); Fuse(f, fid, "cannon"); Start(f, 1100); Steps(f, 100);
        Assert.Equal(Lost(fp), Lost(f));
    }

    [Fact(DisplayName = "CA-37.3 generic control module: 15% slow for 2 s every 3 s; no base slow")]
    public void GenericControlModule()
    {
        var s = Arena(); int id = Build(s, "bolt", 1100); Fuse(s, id, "policy"); Start(s, 1100);
        Steps(s, 88); Assert.Equal(0, s.Enemies[0].SlowTicks);
        Steps(s, 4);
        Assert.Equal(0.85, s.Enemies[0].SlowMult, 6);
        Assert.InRange(s.Enemies[0].SlowTicks, 55, 60);
    }

    [Fact(DisplayName = "CA-37.3 generic support module: +5% damage to OTHER placed towers in range, never self-aura")]
    public void GenericSupportModule()
    {
        var s = Arena(); int host = Build(s, "bolt", 1100); Fuse(s, host, "scaleset");
        int neighbor = Build(s, "bolt", 1060);
        Start(s, 1100); Steps(s, 2);
        // Raio vizinho: 24 × 1,05 = 25 (piso); o Raio híbrido (módulo Scale Set genérico) não se beneficia: 24.
        var plain = Arena(); Build(plain, "bolt", 1100); Start(plain, 1100); Steps(plain, 2);
        Assert.Equal(24, plain.Enemies[0].MaxHp - plain.Enemies[0].Hp);
        Assert.Equal(25 + 24, s.Enemies[0].MaxHp - s.Enemies[0].Hp);
        Assert.NotEqual(host, neighbor);
    }

    [Fact(DisplayName = "CA-37.3 generic economy module pays 50% of the base Cofre income (floor), no leak bonus; Cofre base keeps full income")]
    public void GenericEconomyModule()
    {
        long Income(Action<TdMatchState> setup)
        {
            var s = Arena(); setup(s);
            Assert.True(s.Apply(new StartWave()).IsOk); Steps(s, 1);
            var list = (List<TdEnemy>)s.Enemies; var e = list[0]; e.Hp = 0; list[0] = e;
            Steps(s, 1);
            return s.Events.Where(x => x.Type == TdEventType.VaultIncome).Sum(x => x.B);
        }
        Assert.Equal(7, Income(s => Fuse(s, Build(s, "bolt", 1000), "vault")));
        Assert.Equal(24, Income(s => Build(s, "vault", 1000)));
        Assert.Equal(24 + 7, Income(s => Fuse(s, Build(s, "vault", 1000), "vault")));
    }

    [Fact(DisplayName = "CA-37.3 rune of type applies once to its own component's portion only (Gelo elétrico: Raio portion +10%, Gelo portion untouched)")]
    public void RunesApplyPerComponent()
    {
        var s = Arena(); int id = Build(s, "frost", 1100); Fuse(s, id, "bolt");
        var awakened = typeof(TdMatchState).GetField("_awakened", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var web = s.RuneWeb;
        uint bits = 0;
        foreach (var n in web.Where(n => n.Type == "bolt").Take(2)) bits |= 1u << (n.Ring * 8 + n.Sector);
        awakened.SetValue(s, bits);
        typeof(TdMatchState).GetMethod("RebuildRuneEffects", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(s, null);
        Start(s, 1100); Steps(s, 2);
        Assert.Equal(9 + 9, s.Enemies[0].MaxHp - s.Enemies[0].Hp); // Gelo 9 (sem runa) + Raio 24 × 1,10 × 0,35 = 9,24 → 9
    }

    [Fact(DisplayName = "CA-37.3 disabled hybrid does not act; module timer is separate from the base cooldown")]
    public void DisabledHybridModuleSleeps()
    {
        var s = Arena(); int id = Build(s, "scaleset", 1100); Fuse(s, id, "bolt"); Start(s, 1100);
        var towers = (List<TdTower>)s.Towers; var t = towers[0]; t.DisabledTicks = 400; towers[0] = t;
        Steps(s, 200);
        Assert.Equal(0, s.Enemies[0].MaxHp - s.Enemies[0].Hp);
    }

    [Fact(DisplayName = "CA-37.7 same seed, content and commands give the same StateHash; a fusion changes it; replay covers fuse and sell")]
    public void Replay_IsDeterministic()
    {
        var content = Rig("grunt", 3, 0);
        IReadOnlyList<TdLogEntry> Log(bool fuse) =>
        [
            new(0, new BuildTower("bolt", 1100), 0),
            .. fuse ? [new TdLogEntry(0, new FuseTower(1, "frost"), 1)] : Array.Empty<TdLogEntry>(),
            new(1, new StartWave(), 0),
            new(200, new FuseTower(1, "cannon"), 0), // rejeitada (já é híbrida) quando fuse; aceita quando não
            new(300, new SellTower(1), 0),
        ];
        TdReplayResult Run(bool fuse) => TdReplayRunner.Run(content, new TdReplayScript(6, 9, 54, Log(fuse), 600, TdMatchOptions.Default));
        var a = Run(true); var b = Run(true); var c = Run(false);
        Assert.Equal(a, b);
        Assert.NotEqual(a.StateHash, c.StateHash);
        Assert.Equal(1, a.CommandsRejected);
    }

    [Fact(DisplayName = "CA-37.7 hash of a simple tower does not include module data; fusion consumes no main RNG")]
    public void Hash_ModuleOnlyForHybrids_NoRng()
    {
        var a = Arena(); var b = Arena();
        Build(a, "bolt", 1100); int bid = Build(b, "bolt", 1100);
        Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
        Fuse(b, bid, "frost");
        Assert.NotEqual(a.ComputeStateHash(), b.ComputeStateHash());
        Assert.Equal(a.RngState, b.RngState);
    }

    [Fact(DisplayName = "RF-37.12 fusion content: embedded is valid, enters the ContentHash and the validator rejects bad pairs, roles and parameters")]
    public void FusionContent_IsValidatedAndHashed()
    {
        var files = TdContentLoader.ReadEmbedded();
        Assert.Contains("td-fusion.json", TdContentLoader.FileNames);
        var changed = new Dictionary<string, byte[]>(files) { ["td-fusion.json"] = Encoding.UTF8.GetBytes(" " + Encoding.UTF8.GetString(files["td-fusion.json"])) };
        Assert.NotEqual(TdContentLoader.ComputeHash(files), TdContentLoader.ComputeHash(changed));
        var fusion = TdContentLoader.LoadEmbedded().Fusion;
        Assert.Equal(9, fusion.Pairs.Length);

        static TdContentLoadResult With(string find, string replace)
        {
            var f = TdContentLoader.ReadEmbedded();
            var text = Encoding.UTF8.GetString(f["td-fusion.json"]);
            Assert.Contains(find, text);
            f["td-fusion.json"] = Encoding.UTF8.GetBytes(text.Replace(find, replace, StringComparison.Ordinal));
            return TdContentLoader.Load(f);
        }
        static void Bad(TdContentLoadResult r, string fragment) { Assert.False(r.IsValid); Assert.Contains(r.Errors, x => x.Contains(fragment, StringComparison.Ordinal)); }
        Bad(With("{\"base\": \"bolt\", \"module\": \"frost\", \"name\": \"Raio gélido\"", "{\"base\": \"frost\", \"module\": \"bolt\", \"name\": \"Raio gélido\""), "par duplicado");
        Bad(With("\"economy\": [\"vault\"]", "\"economy\": []"), "sem papel");
        Bad(With("\"support\": [\"scaleset\", \"backup\", \"aks\"]", "\"support\": [\"scaleset\", \"backup\", \"aks\", \"bolt\"]"), "em mais de um papel");
        Bad(With("\"damagePct\": 0.35}}", "\"damagePct\": 0}}"), "deve ser positivo");
        Bad(With("\"effect\": \"hitSlow\"", "\"effect\": \"laser\""), "effect desconhecido 'laser'");
        Bad(With("\"module\": \"cannon\", \"name\": \"Raio explosivo\"", "\"module\": \"policy\", \"name\": \"Raio explosivo\""), "hitSplash exige");
        Bad(With("\"base\": \"balancer\"", "\"base\": \"nada\""), "torre inexistente");
        Bad(With("\"minStage\": 2", "\"minStage\": 99"), "minStage");
    }
}
