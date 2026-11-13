using SideWar.Core.Content;
using SideWar.Core.Simulation;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class SimTickTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);

    private static StripLayout Layout(float spawn = 1000f, int slots = 4) =>
        TestLayouts.FromCenters(spawn, 94f, 10f, spawn - 20f, Enumerable.Range(0, slots).Select(i => 200f + 60f * i).ToArray());

    private static SimState NewState(int slots = 4) => SimState.CreateInitial(Content, Layout(slots: slots), Rng.Seed(42, 54));

    private static Handle AddTower(SimState s, TowerType type, int slot, int level = 1, Targeting mode = Targeting.First, bool placed = true) =>
        s.Towers.Add(new Tower { Id = s.TakeId(), Type = type, Anchor = TowerAnchor.Start, OffsetDip = TestLayouts.X(slot), Placed = placed, Level = level, Mode = mode });

    private static Handle AddEnemy(SimState s, float x, double hp = 1000, short armor = 0, EnemyKind kind = EnemyKind.Grunt, float speed = 0f) =>
        s.Enemies.Add(new Enemy { Id = s.TakeId(), Kind = kind, X = x, Hp = hp, MaxHp = hp, Armor = armor, Speed = speed, SpawnSpeed = 45 });

    [Fact(DisplayName = "T-11.4 Mira 1D: First/Last/Strong/Weak, alcance e desempates")]
    public void Targeting_Modes()
    {
        var s = NewState();
        var a = AddEnemy(s, 250, hp: 50);   // id 1
        var b = AddEnemy(s, 300, hp: 90);   // id 2
        var c = AddEnemy(s, 150, hp: 90);   // id 3
        var far = AddEnemy(s, 400, hp: 5);  // fora do alcance 144 de x=200
        float x = 200f, r = 144f;
        Assert.Equal(c.Index, s.PickTarget(Targeting.First, x, r));
        Assert.Equal(b.Index, s.PickTarget(Targeting.Last, x, r));
        Assert.Equal(c.Index, s.PickTarget(Targeting.Strong, x, r));   // 90 empata (b,c): menor X
        Assert.Equal(a.Index, s.PickTarget(Targeting.Weak, x, r));
        Assert.NotEqual(far.Index, s.PickTarget(Targeting.Weak, x, r));
        Assert.Equal(-1, s.PickTarget(Targeting.First, 700f, r));
    }

    [Fact(DisplayName = "T-11.4 Mira: alcance inclusivo, Hp<=0 e X<=LeakX não são candidatos, empate total pelo menor Id")]
    public void Targeting_Edges()
    {
        var s = NewState();
        AddEnemy(s, 344f);                 // exatamente no alcance (200+144)
        Assert.NotEqual(-1, s.PickTarget(Targeting.First, 200f, 144f));
        Assert.Equal(-1, s.PickTarget(Targeting.First, 200f, 143.9f));
        var s2 = NewState();
        AddEnemy(s2, 100f, hp: 0);         // morto
        AddEnemy(s2, 94f);                 // em LeakX
        Assert.Equal(-1, s2.PickTarget(Targeting.First, 100f, 144f));
        var s3 = NewState();
        var first = AddEnemy(s3, 150f);    // mesmo X, mesmo Hp: menor Id
        AddEnemy(s3, 150f);
        foreach (var m in new[] { Targeting.First, Targeting.Last, Targeting.Strong, Targeting.Weak })
            Assert.Equal(first.Index, s3.PickTarget(m, 200f, 144f));
    }

    [Fact(DisplayName = "T-11.5 Acerto instantâneo: dano no tick do disparo, cooldown da cadência e LastHit")]
    public void Instant_Hit()
    {
        var s = NewState();
        s.StartWave(Ctx);
        s.WaveClock = -1000f; // sem spawn automático neste teste
        AddTower(s, TowerType.Bolt, 0);
        var e = AddEnemy(s, 250, hp: 1000);
        s.Step(Ctx);
        ref var en = ref s.Enemies.Get(e);
        Assert.Equal(1000 - 18, en.Hp);
        Assert.Equal(TowerType.Bolt, en.LastHit);
        Assert.Equal(0.7f, s.Towers.At(0).Cooldown);
        Assert.Equal(1, s.Tick);
    }

    [Fact(DisplayName = "T-11.5 Cadência: intervalo medido em ticks entre acertos")]
    public void Cadence_Ticks()
    {
        var s = NewState();
        s.StartWave(Ctx);
        s.WaveClock = -1000f;
        AddTower(s, TowerType.Bolt, 0);
        var e = AddEnemy(s, 250, hp: 1e9);
        var hitTicks = new List<long>();
        double last = 1e9;
        for (int i = 0; i < 100; i++)
        {
            s.Step(Ctx);
            double hp = s.Enemies.Get(e).Hp;
            if (hp < last) hitTicks.Add(s.Tick);
            last = hp;
        }
        Assert.Equal(1, hitTicks[0]);
        // Medido: 0,7 s com dt=1/30 em float = 21 ticks.
        int gap = (int)(hitTicks[1] - hitTicks[0]);
        Assert.Equal(21, gap);
        Assert.All(hitTicks.Zip(hitTicks.Skip(1)), p => Assert.Equal(gap, p.Second - p.First));
    }

    [Fact(DisplayName = "T-11.5 Alvo morto no mesmo tick não é candidato; torres em ordem de Id")]
    public void Dead_Target_Not_Candidate_And_Id_Order()
    {
        var s = NewState();
        s.StartWave(Ctx);
        s.WaveClock = -1000f;
        var t1 = AddTower(s, TowerType.Bolt, 1);   // id 1 (slot 1, x=260) — atira primeiro
        var t2 = AddTower(s, TowerType.Bolt, 0);   // id 2
        var weak = AddEnemy(s, 250, hp: 10);       // morre no 1º acerto (18)
        var other = AddEnemy(s, 300, hp: 1000);
        s.Step(Ctx);
        Assert.False(s.Enemies.IsValid(weak));      // morreu e foi removido pelo DeathSystem (T3.8)
        Assert.Equal(1000 - 18, s.Enemies.Get(other).Hp); // t2 não mirou o morto: acertou o outro
        Assert.Equal(0.7f, s.Towers.Get(t1).Cooldown);
        Assert.Equal(0.7f, s.Towers.Get(t2).Cooldown);
    }

    [Fact(DisplayName = "T-11.10 Torre SEM POSIÇÃO não atira nem esfria; volta sozinha quando a zona reaparece")]
    public void No_Slot_Tower_Idle()
    {
        var s = NewState(slots: 4);
        s.StartWave(Ctx);
        s.WaveClock = -1000f;
        var t = AddTower(s, TowerType.Bolt, 5, placed: false);
        s.Towers.Get(t).Cooldown = 0.5f;
        var e = AddEnemy(s, 520, hp: 100);
        s.Step(Ctx);
        Assert.Equal(100, s.Enemies.Get(e).Hp);
        Assert.Equal(0.5f, s.Towers.Get(t).Cooldown);
        s.ApplyLayout(Layout(slots: 8));
        for (int i = 0; i < 30; i++) s.Step(Ctx);
        Assert.True(s.Enemies.Get(e).Hp < 100 || !s.Enemies.IsValid(e));
    }

    [Fact(DisplayName = "T-11.3 Armadura reduz o acerto base; dano mínimo 1")]
    public void Armor_And_Min_Damage()
    {
        var s = NewState();
        s.StartWave(Ctx);
        s.WaveClock = -1000f;
        AddTower(s, TowerType.Bolt, 0);
        var armored = AddEnemy(s, 250, hp: 100, armor: 10);
        s.Step(Ctx);
        Assert.Equal(100 - 8, s.Enemies.Get(armored).Hp);
        var s2 = NewState();
        s2.StartWave(Ctx);
        s2.WaveClock = -1000f;
        AddTower(s2, TowerType.Bolt, 0);
        var heavy = AddEnemy(s2, 250, hp: 100, armor: 500);
        s2.Step(Ctx);
        Assert.Equal(99, s2.Enemies.Get(heavy).Hp);
    }

    [Fact(DisplayName = "T-11.5 Dano por nível vem da tabela dano₁ × 1,12^(L-1)")]
    public void Damage_Table()
    {
        Assert.Equal(18.0, Ctx.BaseDamage(TowerType.Bolt, 1));
        double d = 18.0; for (int i = 1; i < 10; i++) d *= 1.12;
        Assert.Equal(d, Ctx.BaseDamage(TowerType.Bolt, 10));
        Assert.Equal(Ctx.BaseDamage(TowerType.Bolt, 999), Ctx.BaseDamage(TowerType.Bolt, 5000));
    }

    [Fact(DisplayName = "T-11.6 Spawn automático: 1º inimigo da onda 1 no 1º tick, em SpawnX, com HP e velocidade do conteúdo")]
    public void Spawn_First_Enemy()
    {
        var s = NewState();
        s.Step(Ctx);
        Assert.NotNull(s.ActivePlan);
        Assert.Equal(1, s.Enemies.Count);
        ref var e = ref s.Enemies.At(0);
        var spawn = s.ActivePlan!.Spawns[0];
        Assert.Equal(1000f - e.Speed * SimContext.Dt, e.X);   // nasce em SpawnX e já marcha no mesmo tick (passo 6)
        Assert.Equal(spawn.Hp, e.Hp);
        Assert.Equal(spawn.Hp, e.MaxHp);
        Assert.Equal((float)spawn.Enemy.Speed, e.Speed);
        Assert.Equal(e.Speed, e.SpawnSpeed);
        Assert.Equal(1, e.Id);
        Assert.Equal(2, s.NextId);
        Assert.Equal(1, s.NextSpawnIndex);
    }

    [Fact(DisplayName = "T-11.6 Spawns respeitam o intervalo da agenda")]
    public void Spawn_Follows_Schedule()
    {
        var s = NewState();
        s.StartWave(Ctx);
        var plan = s.ActivePlan!;
        for (int i = 0; i < 300; i++)
        {
            s.Step(Ctx);
            int expected = 0;
            while (expected < plan.Spawns.Length && plan.Spawns[expected].Time <= s.WaveClock) expected++;
            Assert.Equal(expected, s.NextSpawnIndex);
        }
    }

    [Fact(DisplayName = "T-11.6 Limite de 44 inimigos: spawn adiado e retomado")]
    public void Field_Limit_Defers_And_Resumes()
    {
        var s = NewState();
        s.Wave = 72; // 30 inimigos por onda
        s.StartWave(Ctx);
        var handles = new List<Handle>();
        for (int i = 0; i < 44; i++) handles.Add(AddEnemy(s, 600f + i));
        for (int i = 0; i < 120; i++) s.Step(Ctx);
        Assert.Equal(44, s.Enemies.Count);
        Assert.Equal(0, s.NextSpawnIndex);
        s.Enemies.Remove(handles[0]);
        s.Enemies.Remove(handles[1]);
        s.Step(Ctx);
        Assert.Equal(44, s.Enemies.Count);
        Assert.Equal(2, s.NextSpawnIndex);
    }

    [Fact(DisplayName = "T-11.10 Spawn usa o SpawnX do layout vigente")]
    public void Spawn_Uses_Current_Layout()
    {
        var s = NewState();
        s.ApplyLayout(Layout(spawn: 700f));
        s.Step(Ctx);
        Assert.Equal(700f - s.Enemies.At(0).Speed * SimContext.Dt, s.Enemies.At(0).X);
    }

    [Fact(DisplayName = "Tick: incrementa Tick, termina mesmo se houver exceção e não aceita reentrada")]
    public void Tick_Lifecycle()
    {
        var s = NewState();
        s.Step(Ctx);
        s.Step(Ctx);
        Assert.Equal(2, s.Tick);
        Assert.False(s.InTick);
        Assert.Throws<ArgumentNullException>(() => s.Step(null!));
        Assert.Equal(2, s.Tick);
    }

    [Fact(DisplayName = "T-11.12 Determinismo: duas execuções idênticas dão o mesmo estado")]
    public void Deterministic_Runs()
    {
        static (long, int, double) Run()
        {
            var s = NewState();
            AddTower(s, TowerType.Bolt, 0, level: 3);
            AddTower(s, TowerType.Cannon, 1);
            for (int i = 0; i < 600; i++)
            {
                s.Step(Ctx);
                for (int k = 0; k < s.Enemies.Capacity; k++)
                    if (s.Enemies.IsAlive(k)) { ref var e = ref s.Enemies.At(k); e.X -= e.Speed * SimContext.Dt; }
            }
            double hp = 0;
            for (int k = 0; k < s.Enemies.Capacity; k++) if (s.Enemies.IsAlive(k)) hp += s.Enemies.At(k).Hp;
            return (s.Tick, s.NextId, hp);
        }
        Assert.Equal(Run(), Run());
    }

    [Fact(DisplayName = "T-11.12 Tick em regime não aloca (44 inimigos, 12 torres)")]
    public void Tick_Zero_Alloc()
    {
        var s = NewState(slots: 12);
        s.Wave = 72;
        s.StartWave(Ctx);
        for (int i = 0; i < 12; i++) AddTower(s, (TowerType)(1 + i % 3), i, level: 50);
        for (int i = 0; i < 44; i++) AddEnemy(s, 300f + i * 10, hp: 1e18);
        for (int i = 0; i < 50; i++) s.Step(Ctx);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) s.Step(Ctx);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
