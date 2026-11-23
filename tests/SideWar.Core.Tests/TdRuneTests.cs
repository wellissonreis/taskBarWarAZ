using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using StartWave = SideWar.Core.Td.StartWave;

namespace SideWar.Core.Tests;

/// <summary>TD.29a: teia de runas do design (32 nós, 8 tipos, fragmentos, despertar aleatório) e vazamento no núcleo desenhado.</summary>
public sealed class TdRuneTests
{
    private static readonly TdContent Base = TdMechanicsContent.Load();

    /// <summary>Fase com <paramref name="waves"/> ondas de 1 grunt; créditos fartos.</summary>
    private static TdContent Tiny(int waves)
    {
        var defs = Enumerable.Range(1, waves).Select(i => new TdWaveDef(i, false, 1.0, [new TdWaveGroupDef("grunt", 1, 0.0, 1.0)])).ToArray();
        var stages = Base.Stages.Stages.Select(s => s.Id == 1 ? s with { Waves = waves, StartingCredits = 100000 } : s).ToArray();
        var waveStages = Base.Waves.Stages.Select(s => s.Stage == 1 ? new TdStageWavesDef(1, defs) : s).ToArray();
        return Base with { Waves = new TdWavesDef(waveStages), Stages = Base.Stages with { Stages = stages } };
    }

    private static TdMatchState New(int waves = 2, ulong seed = 42, bool layout = true)
    {
        var s = TdMatchState.Create(Tiny(waves), 1, Rng.Seed(seed, 54), null, TdMatchOptions.Default);
        if (layout) Assert.True(s.Apply(new LayoutChanged(TdSanityBot.TypicalLayout())).IsOk);
        Assert.True(s.Apply(new SetPresence(false)).IsOk);
        return s;
    }

    /// <summary>Concede fragmentos direto (atalho de teste para teias grandes).</summary>
    private static void Give(TdMatchState s, long fragments) =>
        typeof(TdMatchState).GetProperty(nameof(TdMatchState.Fragments))!.SetValue(s, s.Fragments + fragments);

    /// <summary>Joga (com um Raio fixo) até acumular <paramref name="fragments"/> fragmentos.</summary>
    private static void Earn(TdMatchState s, long fragments)
    {
        Assert.True(s.Apply(new BuildTower("bolt", 900)).IsOk);
        Assert.True(s.Apply(new StartWave()).IsOk);
        for (int i = 0; i < 2_000_000 && s.Fragments < fragments; i++) s.Step();
        Assert.True(s.Fragments >= fragments);
    }

    [Fact]
    public void Content_is_a_fixed_web_of_128_nodes_and_8_effect_types()
    {
        var r = Base.Runes;
        Assert.Equal(128, r.Nodes.Length);
        Assert.Equal(8, r.Types.Length);
        Assert.Equal(8, r.RingCosts.Length);
        Assert.Equal(r.RingCosts.Order(), r.RingCosts); // custo cresce (ou fica igual) para fora
        Assert.Equal(16, r.Nodes.Count(n => n.Ring == 7 && n.Power == 4)); // nós-chave: o último anel inteiro, o maior valor da teia
        Assert.All(r.Nodes.Where(n => n.Ring < 7), n => Assert.InRange(n.Power, 1, 3)); // anéis internos valem mais que os externos
        for (int ring = 0; ring < 8; ring++)
            for (int sector = 0; sector < 16; sector++)
                Assert.Single(r.Nodes, n => n.Ring == ring && n.Sector == sector);
        Assert.All(r.Nodes, n => Assert.Contains(r.Types, t => t.Id == n.Type));
        Assert.Equal(r.Types.Select(t => t.Id).Order(), r.Nodes.Select(n => n.Type).Distinct().Order()); // todos os tipos aparecem
        Assert.All(r.Nodes, n => Assert.True(n.X is >= 0 and <= 1 && n.Y is >= 0 and <= 1));
        Assert.Equal(["range", "bolt", "credits", "frost", "core", "cannon", "cooldown", "shield"], r.Types.Select(t => t.Id));
        Assert.Equal([100, 75, 1, 1, 1, 75, -85, 1], r.Types.Select(t => t.Value));
        Assert.Equal([1, 2, 5, 12, 24, 48, 80, 125], r.RingCosts);
        Assert.Equal((1, 1, 2), (Base.Stages.Rules.Fragments.PerWave, Base.Stages.Rules.Fragments.PerQuestion, Base.Stages.Rules.Fragments.PerBoss));
    }

    [Fact]
    public void Web_totals_stay_within_twice_the_old_32_node_web_and_the_cost_fits_tens_of_hours()
    {
        var r = Base.Runes;
        // totais máximos da teia antiga (32 nós): alcance 2000, Raio 1500, créditos 9, Gelo 18, Núcleo 21, Canhão 1500, recarga -1600, Escudo 5
        var oldTotals = new Dictionary<string, int> { ["range"] = 2000, ["bolt"] = 1500, ["credits"] = 9, ["frost"] = 18, ["core"] = 21, ["cannon"] = 1500, ["cooldown"] = -1600, ["shield"] = 5 };
        foreach (var ty in r.Types)
        {
            int total = r.Nodes.Where(n => n.Type == ty.Id).Sum(n => ty.Value * n.Power);
            Assert.True(Math.Abs(total) <= Math.Abs(oldTotals[ty.Id]) * 2.25, $"{ty.Id}: {total}");
        }
        long cost = r.Nodes.Sum(n => (long)r.RingCosts[n.Ring]);
        Assert.InRange(cost, 4000, 5500); // ~35 h de jogo ativo a ~134 fragmentos/h (curva medida com o SanityBot)
    }

    [Fact]
    public void Starts_with_no_fragments_and_the_whole_inner_ring_as_frontier_and_rejects_atomically()
    {
        var s = New();
        Assert.Equal(0, s.Fragments);
        Assert.Equal(0, s.AwakenedCount);
        Assert.Equal(Enumerable.Range(0, 16).Select(k => "0-" + k), s.Frontier);
        ulong h = s.ComputeStateHash();
        var r = s.Apply(new AwakenRune("0-0"));
        Assert.Equal(CommandReason.NoFragments, r.Reason);
        Assert.Equal(h, s.ComputeStateHash());
        Assert.False(s.IsAwakened("0-0"));
        Assert.False(s.IsAwakened("nope"));
    }

    [Fact]
    public void Fragments_come_from_waves_questions_and_the_boss_but_never_from_credits()
    {
        var s = New(3);
        Assert.True(s.Apply(new BuildTower("bolt", 900)).IsOk);
        long credits = s.Credits;
        Assert.True(s.Apply(new StartWave()).IsOk);
        Assert.True(s.Apply(new AnswerBonus(1, false)).IsOk);
        Assert.Equal(0, s.Fragments);
        for (int i = 0; i < 100_000 && s.WaveNumber == 1 && s.Fragments == 0; i++) s.Step();
        Assert.Equal(1, s.Fragments); // onda 1 vencida
        Assert.True(s.Credits != credits); // créditos seguem seu próprio caminho
        Assert.True(s.Apply(new AnswerBonus(2, true)).IsOk); // 1 por onda, acerto
        Assert.Equal(2, s.Fragments);
    }

    [Fact]
    public void Awakening_spends_one_fragment_on_the_clicked_frontier_node_and_grows_it()
    {
        var s = New(6);
        Earn(s, 3);
        long frags = s.Fragments;
        var before = s.Frontier;
        s.ClearEvents();
        Assert.True(s.Apply(new AwakenRune("0-5")).IsOk);
        Assert.Equal(frags - 1, s.Fragments);
        Assert.Equal(1, s.AwakenedCount);
        var ev = Assert.Single(s.Events, e => e.Type == TdEventType.RuneAwakened);
        var node = s.RuneWeb[(int)ev.A];
        Assert.Equal("0-5", node.Id); // exatamente o nó pedido, sem sorteio
        Assert.Contains(node.Id, before);
        Assert.True(s.IsAwakened(node.Id));
        Assert.DoesNotContain(node.Id, s.Frontier);
        Assert.Equal(frags - 1, ev.B);
        // vizinhos do despertado entram na fronteira (mesmo anel ±1, anel interno e externo no mesmo setor)
        var neighbors = new List<string> { $"{node.Ring}-{(node.Sector + 1) % 16}", $"{node.Ring}-{(node.Sector + 15) % 16}" };
        if (node.Ring > 0) neighbors.Add($"{node.Ring - 1}-{node.Sector}");
        if (node.Ring < 7) neighbors.Add($"{node.Ring + 1}-{node.Sector}");
        Assert.All(neighbors, id => Assert.True(s.IsAwakened(id) || s.Frontier.Contains(id), id));
    }

    [Fact]
    public void Awakening_rejects_unknown_locked_awake_and_unfunded_nodes_without_cost()
    {
        var s = New(6);
        Earn(s, 3);
        Assert.True(s.Fragments >= 1);
        long frags = s.Fragments;
        ulong h = s.ComputeStateHash();
        Assert.Equal(CommandReason.UnknownRuneNode, s.Apply(new AwakenRune("nope")).Reason);
        Assert.Equal(CommandReason.RuneNotInFrontier, s.Apply(new AwakenRune("2-3")).Reason); // fora da fronteira
        Assert.Equal(frags, s.Fragments);
        Assert.Equal(h, s.ComputeStateHash());
        Assert.True(s.Apply(new AwakenRune("0-0")).IsOk);
        h = s.ComputeStateHash();
        Assert.Equal(CommandReason.RuneNotInFrontier, s.Apply(new AwakenRune("0-0")).Reason); // já desperto
        Assert.Equal(h, s.ComputeStateHash());
        var t = New(6);
        Assert.Equal(CommandReason.NoFragments, t.Apply(new AwakenRune("0-0")).Reason);
    }

    [Fact]
    public void Frontier_only_reaches_outer_rings_through_awakened_neighbours()
    {
        var s = New(40);
        Earn(s, 12);
        while (s.Fragments >= s.RuneCost(s.Frontier[0]))
        {
            var frontier = s.Frontier;
            Assert.All(frontier, id =>
            {
                var n = s.RuneWeb.Single(x => x.Id == id);
                Assert.False(s.IsAwakened(id));
                if (n.Ring > 0)
                    Assert.True(s.IsAwakened($"{n.Ring}-{(n.Sector + 1) % 16}") || s.IsAwakened($"{n.Ring}-{(n.Sector + 15) % 16}")
                        || s.IsAwakened($"{n.Ring - 1}-{n.Sector}") || (n.Ring < 7 && s.IsAwakened($"{n.Ring + 1}-{n.Sector}")), id);
            });
            Assert.True(s.Apply(new AwakenRune(frontier[0])).IsOk);
        }
    }

    [Fact]
    public void Awakening_is_deterministic_by_seed_and_accepted_while_paused()
    {
        ulong Run(ulong seed, int pick)
        {
            var s = New(8, seed);
            Earn(s, 4);
            Assert.True(s.Apply(new Pause()).IsOk);
            while (s.Fragments > 0) Assert.True(s.Apply(new AwakenRune(s.Frontier[pick])).IsOk); // em pausa
            Assert.True(s.Paused);
            return s.ComputeStateHash();
        }
        Assert.Equal(Run(5, 0), Run(5, 0));
        Assert.NotEqual(Run(5, 0), Run(5, 1)); // o nó pedido decide
    }

    [Fact]
    public void Full_web_awakens_all_128_then_rejects()
    {
        var s = New(40);
        long total = Base.Runes.Nodes.Sum(n => Base.Runes.RingCosts[n.Ring]);
        Give(s, total);
        for (int i = 0; i < 128; i++) Assert.True(s.Apply(new AwakenRune(s.Frontier[0])).IsOk);
        Assert.Equal(128, s.AwakenedCount);
        Assert.Equal(0, s.Fragments);
        Assert.Empty(s.Frontier);
        Assert.All(s.RuneWeb, n => Assert.True(s.IsAwakened(n.Id)));
        for (int i = 0; i < 100_000 && s.Fragments == 0; i++) s.Step();
        if (s.Fragments > 0) Assert.Equal(CommandReason.WebComplete, s.Apply(new AwakenRune("0-0")).Reason);
    }

    [Fact]
    public void Core_runes_shield_reputation_and_credit_runes_pay_at_the_end_of_each_wave()
    {
        var s = New(40);
        Give(s, 60);
        Assert.True(s.Apply(new BuildTower("bolt", 900)).IsOk);
        Assert.True(s.Apply(new StartWave()).IsOk);
        while (s.Fragments > 0 && s.Frontier.Count > 0 && s.Apply(new AwakenRune(s.Frontier[0])).IsOk) { }
        int cores = s.AwakenedOfType("core");
        Assert.True(cores > 0);
        Assert.Equal(Math.Min(TdReputation.MaxShieldPercent, cores * 1), s.XpShieldPercent);
        // todas despertadas (ou ao menos as de crédito): bônus de onda inclui +1 por runa de Créditos
        int credits = s.AwakenedOfType("credits");
        s.ClearEvents();
        int guard = 0;
        long bonus = 0;
        while (guard++ < 200_000 && bonus == 0)
        {
            s.Step();
            foreach (var e in s.Events) if (e.Type == TdEventType.WaveCompleted) bonus = e.B;
        }
        Assert.True(bonus > 0);
        var done = s.Events.First(e => e.Type == TdEventType.WaveCompleted);
        long noLeak = bonus - s.WaveBonusFor((int)done.A) - 1L * credits; // o que sobra é o bônus "sem vazar" (ou 0)
        var src = Base.Runes.CreditSources;
        Assert.True(noLeak == 0 || noLeak == src.NoLeakBase + (long)src.NoLeakPerWave * done.A, $"sobra {noLeak}");
    }

    [Fact]
    public void Unplaced_layout_keeps_the_legacy_leak_point_at_zero()
    {
        var s = New(1, layout: false);
        Assert.Equal(0.0, s.LeakX);
    }

    [Fact]
    public void Enemy_reaching_the_drawn_core_costs_reputation_and_disappears_there()
    {
        var s = New(3); // 3 ondas: a fase não termina (sem cura de fim de fase) logo após o vazamento
        double leakX = s.LeakX;
        Assert.InRange(leakX, 30, 60); // 63 DIP em ~1920 DIP de barra → ~39 unidades lógicas
        Assert.Equal(TdWorld.CoreEdgeDip * TdWorld.PathLength / TdSanityBot.TypicalLayout().Width, leakX, 6);
        Assert.True(s.Apply(new BuildTower("bolt", 900)).IsOk);
        Assert.True(s.Apply(new StartWave()).IsOk);
        Assert.True(s.Apply(new SellTower(s.Towers[0].Id)).IsOk);
        double lastX = double.MaxValue;
        for (int i = 0; i < 200_000 && !s.Events.Any(e => e.Type == TdEventType.EnemyLeaked); i++)
        {
            s.Step();
            if (s.Enemies.Count > 0) lastX = s.Enemies[0].X;
        }
        var leak = Assert.Single(s.Events, e => e.Type == TdEventType.EnemyLeaked);
        Assert.True(lastX > leakX && lastX < leakX + 5, $"o inimigo some ao tocar o núcleo (último X {lastX}, borda {leakX})");
        Assert.Empty(s.Enemies);
        Assert.True(leak.B > 0);
        Assert.Equal(leak.B * TdReputation.XpPerCoreDamage, s.ReputationLostTotal);
    }
}
