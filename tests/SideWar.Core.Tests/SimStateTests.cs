using SideWar.Core.Content;
using SideWar.Core.Simulation;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class SimStateTests
{
    private static StripLayout Layout(float spawn = 1000f, int slots = 4) =>
        StripLayout.Create(spawn, 94f, 10f, spawn - 20f, Enumerable.Range(0, slots).Select(i => 200f + 60f * i).ToArray());

    private static Enemy E(int id, float x) => new() { Id = id, Kind = EnemyKind.Grunt, X = x, Hp = 10, MaxHp = 10, Speed = 45, SpawnSpeed = 45 };
    private static Tower T(int id, int slot) => new() { Id = id, Type = TowerType.Bolt, SlotIndex = slot, Level = 1 };

    [Fact(DisplayName = "T-11.11 Handle layout: index, generation, None")]
    public void Handle_Encoding()
    {
        var h = Handle.Make(37, 5);
        Assert.Equal(37, h.Index);
        Assert.Equal(5, h.Generation);
        Assert.True(Handle.None.IsNone);
        var big = Handle.Make(255, Handle.MaxGeneration);
        Assert.Equal(255, big.Index);
        Assert.Equal(Handle.MaxGeneration, big.Generation);
        Assert.False(big.IsNone);
    }

    [Fact(DisplayName = "T-11.11 Stale handle is invalid after removal and slot reuse")]
    public void Store_StaleHandle()
    {
        var s = new EnemyStore();
        var a = s.Add(E(1, 100));
        Assert.True(s.IsValid(a));
        Assert.Equal(1, s.Count);
        Assert.True(s.Remove(a));
        Assert.False(s.IsValid(a));
        Assert.False(s.Remove(a));
        Assert.Throws<InvalidOperationException>(() => s.Get(a));
        var b = s.Add(E(2, 100));
        Assert.Equal(a.Index, b.Index);
        Assert.Equal(a.Generation + 1, b.Generation);
        Assert.False(s.IsValid(a));
        Assert.Equal(2, s.Get(b).Id);
        Assert.False(s.TryGet(a, out _));
        Assert.True(s.TryGet(b, out var copy));
        Assert.Equal(2, copy.Id);
        Assert.False(s.IsValid(Handle.None));
    }

    [Fact(DisplayName = "T-11.11 Capacity, free list order and Clear")]
    public void Store_CapacityAndFreeList()
    {
        var s = new TowerStore();
        Assert.Equal(16, s.Capacity);
        Assert.Equal(64, new EnemyStore().Capacity);
        var hs = new Handle[16];
        for (int i = 0; i < 16; i++) { hs[i] = s.Add(T(i + 1, i)); Assert.Equal(i, hs[i].Index); Assert.Equal(1, hs[i].Generation); }
        Assert.True(s.Add(T(99, 0)).IsNone);
        s.Remove(hs[3]); s.Remove(hs[9]);
        Assert.Equal(9, s.Add(T(100, 0)).Index);  // LIFO
        Assert.Equal(3, s.Add(T(101, 0)).Index);
        s.Clear();
        Assert.Equal(0, s.Count);
        Assert.False(s.IsValid(hs[0]) && s.IsAlive(0));
        Assert.Equal(0, s.Add(T(1, 0)).Index);
    }

    [Fact(DisplayName = "T-11.11 Generation wraps from 2^24-1 to 1 and never to 0")]
    public void Store_GenerationWraps()
    {
        var s = new EnemyStore();
        Handle prev = Handle.None;
        for (int n = 1; n <= Handle.MaxGeneration; n++)
        {
            var h = s.Add(E(1, 1));
            Assert.Equal(n, h.Generation);
            if (!prev.IsNone) Assert.False(s.IsValid(prev));
            s.Remove(h);
            prev = h;
        }
        var wrapped = s.Add(E(2, 1));
        Assert.Equal(1, wrapped.Generation);
        Assert.False(wrapped.IsNone);
        Assert.False(s.IsValid(prev));
    }

    [Fact(DisplayName = "T-11.11 Id-ascending iteration after slot reuse")]
    public void Store_IdOrder()
    {
        var s = new TowerStore();
        var h1 = s.Add(T(1, 0)); s.Add(T(2, 1)); s.Add(T(3, 2));
        s.Remove(h1);
        s.Add(T(4, 3)); // reutiliza o índice 0
        Span<int> idx = stackalloc int[TowerStore.CapacityConst];
        int n = s.CopyIndicesByIdAscending(idx);
        Assert.Equal(3, n);
        Assert.Equal([2, 3, 4], new[] { s.At(idx[0]).Id, s.At(idx[1]).Id, s.At(idx[2]).Id });
        Assert.Throws<ArgumentException>(() => s.CopyIndicesByIdAscending(new int[1]));
        Assert.Throws<InvalidOperationException>(() => s.At(10));
    }

    [Fact(DisplayName = "T-11.12 Stores allocate 0 bytes in steady state")]
    public void Store_ZeroAlloc()
    {
        var s = new EnemyStore();
        Span<int> idx = stackalloc int[64];
        for (int w = 0; w < 5; w++) { var h = s.Add(E(1, 1)); s.Remove(h); s.CopyIndicesByIdAscending(idx); } // aquecimento
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int w = 0; w < 10_000; w++)
        {
            var h = s.Add(E(w, 1));
            s.Get(h).X += 1;
            s.CopyIndicesByIdAscending(idx);
            s.Remove(h);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact(DisplayName = "T-11.10 StripLayout validates, quantizes and exposes PathLength")]
    public void Layout_Validation()
    {
        var l = StripLayout.Create(1000.0004f, 94f, 10f, 980f, new[] { 100.3f, 200f });
        Assert.Equal(Det.Quantize(1000.0004f), l.SpawnX);
        Assert.Equal(Det.Quantize(100.3f), l.SlotXAt(0));
        Assert.Equal(l.SpawnX - 94f, l.PathLength);
        Assert.Equal(2, l.SlotCount);
        Assert.True(l.HasSlot(1)); Assert.False(l.HasSlot(2)); Assert.False(l.HasSlot(-1));

        Assert.False(StripLayout.TryCreate(94f, 94f, 10f, 80f, [], out _, out var e1)); Assert.NotNull(e1);
        Assert.False(StripLayout.TryCreate(1000f, 94f, 10f, 980f, new float[13], out _, out _));
        Assert.False(StripLayout.TryCreate(float.NaN, 94f, 10f, 980f, [], out _, out _));
        Assert.False(StripLayout.TryCreate(1000f, 94f, 10f, 980f, new[] { float.PositiveInfinity }, out _, out _));
        Assert.True(StripLayout.TryCreate(1000f, 94f, 10f, 980f, new float[12], out var l12, out _));
        Assert.Equal(12, l12!.SlotCount);
        Assert.Throws<ArgumentException>(() => StripLayout.Create(50f, 94f, 10f, 40f, []));
    }

    [Fact(DisplayName = "T-11.10 Layout is immutable: source array changes do not leak")]
    public void Layout_CopiesInput()
    {
        var src = new[] { 100f, 200f };
        var l = StripLayout.Create(1000f, 94f, 10f, 980f, src);
        src[0] = 999f;
        Assert.Equal(100f, l.SlotXAt(0));
    }

    [Fact(DisplayName = "T-11.10 Det.Quantize rounds to 1/1024, half to even")]
    public void Quantize()
    {
        Assert.Equal(1f, Det.Quantize(1f));
        Assert.Equal(0.5f / 1024f * 2f, Det.Quantize(1f / 1024f));
        Assert.Equal(0f, Det.Quantize(0.5f / 1024f)); // meio -> par (0)
        Assert.Equal(2f / 1024f, Det.Quantize(1.5f / 1024f));
    }

    [Fact(DisplayName = "T-11.10 ApplyLayout swaps layout between ticks and clamps enemies beyond new SpawnX (ADR-031)")]
    public void ApplyLayout_ClampsAndSwaps()
    {
        var s = new SimState(Layout(1000f));
        var far = s.Enemies.Add(E(1, 990f));
        var mid = s.Enemies.Add(E(2, 400f));
        var tw = s.Towers.Add(T(3, 3));
        Assert.True(s.TowerHasSlot(s.Towers.Get(tw)));

        var shorter = Layout(600f, 2);
        s.ApplyLayout(shorter);
        Assert.Same(shorter, s.Layout);
        Assert.Equal(600f, s.Enemies.Get(far).X);
        Assert.Equal(400f, s.Enemies.Get(mid).X);
        Assert.Equal(2, s.Enemies.Count);              // nada removido
        Assert.False(s.TowerHasSlot(s.Towers.Get(tw))); // SEM SLOT
        Assert.True(float.IsNaN(s.TowerX(s.Towers.Get(tw))));
        Assert.Equal(1, s.Towers.Count);                // torre preservada

        s.ApplyLayout(Layout(1000f, 4));                // slot reaparece
        Assert.True(s.TowerHasSlot(s.Towers.Get(tw)));
        Assert.Equal(380f, s.TowerX(s.Towers.Get(tw)));
        Assert.Equal(600f, s.Enemies.Get(far).X);       // não "desclampa"
    }

    [Fact(DisplayName = "T-11.10 ApplyLayout is rejected during a tick")]
    public void ApplyLayout_NotDuringTick()
    {
        var s = new SimState(Layout());
        var original = s.Layout;
        s.BeginTick();
        Assert.Throws<InvalidOperationException>(() => s.ApplyLayout(Layout(500f)));
        Assert.Same(original, s.Layout);
        Assert.Throws<InvalidOperationException>(s.BeginTick);
        s.EndTick();
        Assert.Equal(1, s.Tick);
        Assert.Throws<InvalidOperationException>(s.EndTick);
        s.ApplyLayout(Layout(500f));
        Assert.Throws<ArgumentNullException>(() => s.ApplyLayout(null!));
    }

    [Fact(DisplayName = "T-11.10 LayoutChanged command carries the layout")]
    public void LayoutChanged_Command()
    {
        var l = Layout();
        IGameCommand c = new LayoutChanged(l);
        Assert.Same(l, ((LayoutChanged)c).Layout);
    }

    [Fact(DisplayName = "T-11.2 CreateInitial uses economy.json and starts at stage 1 wave 1")]
    public void CreateInitial_FromContent()
    {
        var c = ContentLoader.LoadEmbedded();
        var s = SimState.CreateInitial(c, Layout(), Rng.Seed(42, 54));
        Assert.Equal(c.Economy.StartingCredits, s.Credits);
        Assert.Equal(c.Economy.Core.BaseHp, s.BaseHp);
        Assert.Equal(s.BaseHp, s.MaxBaseHp);
        Assert.Equal((1, 1, 1, 0L, 0L), (s.Level, s.Wave, s.Stage, s.Xp, s.Reservas));
        Assert.Equal(0, s.Tick);
        Assert.Equal(1, s.TakeId());
        Assert.Equal(2, s.TakeId());
        Assert.Equal(0, s.Enemies.Count + s.Towers.Count);
    }

    [Fact(DisplayName = "T-27.1 Credits and XP add with saturation")]
    public void Saturating()
    {
        var s = new SimState(Layout()) { Credits = long.MaxValue - 5 };
        s.AddCredits(10);
        Assert.Equal(long.MaxValue, s.Credits);
        s.AddXp(7); s.AddXp(long.MaxValue);
        Assert.Equal(long.MaxValue, s.Xp);
        Assert.Equal(long.MinValue, Sat.Add(long.MinValue + 1, -5));
        Assert.Equal(3, Sat.Add(5, -2));
    }

    [Fact(DisplayName = "T-11.1 FindTowerAtSlot and content id mapping")]
    public void FindSlot_And_Ids()
    {
        var s = new SimState(Layout());
        Assert.True(s.FindTowerAtSlot(1).IsNone);
        var h = s.Towers.Add(T(1, 1));
        Assert.Equal(h, s.FindTowerAtSlot(1));
        var c = ContentLoader.LoadEmbedded();
        foreach (var e in c.Enemies) Assert.True(SimIds.TryEnemyKind(e.Id, out _), e.Id);
        foreach (var t in c.Towers) Assert.True(SimIds.TryTowerType(t.Id, out var ty) && ty != TowerType.None, t.Id);
        Assert.False(SimIds.TryEnemyKind("x", out _));
        Assert.False(SimIds.TryTowerType("x", out _));
    }

    [Fact(DisplayName = "T-11.13 Rng PCG32 matches the reference vector and restores")]
    public void Rng_Pcg32()
    {
        var r = Rng.Seed(42, 54);
        uint[] expected = [0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e];
        foreach (var e in expected) Assert.Equal(e, r.NextUInt());
        var copy = Rng.FromState(r.State, r.Inc);
        Assert.Equal(r.NextUInt(), copy.NextUInt());
    }
}
