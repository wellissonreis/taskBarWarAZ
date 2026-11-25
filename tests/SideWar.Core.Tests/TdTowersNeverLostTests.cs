using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using SetTargeting = SideWar.Core.Td.SetTargeting;

namespace SideWar.Core.Tests;

public sealed class TdTowersNeverLostTests
{
    private static readonly TdContent Content = TdContentLoader.LoadEmbedded();

    private static TdMatchState WithTowers(int stage, TdMatchOptions? options = null)
    {
        var m = TdMatchState.Create(Content, stage, Rng.Seed(7, 54), null, options ?? TdMatchOptions.Default with { InitialReputation = 0 });
        m.Apply(new LayoutChanged(TdSanityBot.TypicalLayout()));
        typeof(TdMatchState).GetProperty("Credits")!.SetValue(m, 5000L);
        string id = Content.Towers.First(t => t.MinRank <= 0).Id;
        for (int x = 120; x < 480 && m.Towers.Count < 4; x += 50) m.Apply(new BuildTower(id, x));
        return m;
    }

    [Fact]
    public void Carry_keeps_type_position_price_targeting_and_ids_in_a_new_stage()
    {
        var old = WithTowers(1);
        Assert.True(old.Towers.Count >= 3);
        old.Apply(new SetTargeting(old.Towers[1].Id, TdTargeting.Strong));
        var next = TdMatchState.Create(Content, 2, Rng.Seed(9, 54), null, TdMatchOptions.Default with { InitialReputation = 5400 });
        int n = next.ImportTowers(old.ExportTowers());
        next.Apply(new LayoutChanged(TdSanityBot.TypicalLayout()));
        Assert.Equal(old.Towers.Count, n);
        Assert.Equal(old.Towers.Count, next.Towers.Count);
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(old.Towers[i].DefIndex, next.Towers[i].DefIndex);
            Assert.Equal(old.Towers[i].X, next.Towers[i].X);
            Assert.Equal(old.Towers[i].PurchasePrice, next.Towers[i].PurchasePrice);
            Assert.Equal(old.Towers[i].Targeting, next.Towers[i].Targeting);
            Assert.True(next.Towers[i].Placed);
        }
        // hash coerente: duas importações idênticas dão o mesmo estado
        var again = TdMatchState.Create(Content, 2, Rng.Seed(9, 54), null, TdMatchOptions.Default with { InitialReputation = 5400 });
        again.ImportTowers(old.ExportTowers());
        again.Apply(new LayoutChanged(TdSanityBot.TypicalLayout()));
        Assert.Equal(next.ComputeStateHash(), again.ComputeStateHash());
    }

    [Fact]
    public void Unplaced_towers_snap_to_the_nearest_valid_point_and_the_rest_stay_in_reserve_without_refund()
    {
        var old = WithTowers(1);
        var next = TdMatchState.Create(Content, 1, Rng.Seed(9, 54));
        next.ImportTowers(old.ExportTowers());
        long credits = next.Credits;
        // só cabe 1 torre (zona de 44 DIP): as demais ficam na reserva, nenhuma some
        next.Apply(new LayoutChanged(StripLayout.Create(1920, 1920, 0f, 0f, 1920, TowerHost.Taskbar, [new FreeZone(1300, 1344)])));
        Assert.Equal(old.Towers.Count, next.Towers.Count);
        Assert.Equal(0, next.Towers.Count(t => t.Placed));
        Assert.Equal(1, next.AutoPlaceReserve());
        Assert.Equal(1, next.Towers.Count(t => t.Placed));
        Assert.Equal(old.Towers.Count, next.Towers.Count);
        Assert.Equal(credits, next.Credits);
        // layout amplo: todas cabem perto do lugar original
        next.Apply(new LayoutChanged(TdSanityBot.TypicalLayout()));
        next.AutoPlaceReserve();
        Assert.All(next.Towers, t => Assert.True(t.Placed));
    }

    [Fact]
    public void Import_only_fills_an_empty_match()
    {
        var old = WithTowers(1);
        var other = WithTowers(1);
        int before = other.Towers.Count;
        Assert.Equal(0, other.ImportTowers(old.ExportTowers()));
        Assert.Equal(before, other.Towers.Count);
    }

    [Fact]
    public void Run_at_the_rank_cap_repeats_the_region_and_keeps_going_with_towers()
    {
        // Estagiário (XP 0) só pode a região 1: ao vencê-la a corrida repete a região 1 sozinha, com as torres, e segue com ondas.
        int towersAtEnd = -1, maxBuilt = 0;
        // Escala de campanha neutra: mede a repetição da região e a preservação das torres, não o balanceamento.
        var neutral = Content with { Stages = Content.Stages with { Stages = Content.Stages.Stages.Select(s => s with { HpStart = 1, HpEnd = 1, BossHp = 1 }).ToArray() } };
        var r = TdSanityBot.RunContinuous(neutral, 1, TdBotProfile.Otimizado, 100, 2, true, m => { maxBuilt = Math.Max(maxBuilt, m.Towers.Count); }, true, 0);
        towersAtEnd = r.TowersAtEnd;
        Assert.Equal(2, r.StagesCleared);
        Assert.InRange(r.StageNumber, 1, 3); // as fases longas rendem XP para destravar até a região 3, nunca além em duas vitórias
        Assert.True(towersAtEnd >= 1, "torres preservadas na repetição");
        Assert.True(towersAtEnd >= maxBuilt - 2);
        Assert.Contains(r.Phase, new[] { MatchPhase.Onda, MatchPhase.Preparo });
    }
}
