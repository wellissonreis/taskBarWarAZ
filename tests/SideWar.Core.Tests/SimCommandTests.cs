using SideWar.Core.Content;
using SideWar.Core.Simulation;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class SimCommandTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);

    private static StripLayout Layout(params float[] slots) =>
        StripLayout.Create(1000f, 94f, 10f, 980f, slots.Length == 0 ? new[] { 200f, 260f, 320f, 380f } : slots);

    private static SimState NewState(long credits = 100000, StripLayout? layout = null)
    {
        var s = SimState.CreateInitial(Content, layout ?? Layout(), Rng.Seed(42, 54));
        s.Credits = credits;
        return s;
    }

    [Fact(DisplayName = "T-11.9 Build: custos ceil(45×1,5^n) = 45, 68, 102 (Raio) e débito exato")]
    public void Build_Costs_Follow_Example()
    {
        var s = NewState(1000);
        Assert.True(s.Apply(Ctx, new BuildTower(TowerType.Bolt, 0)).IsOk);
        Assert.Equal(1000 - 45, s.Credits);
        Assert.True(s.Apply(Ctx, new BuildTower(TowerType.Bolt, 1)).IsOk);
        Assert.Equal(1000 - 45 - 68, s.Credits);
        Assert.True(s.Apply(Ctx, new BuildTower(TowerType.Bolt, 2)).IsOk);
        Assert.Equal(1000 - 45 - 68 - 102, s.Credits);
    }

    [Fact(DisplayName = "T-11.9 Build cria Level 1, Mode First, Id monotônico e emite TowerBuilt")]
    public void Build_Creates_Tower_And_Event()
    {
        var s = NewState();
        long c = s.Events.Written;
        int nextId = s.NextId;
        Assert.True(s.Apply(Ctx, new BuildTower(TowerType.Frost, 2)).IsOk);
        var h = s.FindTowerAtSlot(2);
        ref var t = ref s.Towers.Get(h);
        Assert.Equal((nextId, TowerType.Frost, 1, Targeting.First), (t.Id, t.Type, t.Level, t.Mode));
        Assert.Equal(nextId + 1, s.NextId);
        Assert.True(s.Events.TryRead(ref c, out var e));
        Assert.Equal(SimEventType.TowerBuilt, e.Type);
        Assert.Equal(320f, e.X);
        Assert.Equal(65.0, e.A);
    }

    [Fact(DisplayName = "T-11.9 Build: SlotMissing, SlotOccupied, TowerLocked e Missing correto")]
    public void Build_Validation_Order()
    {
        var s = NewState(10);
        Assert.Equal(CommandReason.SlotMissing, s.Apply(Ctx, new BuildTower(TowerType.Bolt, 4)).Reason);
        Assert.Equal(CommandReason.SlotMissing, s.Apply(Ctx, new BuildTower(TowerType.Bolt, -1)).Reason);
        var r = s.Apply(Ctx, new BuildTower(TowerType.Bolt, 0));
        Assert.Equal((CommandStatus.Rejected, CommandReason.InsufficientCredits, 35L), (r.Status, r.Reason, r.Missing));
        Assert.Equal(10, s.Credits);
        s.Credits = 1000;
        Assert.True(s.Apply(Ctx, new BuildTower(TowerType.Bolt, 0)).IsOk);
        Assert.Equal(CommandReason.SlotOccupied, s.Apply(Ctx, new BuildTower(TowerType.Frost, 0)).Reason);
        // slot ocupado é checado antes do saldo e de torre travada
        s.Credits = 0;
        Assert.Equal(CommandReason.SlotOccupied, s.Apply(Ctx, new BuildTower(TowerType.ScaleSet, 0)).Reason);
        Assert.Equal(CommandReason.TowerLocked, s.Apply(Ctx, new BuildTower(TowerType.ScaleSet, 1)).Reason);
        Assert.Equal(CommandReason.TowerLocked, s.Apply(Ctx, new BuildTower(TowerType.None, 1)).Reason);
    }

    [Fact(DisplayName = "T-11.9 Torre com unlockStage fica disponível ao chegar na fase")]
    public void Build_Unlock_By_Stage()
    {
        var s = NewState(1_000_000);
        s.Stage = 9;
        Assert.True(s.Apply(Ctx, new BuildTower(TowerType.ScaleSet, 0)).IsOk);
    }

    [Fact(DisplayName = "T-11.9 ADR-032: torres SEM SLOT contam no n do custo e ocupam o índice")]
    public void Build_Cost_Counts_NoSlot_Towers()
    {
        var s = NewState(100000);
        Assert.True(s.Apply(Ctx, new BuildTower(TowerType.Bolt, 0)).IsOk);
        Assert.True(s.Apply(Ctx, new BuildTower(TowerType.Bolt, 3)).IsOk);
        Assert.True(s.Apply(Ctx, new LayoutChanged(Layout(200f, 260f))).IsOk);   // torre do slot 3 fica SEM SLOT
        Assert.False(s.TowerHasSlot(in s.Towers.At(1)));
        long before = s.Credits;
        Assert.True(s.Apply(Ctx, new BuildTower(TowerType.Bolt, 1)).IsOk);
        Assert.Equal(before - 102, s.Credits);
    }

    [Fact(DisplayName = "T-11.9 Upgrade: ceil(54×1,18^(L−1)) = 54, 64; debita, sobe nível, conta manual e emite evento")]
    public void Upgrade_Costs()
    {
        var s = NewState(1000);
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, 0));
        int id = s.Towers.At(0).Id;
        long c = s.Events.Written;
        long start = s.Credits;
        Assert.True(s.Apply(Ctx, new UpgradeTower(id)).IsOk);
        Assert.Equal(start - 54, s.Credits);
        Assert.True(s.Apply(Ctx, new UpgradeTower(id)).IsOk);
        Assert.Equal(start - 54 - 64, s.Credits);
        Assert.Equal(3, s.Towers.At(0).Level);
        Assert.Equal(2, s.ManualUpgrades);
        SimEvent last = default;
        while (s.Events.TryRead(ref c, out var e)) last = e;
        Assert.Equal((SimEventType.TowerUpgraded, 3), (last.Type, last.B));
    }

    [Fact(DisplayName = "T-11.9 Upgrade: TowerNotFound, saldo insuficiente com Missing, MaxLevel 999 e inalcançável")]
    public void Upgrade_Validation()
    {
        var s = NewState(1000);
        Assert.Equal(CommandReason.TowerNotFound, s.Apply(Ctx, new UpgradeTower(77)).Reason);
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, 0));
        int id = s.Towers.At(0).Id;
        s.Credits = 50;
        var r = s.Apply(Ctx, new UpgradeTower(id));
        Assert.Equal((CommandReason.InsufficientCredits, 4L), (r.Reason, r.Missing));
        Assert.Equal(50, s.Credits);
        s.Credits = long.MaxValue;
        s.Towers.At(0).Level = 999;
        Assert.Equal(CommandReason.MaxLevel, s.Apply(Ctx, new UpgradeTower(id)).Reason);
        s.Towers.At(0).Level = 998;   // 54×1,18^997 ≫ long.MaxValue/2
        Assert.Equal(CommandReason.Unreachable, s.Apply(Ctx, new UpgradeTower(id)).Reason);
        Assert.Equal(998, s.Towers.At(0).Level);
    }

    [Fact(DisplayName = "T-11.9 Upgrade vale para torre SEM SLOT")]
    public void Upgrade_NoSlot_Tower()
    {
        var s = NewState();
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, 3));
        s.Apply(Ctx, new LayoutChanged(Layout(200f)));
        Assert.True(s.Apply(Ctx, new UpgradeTower(s.Towers.At(0).Id)).IsOk);
        Assert.Equal(2, s.Towers.At(0).Level);
    }

    [Fact(DisplayName = "T-11.9 SetTargeting muda o modo e não custa; torre inexistente rejeita")]
    public void SetTargeting_Works()
    {
        var s = NewState();
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, 0));
        long credits = s.Credits;
        int id = s.Towers.At(0).Id;
        Assert.True(s.Apply(Ctx, new SetTargeting(id, Targeting.Strong)).IsOk);
        Assert.Equal(Targeting.Strong, s.Towers.At(0).Mode);
        Assert.Equal(credits, s.Credits);
        Assert.Equal(CommandReason.TowerNotFound, s.Apply(Ctx, new SetTargeting(99, Targeting.Weak)).Reason);
    }

    [Fact(DisplayName = "T-11.9 Comando desconhecido e nulo rejeitam; durante o tick, Apply lança")]
    public void Unknown_And_InTick()
    {
        var s = NewState();
        Assert.Equal(CommandReason.UnknownCommand, s.Apply(Ctx, null).Reason);
        Assert.Equal(CommandReason.UnknownCommand, s.Apply(Ctx, new UnknownCmd()).Reason);
        s.BeginTick();
        Assert.Throws<InvalidOperationException>(() => s.Apply(Ctx, new BuildTower(TowerType.Bolt, 0)));
        Assert.Throws<InvalidOperationException>(() => s.Apply(Ctx, new LayoutChanged(Layout())));
        s.EndTick();
    }

    private sealed record UnknownCmd : IGameCommand;

    [Fact(DisplayName = "T-11.3 LayoutChanged aplicado entre ticks: troca o layout, limita inimigos, torre SEM SLOT, evento")]
    public void LayoutChanged_Applied()
    {
        var s = NewState();
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, 3));
        s.Enemies.Add(new Enemy { Id = s.TakeId(), X = 900f, Hp = 10, MaxHp = 10, Speed = 45f, SpawnSpeed = 45f });
        s.Enemies.Add(new Enemy { Id = s.TakeId(), X = 300f, Hp = 10, MaxHp = 10, Speed = 45f, SpawnSpeed = 45f });
        var small = StripLayout.Create(500f, 94f, 10f, 480f, new[] { 200f });
        long c = s.Events.Written;
        Assert.True(s.Apply(Ctx, new LayoutChanged(small)).IsOk);
        Assert.Same(small, s.Layout);
        Assert.Equal(500f, s.Enemies.At(0).X);
        Assert.Equal(300f, s.Enemies.At(1).X);
        Assert.False(s.TowerHasSlot(in s.Towers.At(0)));
        Assert.True(s.Events.TryRead(ref c, out var e));
        Assert.Equal((SimEventType.LayoutChanged, 500f, 1.0), (e.Type, e.X, e.A));
    }

    [Fact(DisplayName = "T-11.3 LayoutChanged nulo é rejeitado; layout inválido nem existe (TryCreate falha)")]
    public void LayoutChanged_Invalid()
    {
        var s = NewState();
        Assert.Equal(CommandReason.InvalidLayout, s.Apply(Ctx, new LayoutChanged(null!)).Reason);
        Assert.False(StripLayout.TryCreate(100f, 100f, 0, 0, ReadOnlySpan<float>.Empty, out _, out _));
        Assert.False(StripLayout.TryCreate(1000f, 94f, 0, 0, new float[13], out _, out _));
    }
}
