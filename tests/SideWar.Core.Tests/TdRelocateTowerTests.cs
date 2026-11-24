using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Core.Tests;

/// <summary>RelocateTower: mover uma torre já colocada (sem custo, recarga curta, preservando o estado).</summary>
public sealed class TdRelocateTowerTests
{
    private static TdMatchState New(params int[] x)
    {
        var s = TdMatchState.Create(TdMechanicsContent.Load(), 2, Rng.Seed(7, 54));
        Assert.True(s.Apply(new LayoutChanged(StripLayout.Create(1200, 1200, 0f, 0f, 1200, TowerHost.Taskbar, [new FreeZone(0, 1200)]))).IsOk);
        foreach (int v in x) Assert.True(s.Apply(new BuildTower("bolt", v)).IsOk);
        return s;
    }

    [Fact]
    public void Valid_move_changes_x_keeps_state_costs_nothing_and_starts_the_cooldown()
    {
        var s = New(100, 700);
        int id = s.Towers[0].Id;
        Assert.True(s.Apply(new SideWar.Core.Td.SetTargeting(id, TdTargeting.Strong)).IsOk);
        var before = s.Towers[0];
        long credits = s.Credits;
        Assert.True(s.Apply(new RelocateTower(id, 400)).IsOk);
        var t = s.Towers.First(x => x.Id == id);
        Assert.NotEqual(before.X, t.X);
        Assert.True(t.Placed);
        Assert.Equal(credits, s.Credits);
        Assert.Equal(before.DefIndex, t.DefIndex);
        Assert.Equal(before.PurchasePrice, t.PurchasePrice);
        Assert.Equal(before.Targeting, t.Targeting);
        Assert.Equal(before.ModuleSlot, t.ModuleSlot);
        Assert.True(t.CooldownTicks >= 30);
    }

    [Fact]
    public void Invalid_moves_are_rejected_without_changing_anything()
    {
        var s = New(100, 140);
        int a = s.Towers[0].Id;
        ulong h = s.ComputeStateHash();
        Assert.Equal(CommandReason.Overlap, s.Apply(new RelocateTower(a, 145)).Reason);
        Assert.Equal(CommandReason.OutOfZone, s.Apply(new RelocateTower(a, 5000)).Reason);
        Assert.Equal(CommandReason.TowerNotFound, s.Apply(new RelocateTower(99, 400)).Reason);
        Assert.Equal(h, s.ComputeStateHash());
    }

    [Fact]
    public void Unplaced_tower_is_rejected_and_same_spot_keeps_the_hash()
    {
        var s = New(100);
        int a = s.Towers[0].Id;
        ulong h = s.ComputeStateHash();
        Assert.True(s.Apply(new RelocateTower(a, s.Towers[0].X)).IsOk);
        Assert.Equal(h, s.ComputeStateHash());
        Assert.True(s.Apply(new LayoutChanged(StripLayout.Create(1200, 1200, 0f, 0f, 1200, TowerHost.Taskbar, [new FreeZone(600, 1200)]))).IsOk);
        Assert.Equal(CommandReason.NotPlaced, s.Apply(new RelocateTower(a, 800)).Reason);
    }

    [Fact]
    public void Same_commands_give_the_same_hash()
    {
        ulong Run()
        {
            var s = New(100, 700);
            s.Apply(new RelocateTower(s.Towers[0].Id, 400));
            for (int i = 0; i < 20; i++) s.Step();
            return s.ComputeStateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
