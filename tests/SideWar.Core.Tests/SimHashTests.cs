using SideWar.Core.Content;
using SideWar.Core.Simulation;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class SimHashTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);

    private static StripLayout Layout() =>
        TestLayouts.FromCenters(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 380f });

    private static SimState Scripted(int ticks)
    {
        var s = SimState.CreateInitial(Content, Layout(), Rng.Seed(42, 54));
        s.Credits = 100000;
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, TestLayouts.X(0)));
        s.Apply(Ctx, new BuildTower(TowerType.Frost, TestLayouts.X(1)));
        s.Apply(Ctx, new BuildTower(TowerType.Cannon, TestLayouts.X(2)));
        for (int i = 0; i < ticks; i++)
        {
            if (i == 300) s.Apply(Ctx, new UpgradeTower(s.Towers.At(0).Id));
            if (i == 500) s.Apply(Ctx, new LayoutChanged(TestLayouts.FromCenters(800f, 94f, 10f, 780f, new[] { 200f, 260f, 320f })));
            s.Step(Ctx);
        }
        return s;
    }

    [Fact(DisplayName = "CA-11.1 StateHash: duas execuções do mesmo roteiro dão o mesmo hash, em 5 repetições")]
    public void Same_Script_Same_Hash()
    {
        ulong first = Scripted(900).ComputeStateHash();
        for (int i = 0; i < 4; i++) Assert.Equal(first, Scripted(900).ComputeStateHash());
        Assert.NotEqual(first, Scripted(901).ComputeStateHash());
    }

    [Fact(DisplayName = "CA-11.1 StateHash: hash do estado inicial é fixo (golden)")]
    public void Initial_Hash_Golden()
    {
        var s = SimState.CreateInitial(Content, Layout(), Rng.Seed(42, 54));
        Assert.Equal(GoldenInitial, s.ComputeStateHash());
    }

    [Fact(DisplayName = "CA-11.1 StateHash: roteiro de 900 ticks com construções, melhoria e LayoutChanged tem hash fixo (golden)")]
    public void Scripted_Hash_Golden() => Assert.Equal(GoldenScripted, Scripted(900).ComputeStateHash());

    private const ulong GoldenInitial = 10880851112086112302UL;
    private const ulong GoldenScripted = 6761761969928038329UL;

    private static SimState Base()
    {
        var s = SimState.CreateInitial(Content, Layout(), Rng.Seed(1, 2));
        s.Towers.Add(new Tower { Id = s.TakeId(), Type = TowerType.Bolt, Anchor = TowerAnchor.Start, OffsetDip = TestLayouts.X(0), Placed = true, Level = 1 });
        s.Enemies.Add(new Enemy { Id = s.TakeId(), Kind = EnemyKind.Grunt, X = 500f, Speed = 45f, SpawnSpeed = 45f, Hp = 42, MaxHp = 42 });
        s.Step(Ctx);
        return s;
    }

    public static TheoryData<string> Mutations() => new()
    {
        "tick", "stage", "wave", "falls", "credits", "carry", "reservas", "xp", "level", "basehp", "maxbasehp", "nextid",
        "manual", "missions", "missions.xor", "peak", "layout.spawn", "layout.leak", "layout.core", "layout.portal", "layout.slot", "layout.count", "layout.width", "layout.host", "layout.zone", "migrated",
        "tower.cooldown", "tower.level", "tower.mode", "tower.hits", "tower.anchor", "tower.offset", "tower.placed", "tower.type",
        "enemy.x", "enemy.speed", "enemy.spawnspeed", "enemy.hp", "enemy.maxhp", "enemy.armor", "enemy.flags", "enemy.lasthit", "enemy.kind",
        "waveclock", "rest", "nextspawn", "plan", "rng.state",
    };

    [Theory(DisplayName = "CA-11.1 StateHash: cada campo do estado altera o hash")]
    [MemberData(nameof(Mutations))]
    public void Each_Field_Changes_Hash(string what)
    {
        var s = Base();
        ulong before = s.ComputeStateHash();
        Mutate(s, what);
        Assert.NotEqual(before, s.ComputeStateHash());
    }

    private static StripLayout Rebuild(StripLayout l, int? width = null, TowerHost? host = null, int zoneDelta = 0)
    {
        var zones = l.FreeZones.ToArray();
        zones[^1] = new FreeZone(zones[^1].X0, zones[^1].X1 + zoneDelta);
        return StripLayout.Create(width ?? l.Width + zoneDelta, l.SpawnX, l.LeakX, l.CoreX, l.PortalX, host ?? l.Host, zones);
    }

    private static void Mutate(SimState s, string what)
    {
        switch (what)
        {
            case "tick": s.Tick++; break;
            case "stage": s.Stage++; break;
            case "wave": s.Wave++; break;
            case "falls": s.Falls++; break;
            case "credits": s.Credits++; break;
            case "carry": s.Carry += 0.5; break;
            case "reservas": s.Reservas++; break;
            case "xp": s.Xp++; break;
            case "level": s.Level++; break;
            case "basehp": s.BaseHp--; break;
            case "maxbasehp": s.MaxBaseHp++; break;
            case "nextid": s.NextId++; break;
            case "manual": s.ManualUpgrades++; break;
            case "missions": s.MissionsCollected++; break;
            case "missions.xor": s.MissionsCollectedXor ^= 0x5A; break;
            case "peak": s.PeakDamageFactor = 3.0; break;
            case "layout.spawn": s.ApplyLayout(TestLayouts.FromCenters(1001f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 380f })); break;
            case "layout.leak": s.ApplyLayout(TestLayouts.FromCenters(1000f, 95f, 10f, 980f, new[] { 200f, 260f, 320f, 380f })); break;
            case "layout.core": s.ApplyLayout(TestLayouts.FromCenters(1000f, 94f, 11f, 980f, new[] { 200f, 260f, 320f, 380f })); break;
            case "layout.portal": s.ApplyLayout(TestLayouts.FromCenters(1000f, 94f, 10f, 981f, new[] { 200f, 260f, 320f, 380f })); break;
            case "layout.slot": s.ApplyLayout(TestLayouts.FromCenters(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 381f })); break;
            case "layout.count": s.ApplyLayout(TestLayouts.FromCenters(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f })); break;
            case "tower.cooldown": s.Towers.At(0).Cooldown += 0.25f; break;
            case "tower.level": s.Towers.At(0).Level++; break;
            case "tower.mode": s.Towers.At(0).Mode = Targeting.Weak; break;
            case "tower.hits": s.Towers.At(0).Hits++; break;
            case "tower.anchor": s.Towers.At(0).Anchor = TowerAnchor.End; break;
            case "tower.offset": s.Towers.At(0).OffsetDip++; break;
            case "tower.placed": s.Towers.At(0).Placed = !s.Towers.At(0).Placed; break;
            case "layout.width": s.ApplyLayout(Rebuild(s.Layout, width: s.Layout.Width + 100)); break;
            case "layout.host": s.ApplyLayout(Rebuild(s.Layout, host: TowerHost.Band)); break;
            case "layout.zone": s.ApplyLayout(Rebuild(s.Layout, zoneDelta: 1)); break;
            case "migrated": s.MigratedSlotsPending = true; break;
            case "tower.type": s.Towers.At(0).Type = TowerType.Frost; break;
            case "enemy.x": s.Enemies.At(0).X += 1f; break;
            case "enemy.speed": s.Enemies.At(0).Speed -= 1f; break;
            case "enemy.spawnspeed": s.Enemies.At(0).SpawnSpeed += 1f; break;
            case "enemy.hp": s.Enemies.At(0).Hp -= 1; break;
            case "enemy.maxhp": s.Enemies.At(0).MaxHp += 1; break;
            case "enemy.armor": s.Enemies.At(0).Armor++; break;
            case "enemy.flags": s.Enemies.At(0).Flags |= EnemyFlags.Chilled; break;
            case "enemy.lasthit": s.Enemies.At(0).LastHit = TowerType.Cannon; break;
            case "enemy.kind": s.Enemies.At(0).Kind = EnemyKind.Tank; break;
            case "waveclock": s.WaveClock += 0.5f; break;
            case "rest": s.Rest += 0.5f; break;
            case "nextspawn": s.RestoreNextSpawn(s.NextSpawnIndex + 1); break;
            case "plan": s.Wave = 5; s.StartWave(Ctx); break;
            case "rng.state": s.Rng.NextUInt(); break;
            default: throw new ArgumentException(what);
        }
    }

    [Fact(DisplayName = "CA-11.1 StateHash: floats pelos bits (0,0 ≠ −0,0) e doubles pelos bits")]
    public void Floats_By_Bits()
    {
        var s = Base();
        s.Enemies.At(0).X = 0f;
        ulong a = s.ComputeStateHash();
        s.Enemies.At(0).X = -0f;
        Assert.NotEqual(a, s.ComputeStateHash());
    }

    [Fact(DisplayName = "CA-11.1 StateHash: Events fora do hash")]
    public void Events_Not_In_Hash()
    {
        var s = Base();
        ulong a = s.ComputeStateHash();
        s.Events.Add(new SimEvent(SimEventType.Kill, 1f, 0f, 2.0, 3));
        Assert.Equal(a, s.ComputeStateHash());
    }

    [Fact(DisplayName = "CA-11.1 StateHash: independe do slot de memória (ordem por Id), não da posição no array")]
    public void Order_By_Id_Not_Slot()
    {
        SimState Make(bool swap)
        {
            var s = SimState.CreateInitial(Content, Layout(), Rng.Seed(1, 2));
            var a = new Tower { Id = 1, Type = TowerType.Bolt, Anchor = TowerAnchor.Start, OffsetDip = TestLayouts.X(0), Placed = true, Level = 1 };
            var b = new Tower { Id = 2, Type = TowerType.Frost, Anchor = TowerAnchor.Start, OffsetDip = TestLayouts.X(1), Placed = true, Level = 1 };
            if (swap) { s.Towers.Add(b); s.Towers.Add(a); } else { s.Towers.Add(a); s.Towers.Add(b); }
            s.NextId = 3;
            return s;
        }
        Assert.Equal(Make(false).ComputeStateHash(), Make(true).ComputeStateHash());
    }

    [Fact(DisplayName = "CA-11.1 Restore: plano reconstruído + RestoreNextSpawn reproduz o hash e a continuação")]
    public void Restore_Wave_Position()
    {
        var s = Scripted(400);
        Assert.True(s.NextSpawnIndex > 0);
        ulong h = s.ComputeStateHash();
        var r = SimState.CreateInitial(Content, s.Layout, Rng.FromState(s.Rng.State, s.Rng.Inc));
        r.Credits = s.Credits; r.Carry = s.Carry; r.Reservas = s.Reservas; r.Xp = s.Xp; r.Level = s.Level;
        r.BaseHp = s.BaseHp; r.MaxBaseHp = s.MaxBaseHp; r.Tick = s.Tick; r.Stage = s.Stage; r.Wave = s.Wave; r.Falls = s.Falls;
        r.NextId = s.NextId; r.ManualUpgrades = s.ManualUpgrades; r.WaveClock = s.WaveClock; r.Rest = s.Rest;
        for (int i = 0; i < s.Towers.Capacity; i++) if (s.Towers.IsAlive(i)) r.Towers.Add(s.Towers.At(i));
        for (int i = 0; i < s.Enemies.Capacity; i++) if (s.Enemies.IsAlive(i)) r.Enemies.Add(s.Enemies.At(i));
        r.StartWave(Ctx);
        r.WaveClock = s.WaveClock; r.Rest = s.Rest;
        r.RestoreNextSpawn(s.NextSpawnIndex);
        Assert.Equal(h, r.ComputeStateHash());
        for (int i = 0; i < 100; i++) { s.Step(Ctx); r.Step(Ctx); }
        Assert.Equal(s.ComputeStateHash(), r.ComputeStateHash());
        Assert.Throws<ArgumentOutOfRangeException>(() => r.RestoreNextSpawn(100000));
        Assert.Throws<InvalidOperationException>(() => new SimState(Layout()).RestoreNextSpawn(0));
    }

    [Fact(DisplayName = "RNF-11.2 ComputeStateHash e Apply (rejeitado) não alocam")]
    public void Hash_Zero_Alloc()
    {
        var s = Scripted(200);
        s.ComputeStateHash();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) s.ComputeStateHash();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
