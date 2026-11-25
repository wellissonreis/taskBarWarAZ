using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using SellTower = SideWar.Core.Td.SellTower;

namespace SideWar.Core.Tests;

/// <summary>Fusão por patente: de 2 (Estagiário) a 6 torres (Especialista) e receitas nomeadas que não dependem da ordem.</summary>
public sealed class TdFusionRankTests
{
    private static TdMatchState Arena(TdRankLevel rank = TdRankLevel.Arquiteto)
    {
        var m = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 6, Rng.Seed(7, 54), null, TdMatchOptions.Classic with { InitialReputation = TdRank.Threshold(rank) });
        m.Apply(new LayoutChanged(StripLayout.Create(1000, 954, 88, 4, 920, TowerHost.Band, [new FreeZone(32, 968)])));
        typeof(TdMatchState).GetProperty("Credits")!.SetValue(m, 1000000L);
        SetRank(m, rank);
        return m;
    }

    private static void SetRank(TdMatchState m, TdRankLevel rank) =>
        typeof(TdMatchState).GetProperty("GateReputation")!.SetValue(m, TdRank.Threshold(rank));

    private static int Build(TdMatchState m, string id)
    {
        for (int x = 500; x < 1200; x += 20)
            if (m.Apply(new BuildTower(id, x)).IsOk) return m.Towers[^1].Id;
        throw new InvalidOperationException("sem espaço para " + id);
    }

    private static int Fused(TdMatchState m, params string[] ids)
    {
        int host = Build(m, ids[0]);
        foreach (var id in ids.Skip(1))
        {
            var r = m.Apply(new FuseTowers(host, Build(m, id)));
            Assert.True(r.IsOk, $"{id}: {r.Reason}");
            host = m.Towers[^1].Id; // única entidade: a base pode ter trocado
        }
        return m.Towers[^1].Id;
    }

    private static int Def(TdMatchState m, string id) => Array.FindIndex(m.Content.Towers, t => t.Id == id);

    [Theory]
    [InlineData(TdRankLevel.Estagiario, 2)]
    [InlineData(TdRankLevel.Junior, 3)]
    [InlineData(TdRankLevel.Pleno, 4)]
    [InlineData(TdRankLevel.Senior, 5)]
    [InlineData(TdRankLevel.Especialista, 6)]
    [InlineData(TdRankLevel.Arquiteto, 6)]
    public void Rank_limits_how_many_towers_a_fusion_holds(TdRankLevel rank, int limit)
    {
        var m = Arena(rank);
        Assert.Equal(limit, m.FusionMaxComponents);
        int host = Fused(m, Enumerable.Repeat("bolt", limit).ToArray());
        Assert.Equal(limit, m.Towers.Single().ComponentTotal);
        int extra = Build(m, "frost");
        ulong hash = m.ComputeStateHash();
        var expected = limit == 6 ? CommandReason.AlreadyFused : CommandReason.FusionLimit;
        Assert.Equal(expected, m.Apply(new FuseTowers(host, extra)).Reason);
        Assert.Equal(expected, m.Apply(new FuseTower(host, "frost")).Reason);
        var preview = m.CheckFusionDrag(host, extra);
        Assert.Equal((limit + 1, limit), (preview.Count, preview.Limit));
        Assert.Equal(hash, m.ComputeStateHash());
    }

    [Fact]
    public void Merging_two_hybrids_counts_every_component()
    {
        var m = Arena(TdRankLevel.Pleno);
        int a = Fused(m, "bolt", "frost");
        int b = Build(m, "cannon");
        Assert.True(m.Apply(new FuseTowers(b, Build(m, "dns"))).IsOk);
        Assert.True(m.Apply(new FuseTowers(a, b)).IsOk);
        Assert.Equal(4, m.Towers.Single().ComponentTotal);
        Assert.Equal(["bolt", "cannon", "dns", "frost"], m.Towers[0].Components().Select(i => m.Content.Towers[i].Id).Order());
    }

    [Fact]
    public void Each_component_still_needs_its_own_rank()
    {
        var m = Arena(TdRankLevel.Arquiteto);
        int host = Build(m, "bolt"), cosmos = Build(m, "cosmos");
        SetRank(m, TdRankLevel.Junior);
        Assert.Equal(CommandReason.TowerLocked, m.Apply(new FuseTowers(host, cosmos)).Reason);
    }

    [Fact]
    public void Recipe_ignores_order_and_gets_its_name()
    {
        string[][] orders = [["bolt", "frost", "cannon"], ["cannon", "bolt", "frost"], ["frost", "cannon", "bolt"]];
        foreach (var order in orders)
        {
            var m = Arena(TdRankLevel.Junior);
            Fused(m, order);
            var t = m.Towers.Single();
            Assert.Equal("Zona de Disponibilidade", m.TowerDisplayName(t));
            Assert.Equal("cannon", m.Content.Towers[t.DefIndex].Id); // a mais cara vira base
            Assert.NotNull(m.RecipeBonus(t));
        }
    }

    [Fact]
    public void Extra_tower_keeps_the_contained_recipe_and_no_recipe_falls_back_to_generic()
    {
        var m = Arena(TdRankLevel.Pleno);
        Fused(m, "bolt", "frost", "cannon", "dns");
        Assert.Equal("Zona de Disponibilidade +1", m.TowerDisplayName(m.Towers.Single()));
        var g = Arena(TdRankLevel.Junior);
        Fused(g, "cannon", "dns", "dns");
        Assert.Null(g.RecipeBonus(g.Towers.Single()));
        Assert.EndsWith(" +1", g.TowerDisplayName(g.Towers[0]));
    }

    [Fact]
    public void Content_has_the_recipe_book_with_reachable_ranks_and_monotonic_bonus()
    {
        var m = Arena();
        var all = m.Content.Fusion.Recipes!;
        Assert.True(all.Count(r => r.Parts.Length == 3) >= 12);
        Assert.True(all.Count(r => r.Parts.Length == 4) >= 8);
        Assert.True(all.Count(r => r.Parts.Length == 5) >= 5);
        Assert.True(all.Count(r => r.Parts.Length == 6) >= 3);
        foreach (var r in all)
        {
            Assert.InRange(m.RecipeMinRank(r), r.Parts.Length - 2, 5);
            int[] parts = r.Parts.Select(id => Def(m, id)).ToArray();
            var own = m.RecipeBonus(parts)!;
            Assert.Equal(r.Name, own.Primary.Name);
            Assert.False(string.IsNullOrWhiteSpace(own.Text));
            if (parts.Length == 6) continue;
            var more = m.RecipeBonus([.. parts, Def(m, "bolt")])!; // somar uma torre nunca tira efeito
            Assert.True(more.DamageBp >= own.DamageBp && more.AuraBp >= own.AuraBp && more.HitDamagePct >= own.HitDamagePct && more.ChainJumps >= own.ChainJumps && more.WaveCredits >= own.WaveCredits);
            Assert.True(own.SlowFactor == 0 || more.SlowFactor <= own.SlowFactor);
        }
    }

    [Fact]
    public void Demotion_keeps_existing_fusion_but_blocks_new_ones_above_the_limit()
    {
        var m = Arena(TdRankLevel.Pleno);
        int host = Fused(m, "bolt", "frost", "cannon", "bolt");
        SetRank(m, TdRankLevel.Estagiario);
        Assert.Equal(4, m.Towers.Single().ComponentTotal);
        Assert.Equal(CommandReason.FusionLimit, m.Apply(new FuseTowers(host, Build(m, "dns"))).Reason);
        Assert.Equal(4, m.Towers.First(t => t.Id == host).ComponentTotal);
    }

    [Fact]
    public void Hybrid_survives_carry_and_sells_every_component_without_profit()
    {
        var m = Arena();
        long start = m.Credits;
        int host = Fused(m, "bolt", "frost", "cannon", "dns");
        Assert.Equal(1, m.TowerCount(Def(m, "dns")));
        var carry = m.ExportTowers();
        var next = Arena();
        Assert.Equal(1, next.ImportTowers(carry));
        Assert.Equal(m.Towers[0].Components(), next.Towers[0].Components());
        Assert.Equal(m.SellValue(host), next.SellValue(next.Towers[0].Id));
        Assert.True(m.Apply(new SellTower(host)).IsOk);
        Assert.True(m.Credits < start);
        Assert.Equal(0, m.TowerCount(Def(m, "dns")));
    }

    [Fact]
    public void Hash_covers_the_third_component_and_replays_deterministically()
    {
        ulong Run(string third)
        {
            var m = Arena();
            Fused(m, "cannon", "frost", third);
            for (int i = 0; i < 5; i++) m.Step();
            return m.ComputeStateHash();
        }
        Assert.Equal(Run("bolt"), Run("bolt"));
        Assert.NotEqual(Run("bolt"), Run("dns"));
    }

    [Fact]
    public void Recipe_raises_the_damage_of_the_hybrid_in_combat()
    {
        double Lost(params string[] ids)
        {
            var m = Arena();
            Fused(m, ids);
            Assert.True(m.Apply(new StartWave()).IsOk);
            for (int i = 0; i < 600 && m.Enemies.Count == 0; i++) m.Step();
            Assert.NotEmpty(m.Enemies);
            var enemies = (List<TdEnemy>)m.Enemies;
            double x = m.Towers[0].X;
            for (int i = 0; i < enemies.Count; i++) { var e = enemies[i]; e.X = x; e.Speed = 0; e.Hp = e.MaxHp = 1_000_000; enemies[i] = e; }
            for (int i = 0; i < 150; i++) m.Step();
            return m.Enemies.Sum(e => e.MaxHp - e.Hp);
        }
        double two = Lost("cannon", "bolt"), three = Lost("cannon", "bolt", "frost"), four = Lost("cannon", "bolt", "frost", "policy");
        Assert.True(three > two, $"{three} <= {two}");
        Assert.True(four > three, $"{four} <= {three}");
    }
}
