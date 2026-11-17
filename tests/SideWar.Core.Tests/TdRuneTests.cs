using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using StartWave = SideWar.Core.Td.StartWave;

namespace SideWar.Core.Tests;

public sealed class TdRuneTests
{
    private static TdContent Rich()
    {
        var c = TdMechanicsContent.Load();
        return c with { Stages = c.Stages with { Stages = c.Stages.Stages.Select(s => s with { StartingCredits = 100000 }).ToArray() } };
    }
    private static TdMatchState New() => TdMatchState.Create(Rich(), 6, Rng.Seed(7, 54));
    private static void Buy(TdMatchState s, params string[] ids)
    {
        foreach (string id in ids) Assert.True(s.Apply(new BuyRune(id)).IsOk, id);
    }

    [Fact]
    public void Adjacency_rejections_and_credit_loop_are_atomic()
    {
        var s = New();
        Assert.True(s.OwnsRune("core"));
        ulong h = s.ComputeStateHash();
        Assert.Equal(CommandReason.UnknownRune, s.Apply(new BuyRune("missing")).Reason);
        Assert.Equal(CommandReason.RuneNotAdjacent, s.Apply(new BuyRune("bolt.k")).Reason);
        Assert.Equal(CommandReason.NotAvailable, s.Apply(new RefundRune("core")).Reason);
        Assert.Equal(CommandReason.RuneNotOwned, s.Apply(new RefundRune("bolt.s1")).Reason);
        Assert.Equal(h, s.ComputeStateHash());
        long credits = s.Credits;
        Buy(s, "bolt.s1", "bolt.s2", "bolt.n1", "bolt.s3");
        Assert.Equal(credits - 240, s.Credits);
        Assert.Equal(CommandReason.RuneOwned, s.Apply(new BuyRune("bolt.s1")).Reason);
        Assert.True(s.Apply(new RefundRune("bolt.s1")).IsOk);
        Assert.Equal(credits, s.Credits);
        Assert.Equal(h, s.ComputeStateHash());
        var c = Rich();
        c = c with { Stages = c.Stages with { Stages = c.Stages.Stages.Select(x => x with { StartingCredits = -80 }).ToArray() } };
        var poor = TdMatchState.Create(c, 6, Rng.Seed(7, 54));
        Assert.Equal(CommandReason.InsufficientCredits, poor.Apply(new BuyRune("bolt.s1")).Reason);
        Assert.Equal(40, poor.Apply(new BuyRune("bolt.s1")).Missing);
    }

    [Fact]
    public void Cascade_preserves_alternate_path_through_border_from_either_side()
    {
        var s = New();
        Buy(s, "bolt.s1", "bolt.s4", "bolt.n2", "border.bolt-frost", "frost.n1", "frost.s2", "frost.s1");
        Assert.True(s.Apply(new RefundRune("bolt.s1")).IsOk);
        Assert.True(s.OwnsRune("bolt.n2"));
        Assert.True(s.OwnsRune("border.bolt-frost"));
        Assert.True(s.Apply(new RefundRune("frost.s1")).IsOk);
        Assert.Single(s.OwnedRunes);
        Assert.True(s.OwnsRune("core"));
        var other = New();
        Buy(other, "frost.s1", "frost.s2", "frost.n1", "border.bolt-frost", "bolt.n2");
    }

    [Fact]
    public void Commands_work_during_wave_and_pause_without_resetting_timers()
    {
        var s = New();
        Assert.True(s.Apply(new BuildTower("bolt", 1100)).IsOk);
        Assert.True(s.Apply(new StartWave()).IsOk);
        for (int i = 0; i < 40; i++) s.Step();
        s.Apply(new Pause());
        var tower = s.Towers[0];
        Buy(s, "bolt.s1", "bolt.s2", "bolt.n1");
        Assert.Equal(tower, s.Towers[0]);
        ulong hash = s.ComputeStateHash();
        for (int i = 0; i < 100; i++) s.Step();
        Assert.Equal(hash, s.ComputeStateHash());
        Assert.True(s.Apply(new RefundRune("bolt.s1")).IsOk);
        Assert.Equal(tower, s.Towers[0]);
        s.Apply(new Resume());
        Buy(s, "bolt.s1");
        Assert.True(s.Apply(new RefundRune("bolt.s1")).IsOk);
    }

    [Fact]
    public void Economy_modifiers_revert_and_integrity_cannot_be_farmed()
    {
        var s = New();
        long normal = s.TowerCostAt(0, 6);
        Buy(s, "general.s1", "general.s2", "general.n1");
        Assert.True(s.TowerCostAt(0, 6) < normal);
        long low = s.TowerCostAt(0, 6);
        Buy(s, "border.scaleset-general");
        Assert.True(s.TowerCostAt(0, 6) < low);
        Buy(s, "general.s4");
        Assert.Equal(s.TowerCostAt(0, 0) * 7500 / 10000, s.SellValueAt(0, 1));
        Buy(s, "general.n2", "general.s5", "general.s6");
        int integrity = s.Integrity;
        s.Apply(new RefundRune("general.s6"));
        Assert.Equal(integrity - 1, s.Integrity);
        Buy(s, "general.s6");
        Assert.Equal(integrity, s.Integrity);
        s.Apply(new RefundRune("general.s1"));
        Assert.Equal(normal, s.TowerCostAt(0, 6));
    }

    [Fact]
    public void Replay_orders_paused_rune_commands_and_hashes_owned_nodes()
    {
        var c = Rich();
        TdLogEntry[] log = [new(0, new Pause(), 0), new(0, new BuyRune("bolt.s1"), 1),
            new(0, new BuyRune("bolt.s2"), 2), new(0, new RefundRune("bolt.s2"), 3), new(0, new Resume(), 4), new(0, new StartWave(), 5)];
        var script = new TdReplayScript(6, 7, 54, log, 200);
        var manual = TdMatchState.Create(c, 6, Rng.Seed(7, 54));
        foreach (var e in log) Assert.True(manual.Apply(e.Command).IsOk);
        for (int i = 0; i < 200; i++) manual.Step();
        for (int i = 0; i < 5; i++)
        {
            var replay = TdReplayRunner.Run(c, script);
            Assert.Equal(0, replay.CommandsRejected);
            Assert.Equal(manual.ComputeStateHash(), replay.StateHash);
        }
        var a = New(); var b = New();
        Buy(a, "bolt.s1"); Buy(b, "cannon.s1");
        Assert.Equal(a.Credits, b.Credits);
        Assert.NotEqual(a.ComputeStateHash(), b.ComputeStateHash());
    }
    private static void Sector(TdMatchState s, string sector)
    {
        var pending = s.Content.Runes.Nodes.Where(n => n.Sector == sector).ToList();
        while (pending.Count > 0)
        {
            var node = pending.First(n => s.Content.Runes.Nodes.Any(o => s.OwnsRune(o.Id) &&
                (n.Links.Contains(o.Id, StringComparer.Ordinal) || o.Links.Contains(n.Id, StringComparer.Ordinal))));
            Buy(s, node.Id);
            pending.Remove(node);
        }
    }

    private static TdMatchState Arena(string tower, bool runes, params string[] sectors)
    {
        var c = Rich();
        c = c with
        {
            Enemies = c.Enemies with { Enemies = c.Enemies.Enemies.Select(e => e.Id == "grunt" ? e with { Hp = 100000, Speed = 0, Armor = 0 } : e).ToArray() },
            Waves = new TdWavesDef([.. c.Waves.Stages.Take(5), new TdStageWavesDef(6,
                [new TdWaveDef(1, false, 1, [new TdWaveGroupDef("grunt", 3, 0, 0)])])]),
        };
        var s = TdMatchState.Create(c, 6, Rng.Seed(7, 54));
        if (runes) foreach (string sector in sectors) Sector(s, sector);
        Assert.True(s.Apply(new BuildTower(tower, 1150)).IsOk);
        return s;
    }

    private static void Advance(TdMatchState s, int ticks)
    {
        Assert.True(s.Apply(new StartWave()).IsOk);
        for (int i = 0; i < ticks; i++) s.Step();
    }

    [Theory]
    [InlineData("bolt")]
    [InlineData("frost")]
    [InlineData("cannon")]
    public void Attack_sectors_apply_damage_control_and_keystones_in_combat(string type)
    {
        var baseline = Arena(type, false);
        var powered = Arena(type, true, type);
        Advance(baseline, 1); Advance(powered, 1);
        if (type == "bolt")
        {
            Assert.True(powered.Enemies[0].Hp < baseline.Enemies[0].Hp);
            Assert.True(powered.Towers[0].CooldownTicks > baseline.Towers[0].CooldownTicks); // Foco: −25% cadência
        }
        else if (type == "frost")
        {
            Assert.All(powered.Enemies, e => Assert.True(e.SlowTicks > 0)); // Inverno em área
            Assert.True(powered.Enemies[0].ParalyzeTicks > 0);
            Assert.Equal(0.15, powered.Enemies[0].VulnBonus, 6);
        }
        else
        {
            Assert.NotEmpty(powered.Zones);
            Assert.True(powered.Enemies[1].Hp < baseline.Enemies[1].Hp); // Estilhaço
        }
    }

    [Fact]
    public void Every_tower_of_type_including_later_builds_gets_runes_and_refund_removes_damage()
    {
        var s = Arena("bolt", true, "bolt");
        Assert.True(s.Apply(new BuildTower("bolt", 1100)).IsOk);
        Advance(s, 1);
        Assert.All(s.Towers, t => Assert.Equal(1, t.ShotCount));
        double poweredDamage = s.Enemies[0].MaxHp - s.Enemies[0].Hp;
        var baseline = Arena("bolt", false);
        baseline.Apply(new BuildTower("bolt", 1100));
        Advance(baseline, 1);
        Assert.True(poweredDamage > baseline.Enemies[0].MaxHp - baseline.Enemies[0].Hp);
        s.Apply(new RefundRune("bolt.s1"));
        var before = s.Towers.ToArray();
        Assert.All(before, t => Assert.Equal(1, t.ShotCount));
        double hp = s.Enemies[0].Hp;
        int cooldown = before[0].CooldownTicks;
        for (int i = 0; i < cooldown; i++) s.Step();
        Assert.Equal(2 * s.Content.Towers[0].Damage, hp - s.Enemies[0].Hp, 6);
    }

    [Theory]
    [InlineData("border.bolt-frost", "bolt", "frost", 2500)]
    [InlineData("border.frost-cannon", "cannon", "frost", 3000)]
    public void Slowed_target_border_bonus_requires_slow_and_disappears_on_refund(string border, string tower, string control, int bp)
    {
        var a = Arena(tower, true, tower, control);
        var b = Arena(tower, true, tower, control);
        Buy(b, border);
        // Sem lentidão, a fronteira não altera o primeiro tiro.
        Advance(a, 1); Advance(b, 1);
        Assert.Equal(a.Enemies[0].Hp, b.Enemies[0].Hp);
        // Adiciona Gelo durante a onda. O próximo tiro encontra o alvo lento.
        a.Apply(new BuildTower("frost", 1195)); b.Apply(new BuildTower("frost", 1195));
        for (int i = 0; i < 100; i++) { a.Step(); b.Step(); }
        Assert.True(b.Enemies[0].Hp < a.Enemies[0].Hp, bp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        b.Apply(new RefundRune(border));
        Assert.False(b.OwnsRune(border));
    }

    [Fact]
    public void Balancer_effects_and_conditional_duration_preserve_existing_timers()
    {
        var a = Arena("balancer", true, "balancer", "scaleset");
        var b = Arena("balancer", true, "balancer", "scaleset");
        Buy(b, "border.balancer-scaleset");
        // Anycast cria nó 90+6% à frente da torre; ambos têm inimigo em alcance.
        Advance(a, 1); Advance(b, 1);
        Assert.NotEmpty(a.Nodes);
        Assert.Equal(a.Nodes[0].LifeTicks, b.Nodes[0].LifeTicks); // falta Scale Set em campo
        Assert.True(a.Nodes[0].Holds > 1);
        Assert.True(a.Nodes[0].Dps > 4);
        Assert.Equal(1, a.Nodes[0].Flags & 1);
        var before = b.Nodes[0];
        b.Apply(new RefundRune("border.balancer-scaleset"));
        b.Apply(new RefundRune("balancer.s1"));
        Assert.Equal(before.LifeTicks, b.Nodes[0].LifeTicks);
        Assert.Equal(1, b.Nodes[0].Holds);
        Assert.Equal(4, b.Nodes[0].Dps);
        Assert.Equal(0, b.Nodes[0].Flags);
        var c = Arena("balancer", true, "balancer", "scaleset");
        Buy(c, "border.balancer-scaleset");
        c.Apply(new BuildTower("scaleset", 1000));
        Advance(c, 1);
        Assert.True(c.Nodes[0].LifeTicks > a.Nodes[0].LifeTicks);
    }

    [Fact]
    public void Support_runes_strengthen_neighbor_and_extend_scale_without_reset_on_refund()
    {
        var a = Arena("bolt", false);
        var b = Arena("bolt", true, "scaleset");
        a.Apply(new BuildTower("scaleset", 1050)); b.Apply(new BuildTower("scaleset", 1050));
        Advance(a, 1); Advance(b, 1);
        Assert.True(b.Enemies[0].Hp < a.Enemies[0].Hp);
        Assert.True(b.Towers[0].CooldownTicks < a.Towers[0].CooldownTicks);
        Assert.True(a.Apply(new UseAbility("scale")).IsOk);
        Assert.True(b.Apply(new UseAbility("scale")).IsOk);
        Assert.Equal(a.AbilityStatuses[0].ActiveTicks + 120, b.AbilityStatuses[0].ActiveTicks);
        var before = b.AbilityStatuses.ToArray();
        b.Apply(new RefundRune("scaleset.s1"));
        Assert.Equal(before, b.AbilityStatuses);
    }
    [Fact]
    public void Held_border_and_peak_border_are_conditional_damage_bonuses()
    {
        var a = Arena("cannon", true, "cannon", "balancer");
        var b = Arena("cannon", true, "cannon", "balancer");
        a.Apply(new RefundRune("balancer.k")); b.Apply(new RefundRune("balancer.k"));
        Buy(b, "border.cannon-balancer");
        a.Apply(new BuildTower("balancer", 1195)); b.Apply(new BuildTower("balancer", 1195));
        Advance(a, 1); Advance(b, 1);
        Assert.Equal(a.Enemies[0].Hp, b.Enemies[0].Hp);
        for (int i = 0; i < 140; i++) { a.Step(); b.Step(); }
        Assert.True(b.Enemies.Sum(e => e.Hp) < a.Enemies.Sum(e => e.Hp));
        var plain = Arena("bolt", false);
        var peak = Arena("bolt", false);
        Buy(plain, "bolt.s1", "bolt.s2", "bolt.n1");
        Buy(peak, "bolt.s1", "bolt.s2", "bolt.n1", "border.general-bolt");
        Advance(plain, 1); Advance(peak, 1);
        Assert.True(peak.Enemies[0].Hp < plain.Enemies[0].Hp);
    }

    [Fact]
    public void Piercing_and_fourth_shot_burst_use_runes_and_keep_shot_counter()
    {
        var s = Arena("bolt", true, "bolt");
        Advance(s, 1);
        double first = s.Enemies[0].MaxHp - s.Enemies[0].Hp;
        while (s.Towers[0].ShotCount < 3) s.Step();
        double hp = s.Enemies[0].Hp;
        while (s.Towers[0].ShotCount < 4) s.Step();
        Assert.True(hp - s.Enemies[0].Hp >= 2 * first);
        s.Apply(new RefundRune("bolt.n2"));
        Assert.Equal(4, s.Towers[0].ShotCount);
    }
    [Theory]
    [InlineData("bolt", "bolt.n1")]
    [InlineData("cannon", "cannon.n2")]
    public void Notables_improve_damage_against_armor(string tower, string notable)
    {
        var prototype = Arena(tower, false);
        var c = prototype.Content with { Enemies = prototype.Content.Enemies with
            { Enemies = prototype.Content.Enemies.Enemies.Select(e => e.Id == "grunt" ? e with { Armor = 10, Traits = ["armored"] } : e).ToArray() } };
        TdMatchState Make(bool own)
        {
            var s = TdMatchState.Create(c, 6, Rng.Seed(7, 54));
            Buy(s, tower + ".s1", tower + (tower == "bolt" ? ".s2" : ".s4"));
            if (own) Buy(s, notable);
            s.Apply(new BuildTower(tower, 1150));
            Advance(s, 1);
            return s;
        }
        var a = Make(false); var b = Make(true);
        Assert.True(b.Enemies[0].Hp < a.Enemies[0].Hp);
    }

    [Fact]
    public void General_runes_modify_wave_call_ability_and_diversity_and_respect_sell_cap()
    {
        var s = Arena("bolt", false);
        long waveBonus = s.WaveBonusFor(1);
        Sector(s, "general");
        Assert.Equal((long)Math.Ceiling(waveBonus * 1.08), s.WaveBonusFor(1));
        Assert.Equal(s.TowerCostAt(0, 0) * 8000 / 10000, s.SellValueAt(0, 1));
        s.Apply(new SetAutoStart(AutoStartMode.Countdown, 20));
        long credits = s.Credits;
        Advance(s, 1);
        Assert.True(s.Credits >= credits + 17); // ceil(20 × 0.65 × 1.25)
        Assert.True(s.Apply(new UseAbility("scale")).IsOk);
        Assert.Equal(1710, s.AbilityStatuses[0].CooldownTicks); // 60s × 95% × 30
        double firstDamage = s.Enemies[0].MaxHp - s.Enemies[0].Hp;
        Assert.Equal(Math.Floor(s.Content.Towers[0].Damage * 1.05), firstDamage);
        var status = s.AbilityStatuses[0];
        s.Apply(new RefundRune("general.s1"));
        Assert.Equal(status, s.AbilityStatuses[0]);
    }
}
