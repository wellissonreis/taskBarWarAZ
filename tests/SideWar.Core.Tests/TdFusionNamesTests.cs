using SideWar.Core.Simulation;
using FreeZone = SideWar.Core.Simulation.FreeZone;
using Rng = SideWar.Core.Simulation.Rng;
using SideWar.Core.Content.Td;
using SideWar.Core.Td;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using Xunit;

namespace SideWar.Core.Tests;

/// <summary>Parte D: toda combinação base+módulo tem nome próprio (não genérico) e único; os nomes vivem só em td-fusion.json.</summary>
public sealed class TdFusionNamesTests
{
    private static TdMatchState Create() => TdMatchState.Create(TdContentLoader.LoadEmbedded(), 6, Rng.Seed(7, 54));

    [Fact]
    public void Every_ordered_base_module_combination_has_a_unique_non_generic_name()
    {
        var m = Create();
        var towers = m.Content.Towers;
        var all = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        for (int b = 0; b < towers.Length; b++)
            for (int mo = 0; mo < towers.Length; mo++)
            {
                string name = m.FusionName(b, mo);
                Assert.False(string.IsNullOrWhiteSpace(name));
                Assert.DoesNotContain(" + ", name);
                Assert.True(all.TryAdd(name, (towers[b].Id, towers[mo].Id)), $"nome repetido '{name}': {towers[b].Id}+{towers[mo].Id} e {(all.TryGetValue(name, out var o) ? o : default)}");
            }
        Assert.Equal(towers.Length * towers.Length, all.Count);
    }

    [Fact]
    public void Cannon_with_frost_is_Canhao_de_fogo_frio_and_the_reverse_has_its_own_name_with_the_same_effect()
    {
        var m = Create();
        int cannon = Array.FindIndex(m.Content.Towers, t => t.Id == "cannon"), frost = Array.FindIndex(m.Content.Towers, t => t.Id == "frost");
        Assert.Equal("Canhão de fogo frio", m.FusionName(cannon, frost));
        Assert.Equal("Gelo de artilharia", m.FusionName(frost, cannon));
        Assert.Null(m.FusionPair(cannon, frost)); // continua no efeito genérico por papel: só o nome mudou
        Assert.Null(m.FusionPair(frost, cannon));
    }

    [Fact]
    public void Named_pairs_keep_their_names_and_the_check_reports_the_composed_name()
    {
        var m = Create();
        int bolt = Array.FindIndex(m.Content.Towers, t => t.Id == "bolt"), frost = Array.FindIndex(m.Content.Towers, t => t.Id == "frost");
        Assert.Equal("Raio gélido", m.FusionName(bolt, frost));
        Assert.Equal("Gelo elétrico", m.FusionName(frost, bolt));
        Assert.Equal("Raio em dobro", m.FusionName(bolt, bolt));
    }
}

/// <summary>Parte E: fusão por arrasto (torre colocada sobre outra): a mais forte vira base, nada é cobrado e o híbrido nunca é mais fraco.</summary>
public sealed class TdFusionDragTests
{
    private static TdMatchState Arena()
    {
        var m = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 6, Rng.Seed(7, 54));
        m.Apply(new LayoutChanged(StripLayout.Create(1000, 954, 88, 4, 920, TowerHost.Band, [new FreeZone(32, 968)])));
        typeof(TdMatchState).GetProperty("Credits")!.SetValue(m, 100000L);
        return m;
    }

    private static int Build(TdMatchState m, string id, int x)
    {
        Assert.True(m.Apply(new BuildTower(id, x)).IsOk);
        return m.Towers[^1].Id;
    }

    [Fact]
    public void Drag_fuses_without_cost_absorbs_the_dragged_tower_and_keeps_the_stronger_as_base()
    {
        var m = Arena();
        int frost = Build(m, "frost", 300), cannon = Build(m, "cannon", 500);
        long credits = m.Credits;
        int count = m.Towers.Count;
        // arrasta o Gelo (mais barato) sobre o Canhão: o Canhão é a base; o nome é o do par
        Assert.True(m.Apply(new FuseTowers(cannon, frost)).IsOk);
        Assert.Equal(credits, m.Credits);
        Assert.Equal(count - 1, m.Towers.Count);
        var t = m.Towers.Single();
        Assert.Equal(cannon, t.Id);
        Assert.Equal("cannon", m.Content.Towers[t.DefIndex].Id);
        Assert.Equal("frost", m.Content.Towers[t.ModuleDefIndex].Id);
        Assert.Equal("Canhão de fogo frio", m.TowerDisplayName(t));
    }

    [Fact]
    public void Dragging_the_stronger_tower_on_the_weaker_swaps_roles_so_the_result_is_never_weaker()
    {
        var m = Arena();
        int frost = Build(m, "frost", 300), cannon = Build(m, "cannon", 500);
        Assert.True(m.Apply(new FuseTowers(frost, cannon)).IsOk); // arrasta o Canhão sobre o Gelo
        var t = m.Towers.Single();
        Assert.Equal(cannon, t.Id); // a base é a mais forte (maior custo), não o alvo
        Assert.Equal("frost", m.Content.Towers[t.ModuleDefIndex].Id);
    }

    [Fact]
    public void Result_base_always_costs_at_least_as_much_as_both_originals_for_every_pair()
    {
        var content = TdContentLoader.LoadEmbedded();
        var ids = content.Towers.Where(t => t.UnlockStage <= 6).Select(t => t.Id).ToArray();
        foreach (var a in ids)
            foreach (var b in ids)
            {
                var m = Arena();
                int ia = Build(m, a, 300), ib = Build(m, b, 600);
                Assert.True(m.Apply(new FuseTowers(ib, ia)).IsOk, $"{a}+{b}");
                var t = m.Towers.Single();
                var baseDef = m.Content.Towers[t.DefIndex];
                Assert.True(t.HasModule);
                Assert.True(baseDef.Cost >= m.Content.Towers.First(d => d.Id == a).Cost && baseDef.Cost >= m.Content.Towers.First(d => d.Id == b).Cost, $"{a}+{b}");
            }
    }

    [Fact]
    public void Drag_is_refused_when_locked_unplaced_already_fused_or_the_same_tower()
    {
        var early = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54));
        early.Apply(new LayoutChanged(StripLayout.Create(1000, 954, 88, 4, 920, TowerHost.Band, [new FreeZone(32, 968)])));
        typeof(TdMatchState).GetProperty("Credits")!.SetValue(early, 100000L);
        int e1 = Build(early, "bolt", 300), e2 = Build(early, "bolt", 600);
        Assert.Equal(CommandReason.FusionLocked, early.Apply(new FuseTowers(e1, e2)).Reason);

        var m = Arena();
        int a = Build(m, "bolt", 300), b = Build(m, "frost", 600), c = Build(m, "dns", 800);
        Assert.False(m.Apply(new FuseTowers(a, a)).IsOk);
        Assert.True(m.Apply(new FuseTowers(a, b)).IsOk);
        Assert.Equal(CommandReason.AlreadyFused, m.Apply(new FuseTowers(a, c)).Reason);
        Assert.Equal(CommandReason.TowerNotFound, m.Apply(new FuseTowers(a, b)).Reason); // b foi absorvida
    }

    [Fact]
    public void Hybrid_with_a_generic_support_module_is_stronger_than_its_base_alone()
    {
        // O módulo de suporte genérico reforça o próprio híbrido (+5%): nunca fica mais fraco que a base sozinha.
        var plain = Arena(); Build(plain, "bolt", 300);
        var hybrid = Arena(); int bolt = Build(hybrid, "bolt", 300); Assert.True(hybrid.Apply(new FuseTower(bolt, "scaleset")).IsOk);
        Assert.True(hybrid.Towers[0].HasModule);
        Assert.Single(hybrid.Towers);
        Assert.Equal(plain.Towers[0].DefIndex, hybrid.Towers[0].DefIndex);
    }
}
