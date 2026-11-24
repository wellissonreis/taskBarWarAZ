using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Persistence.Profile;
using Xunit;

namespace SideWar.Persistence.Tests;

public sealed class TdBestiaryPersistenceTests
{
    [Theory]
    [InlineData(4499, TdRankLevel.Estagiario, 0)]
    [InlineData(4500, TdRankLevel.Junior, 5300)]
    [InlineData(23999, TdRankLevel.Junior, 5300)]
    [InlineData(24000, TdRankLevel.Pleno, 27600)]
    [InlineData(68000, TdRankLevel.Senior, 79200)]
    [InlineData(145000, TdRankLevel.Especialista, 167400)]
    [InlineData(300000, TdRankLevel.Arquiteto, 300000)]
    public void V8_migration_preserves_rank_and_resets_xp_to_its_floor(int oldXp, TdRankLevel rank, int newXp)
    {
        var old = new JsonObject { ["reputation"] = oldXp };
        Assert.True(TdProfileMigrations.Default.TryMigrate(TdProfileData.Schema, 8, 9, old, out var migrated, out _));
        Assert.Equal(newXp, migrated["reputation"]!.GetValue<int>());
        Assert.Equal(rank, TdRank.For(newXp));
    }

    [Fact]
    public void Core_encounters_record_base_and_elite_across_store_reload()
    {
        var content = TdContentLoader.LoadEmbedded();
        var match = TdMatchState.Create(content, 1, Rng.Seed(7, 54));
        int index = Array.FindIndex(content.Enemies.Enemies, e => e.Id == "swarm");
        var make = typeof(TdMatchState).GetMethod("MakeEnemy", BindingFlags.Instance | BindingFlags.NonPublic)!;
        make.Invoke(match, [index, 1, 100d, (ushort)1]);
        var p = TdProfileRules.RecordEncounters(TdProfileData.Default, match, match.Events);
        var fs = new FakeFileSystem();
        var store = new TdProfileStore(fs, "/data");
        Assert.True(store.Save(p));
        var reloaded = store.Load().Profile;
        Assert.Equal(1, reloaded.Encounters.Single(e => e.Id == "swarm").Count);
        Assert.Equal(1, reloaded.Encounters.Single(e => e.Id == "swarm:elite").Count);
        Assert.Same(reloaded, TdProfileRules.RecordEncounters(reloaded, match, []));
    }

    [Fact]
    public void V7_migrates_empty_and_encounters_survive_serialization_and_reset()
    {
        var old = JsonNode.Parse("{\"reputation\":42,\"runStageNumber\":7}")!;
        Assert.True(TdProfileMigrations.Default.TryMigrate(TdProfileData.Schema, 7, TdProfileData.SchemaVersion, old, out var migrated, out _));
        Assert.Equal(0, migrated["reputation"]!.GetValue<int>());
        Assert.Empty(migrated["encounters"]!.AsArray());
        var p = TdProfileData.Default with { Encounters = [new() { Id = "swarm", Count = 12 }, new() { Id = "swarm:elite", Count = 2 }] };
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) TdProfileSerializer.Write(writer, p);
        using var doc = JsonDocument.Parse(stream.ToArray());
        var read = TdProfileSerializer.Read(doc.RootElement);
        Assert.Equal(12, read.Encounters.Single(e => e.Id == "swarm").Count);
        Assert.Equal(2, read.Encounters.Single(e => e.Id == "swarm:elite").Count);
        Assert.Empty(TdProfileData.ResetToNew().Encounters);
    }

    [Fact]
    public void V9_migrates_without_run_towers_and_run_towers_survive_serialization_and_are_sanitized()
    {
        var old = JsonNode.Parse("{\"reputation\":10}")!;
        Assert.True(TdProfileMigrations.Default.TryMigrate(TdProfileData.Schema, 9, 10, old, out var migrated, out _));
        Assert.Empty(migrated["runTowers"]!.AsArray());
        var p = TdProfileData.Default with { RunTowers = [new() { DefId = "cannon", X = 300, Placed = false, PurchasePrice = 90, Targeting = 2, ModuleDefId = "bolt", ModulePrice = 40 }, new() { DefId = "", X = 1 }] };
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) TdProfileSerializer.Write(writer, p);
        using var doc = JsonDocument.Parse(stream.ToArray());
        var read = TdProfileSerializer.Read(doc.RootElement);
        var t = Assert.Single(read.RunTowers);
        Assert.Equal(("cannon", 300, false, 90L, 2, "bolt", 40L), (t.DefId, t.X, t.Placed, t.PurchasePrice, t.Targeting, t.ModuleDefId, t.ModulePrice));
        Assert.Empty(TdProfileData.ResetToNew().RunTowers);
    }
}
