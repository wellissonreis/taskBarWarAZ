using SideWar.Core.Content;
using SideWar.Core.Simulation;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class SimDamageTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);

    private static StripLayout Layout() =>
        StripLayout.Create(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 380f });

    private static SimState NewState() => SimState.CreateInitial(Content, Layout(), Rng.Seed(42, 54));

    private static Handle AddTower(SimState s, TowerType type, int slot, int level = 1) =>
        s.Towers.Add(new Tower { Id = s.TakeId(), Type = type, SlotIndex = slot, Level = level });

    private static Handle AddEnemy(SimState s, float x, double hp = 1000, short armor = 0, EnemyKind kind = EnemyKind.Grunt,
        EnemyFlags flags = EnemyFlags.None, float speed = 45f) =>
        s.Enemies.Add(new Enemy { Id = s.TakeId(), Kind = kind, X = x, Hp = hp, MaxHp = hp, Armor = armor, Speed = speed, SpawnSpeed = 45f, Flags = flags });

    private static List<SimEvent> Drain(SimState s, ref long cursor)
    {
        var l = new List<SimEvent>();
        while (s.Events.TryRead(ref cursor, out var e)) l.Add(e);
        return l;
    }

    [Fact(DisplayName = "T-11.1 Raio: o 3º acerto real causa dano ×2 e o contador zera; emite SuperHit")]
    public void Bolt_SuperHit()
    {
        var s = NewState();
        AddTower(s, TowerType.Bolt, 0);
        var e = AddEnemy(s, 250f);
        long c = 0;
        for (int i = 0; i < 43; i++) s.Step(Ctx);   // acertos nos ticks 0, 21 e 42
        var ev = Drain(s, ref c);
        var hits = ev.Where(x => x.Type == SimEventType.Hit).ToList();
        Assert.Equal(3, hits.Count);
        Assert.Equal(18.0, hits[0].A);
        Assert.Equal(18.0, hits[1].A);
        Assert.Equal(36.0, hits[2].A);
        Assert.Single(ev, x => x.Type == SimEventType.SuperHit);
        Assert.Equal(1000 - 72, s.Enemies.Get(e).Hp);
        Assert.Equal(0, s.Towers.At(0).Hits);
        Assert.All(hits, h => { Assert.Equal(250f, h.X); Assert.Equal((int)TowerType.Bolt, h.B); });
    }

    [Fact(DisplayName = "T-11.1 Contador de super hit só conta acertos reais (sem alvo não conta)")]
    public void Bolt_Counter_Only_Real_Hits()
    {
        var s = NewState();
        var t = AddTower(s, TowerType.Bolt, 0);
        for (int i = 0; i < 50; i++) s.Step(Ctx);
        Assert.Equal(0, s.Towers.Get(t).Hits);
    }

    [Fact(DisplayName = "T-11.2 Gelo: lentidão ×0,65 cumulativa até o piso de 45% da SpawnSpeed; marca Chilled")]
    public void Frost_Slow_And_Floor()
    {
        var s = NewState();
        AddTower(s, TowerType.Frost, 0);
        var e = AddEnemy(s, 250f);
        s.Step(Ctx);
        Assert.Equal(45f * 0.65f, s.Enemies.Get(e).Speed);
        Assert.True((s.Enemies.Get(e).Flags & EnemyFlags.Chilled) != 0);
        for (int i = 0; i < 27 * 3; i++) s.Step(Ctx);   // mais acertos (cadência 27 ticks)
        Assert.Equal(45f * 0.45f, s.Enemies.Get(e).Speed);
    }

    [Fact(DisplayName = "T-11.2 Chilled +25% em Raio, 0% no Gelo; piso após floor")]
    public void Chilled_Bonus()
    {
        var s = NewState();
        AddTower(s, TowerType.Bolt, 0);
        var e = AddEnemy(s, 250f, flags: EnemyFlags.Chilled);
        s.Step(Ctx);
        Assert.Equal(1000 - 22, s.Enemies.Get(e).Hp);      // floor(18 × 1,25 = 22,5)
        var s2 = NewState();
        AddTower(s2, TowerType.Frost, 0);
        var e2 = AddEnemy(s2, 250f, flags: EnemyFlags.Chilled);
        s2.Step(Ctx);
        Assert.Equal(1000 - 6, s2.Enemies.Get(e2).Hp);     // Gelo não recebe +25%
    }

    [Fact(DisplayName = "T-11.3 Armadura: floor após a subtração e dano mínimo 1")]
    public void Armor_And_Minimum()
    {
        var s = NewState();
        AddTower(s, TowerType.Bolt, 0);
        var a = AddEnemy(s, 250f, armor: 3, flags: EnemyFlags.Chilled);   // floor(22,5 − 3 = 19,5) = 19
        s.Step(Ctx);
        Assert.Equal(1000 - 19, s.Enemies.Get(a).Hp);
        var s2 = NewState();
        AddTower(s2, TowerType.Bolt, 0);
        var b = AddEnemy(s2, 250f, armor: 100);
        s2.Step(Ctx);
        Assert.Equal(1000 - 1, s2.Enemies.Get(b).Hp);
    }

    [Fact(DisplayName = "T-11.3 Canhão: área de 36 DIP ×0,6 (borda inclusiva), armadura por alvo, metade em runner, um Splash")]
    public void Cannon_Splash()
    {
        var s = NewState();
        AddTower(s, TowerType.Cannon, 0);
        var main = AddEnemy(s, 250f);
        var edge = AddEnemy(s, 286f);                    // exatamente 36
        var outside = AddEnemy(s, 286.5f);
        var armored = AddEnemy(s, 270f, armor: 4);        // 20 de distância: 36 − 4 = 32
        var runner = AddEnemy(s, 260f, kind: EnemyKind.Runner);   // floor(60×0,6×0,5)=18
        var dead = AddEnemy(s, 255f, hp: 0);              // não recebe
        long c = 0;
        s.Step(Ctx);
        Assert.Equal(1000 - 60, s.Enemies.Get(main).Hp);
        Assert.Equal(1000 - 36, s.Enemies.Get(edge).Hp);
        Assert.Equal(1000, s.Enemies.Get(outside).Hp);
        Assert.Equal(1000 - 32, s.Enemies.Get(armored).Hp);
        Assert.Equal(1000 - 18, s.Enemies.Get(runner).Hp);
        Assert.Equal(0, s.Enemies.Get(dead).Hp);
        Assert.Equal(TowerType.Cannon, s.Enemies.Get(edge).LastHit);
        var splash = Drain(s, ref c).Where(x => x.Type == SimEventType.Splash).ToList();
        Assert.Single(splash);
        Assert.Equal(250f, splash[0].X);
        Assert.Equal(3, splash[0].B);
    }

    [Fact(DisplayName = "T-11.3 Canhão contra runner principal: ×0,5")]
    public void Cannon_Vs_Runner()
    {
        var s = NewState();
        AddTower(s, TowerType.Cannon, 0);
        var r = AddEnemy(s, 250f, kind: EnemyKind.Runner);
        s.Step(Ctx);
        Assert.Equal(1000 - 30, s.Enemies.Get(r).Hp);
    }

    [Fact(DisplayName = "T-11.5 Pico ×3 multiplica o dano antes da armadura")]
    public void Peak_Factor()
    {
        var s = NewState();
        s.PeakDamageFactor = 3.0;
        AddTower(s, TowerType.Bolt, 0);
        var e = AddEnemy(s, 250f, armor: 2);
        s.Step(Ctx);
        Assert.Equal(1000 - 52, s.Enemies.Get(e).Hp);     // 18×3 − 2
    }

    [Fact(DisplayName = "T-27.4 Scale Set: 14·1,12^(L−1)·(1+0,15·Raios com slot); SEM SLOT não conta")]
    public void ScaleSet_Bonus()
    {
        var s = NewState();
        AddTower(s, TowerType.ScaleSet, 0, level: 10);
        AddTower(s, TowerType.Bolt, 3);
        AddTower(s, TowerType.Bolt, 3);
        AddTower(s, TowerType.Bolt, 2);
        AddTower(s, TowerType.Bolt, 9);                   // SEM SLOT (layout tem 4)
        Assert.Equal(3, s.CountActiveBolts());
        var e = AddEnemy(s, 100f, hp: 1e6);               // só o Scale Set alcança (dist 100 ≤ 144 vs 280)
        s.Step(Ctx);
        double expected = Math.Floor(Ctx.BaseDamage(TowerType.ScaleSet, 10) * (1.0 + 0.15 * 3));
        Assert.Equal(1e6 - expected, s.Enemies.Get(e).Hp);
        Assert.Equal(Math.Floor(14 * Math.Pow(1.12, 9) * 1.45), expected);
    }

    [Fact(DisplayName = "T-11.5 Ordem de ADR-031: super hit × runner × chilled × pico − armadura, floor, mínimo 1")]
    public void Order_Of_Factors()
    {
        var s = NewState();
        s.PeakDamageFactor = 3.0;
        var t = AddTower(s, TowerType.Bolt, 0, level: 3);
        s.Towers.At(t.Index).Hits = 2;                     // este é o 3º acerto
        var e = AddEnemy(s, 250f, armor: 5, flags: EnemyFlags.Chilled);
        s.Step(Ctx);
        double d = Math.Floor(Ctx.BaseDamage(TowerType.Bolt, 3) * 2 * 1.25 * 3 - 5);
        Assert.Equal(1000 - d, s.Enemies.Get(e).Hp);
    }

    [Fact(DisplayName = "Eventos: buffer circular de 1024 com cursor por leitor; atraso perde os mais antigos")]
    public void Event_Buffer_Cursor()
    {
        var b = new SimEventBuffer();
        for (int i = 0; i < 1030; i++) b.Add(new SimEvent(SimEventType.Hit, i, 0, i, 0));
        long cur = 0;
        Assert.True(b.TryRead(ref cur, out var first));
        Assert.Equal(6f, first.X);                         // 1030 − 1024
        int n = 1;
        while (b.TryRead(ref cur, out _)) n++;
        Assert.Equal(1024, n);
        Assert.False(b.TryRead(ref cur, out _));
        b.Add(new SimEvent(SimEventType.Splash, 1, 0, 0, 0));
        Assert.True(b.TryRead(ref cur, out var e));
        Assert.Equal(SimEventType.Splash, e.Type);
    }

    [Fact(DisplayName = "RNF-11.2 Dano e eventos: 0 B alocados em 2000 ticks e determinismo de HP")]
    public void No_Allocation_And_Determinism()
    {
        double Run(out long bytes)
        {
            var s = NewState();
            AddTower(s, TowerType.Bolt, 0);
            AddTower(s, TowerType.Frost, 1);
            AddTower(s, TowerType.Cannon, 2);
            AddTower(s, TowerType.ScaleSet, 3);
            for (int i = 0; i < 10; i++) AddEnemy(s, 150f + 10 * i, hp: 1e12);
            s.Step(Ctx);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++) s.Step(Ctx);
            bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            double hp = 0;
            for (int k = 0; k < s.Enemies.Capacity; k++) if (s.Enemies.IsAlive(k)) hp += s.Enemies.At(k).Hp;
            return hp;
        }
        double a = Run(out long b1);
        double b = Run(out _);
        Assert.Equal(0, b1);
        Assert.Equal(a, b);
        Assert.True(a < 1e13);
    }
}
