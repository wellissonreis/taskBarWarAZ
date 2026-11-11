using SideWar.Core.Content;
using SideWar.Core.Simulation;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class SimProgressTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);

    private static StripLayout Layout() =>
        StripLayout.Create(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 380f });

    private static SimState NewState() => SimState.CreateInitial(Content, Layout(), Rng.Seed(42, 54));

    private static Handle AddEnemy(SimState s, float x, double hp = 1000, EnemyKind kind = EnemyKind.Grunt, float speed = 0f) =>
        s.Enemies.Add(new Enemy { Id = s.TakeId(), Kind = kind, X = x, Hp = hp, MaxHp = hp, Speed = speed, SpawnSpeed = speed });

    private static List<SimEvent> Drain(SimState s, ref long cursor)
    {
        var l = new List<SimEvent>();
        while (s.Events.TryRead(ref cursor, out var e)) l.Add(e);
        return l;
    }

    /// <summary>Estado numa onda sem spawns pendentes (campo controlado pelo teste).</summary>
    private static SimState Quiet(int stage = 1, int wave = 1)
    {
        var s = NewState();
        s.Stage = stage;
        s.Wave = wave;
        s.StartWave(Ctx);
        s.WaveClock = -100000f;   // nenhum spawn durante o teste
        return s;
    }

    [Fact(DisplayName = "RF-27.8 Vazamento: dano ao núcleo por tipo, inimigo removido, sem ouro; evento Leak")]
    public void Leak_Damages_Core()
    {
        var s = Quiet();
        AddEnemy(s, 94.5f, kind: EnemyKind.Tank, speed: 30f);   // 94,5 − 1 → cruza LeakX
        AddEnemy(s, 94.5f, kind: EnemyKind.Boss, speed: 25f);
        long c = s.Events.Written;
        s.Step(Ctx);
        Assert.Equal(0, s.Enemies.Count);
        Assert.Equal(100 - 2 - 10, s.BaseHp);
        Assert.Equal(100, s.Credits);
        Assert.Equal(2, Drain(s, ref c).Count(e => e.Type == SimEventType.Leak));
    }

    [Fact(DisplayName = "RF-27.6 Kill: ouro e XP por tipo e onda vigente, carry fracionário, evento Kill, sem Hit extra")]
    public void Kill_Rewards()
    {
        var s = Quiet(wave: 3);
        AddEnemy(s, 500f, hp: 0, kind: EnemyKind.Grunt);
        long c = s.Events.Written;
        s.Step(Ctx);
        double gold = 9 * 1.07 * 1.07;   // 10,3041
        Assert.Equal(100 + 10, s.Credits);
        Assert.Equal(gold - 10, s.Carry, 9);
        double xp = 5 * 1.04 * 1.04;     // 5,408
        Assert.Equal(5, s.Xp);
        Assert.Equal(xp - 5, s.XpCarry, 9);
        var kill = Assert.Single(Drain(s, ref c), e => e.Type == SimEventType.Kill);
        Assert.Equal((int)EnemyKind.Grunt, kill.B);
    }

    [Fact(DisplayName = "RF-27.1 Multiplicadores: créditos × (1 + 0,01 × Reservas) × presença; XP só presença")]
    public void Kill_Multipliers()
    {
        var s = Quiet();
        s.Reservas = 100;
        s.PresenceMultiplier = 0.25;
        AddEnemy(s, 500f, hp: 0, kind: EnemyKind.Tank);   // 20 ouro, 12 XP
        s.Step(Ctx);
        Assert.Equal(100 + 10, s.Credits);                // 20 × 2 × 0,25
        Assert.Equal(3, s.Xp);                            // 12 × 0,25
    }

    [Fact(DisplayName = "CA-27.6 XP e nível: enquanto xp ≥ nível × 45; HP do núcleo = 100 + 5 × (nível − 1); LevelUp")]
    public void Level_Up_Loop()
    {
        var s = Quiet();
        s.Xp = 45 + 90 + 44;          // nível 1 → 2 → 3 e sobra 44
        long c = s.Events.Written;
        s.Step(Ctx);
        Assert.Equal(3, s.Level);
        Assert.Equal(44, s.Xp);
        Assert.Equal(110, s.MaxBaseHp);
        Assert.Equal(2, Drain(s, ref c).Count(e => e.Type == SimEventType.LevelUp));
        s.Xp = 3 * 45 - 1;
        s.Step(Ctx);
        Assert.Equal(3, s.Level);     // fronteira: 134 não basta
        s.Xp++;
        s.Step(Ctx);
        Assert.Equal(4, s.Level);
        Assert.Equal(0, s.Xp);
        Assert.Equal(115, s.MaxBaseHp);
        Assert.Equal(115, SimState.CoreHpForLevel(Content.Economy, 4));
    }

    [Theory(DisplayName = "CA-27.5 Queda: recuo de 5 ondas, nunca abaixo de (último chefe + 1) nem de 1; créditos intactos")]
    [InlineData(10, 0, 5)]
    [InlineData(4, 0, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(23, 20, 21)]
    [InlineData(30, 20, 25)]
    public void Fall_Rewinds(int wave, int lastBoss, int expected)
    {
        var s = Quiet(wave: wave);
        s.RestoreStageProgress(1, lastBoss, 0);
        s.Credits = 777;
        AddEnemy(s, 500f);
        AddEnemy(s, 94.5f, kind: EnemyKind.Boss, speed: 25f);
        s.BaseHp = 5;                       // o chefe vaza (10) e zera o núcleo
        long c = s.Events.Written;
        s.Step(Ctx);
        Assert.Equal(expected, s.Wave);
        Assert.Equal(1, s.Falls);
        Assert.Equal(1, s.StageFalls);
        Assert.Equal(s.MaxBaseHp, s.BaseHp);
        Assert.Equal(777, s.Credits);
        Assert.Equal(0, s.Enemies.Count);
        Assert.Equal(expected, s.ActivePlan!.Wave);
        Assert.Equal(0, s.NextSpawnIndex);
        var ev = Drain(s, ref c);
        var fall = Assert.Single(ev, e => e.Type == SimEventType.Fall);
        Assert.Equal(expected, fall.A);
    }

    [Fact(DisplayName = "RF-27.7 CheckEnd: Rest só conta com agenda esgotada e campo vazio; próxima onda após 1,4 s com núcleo restaurado")]
    public void CheckEnd_Advances_Wave()
    {
        var s = Quiet();
        s.RestoreNextSpawn(s.ActivePlan!.Spawns.Length);
        var e = AddEnemy(s, 500f);
        s.BaseHp = 50;
        for (int i = 0; i < 10; i++) s.Step(Ctx);
        Assert.Equal(0f, s.Rest);                 // há inimigo em campo
        s.Enemies.Remove(e);
        int ticks = 0;
        while (s.Wave == 1 && ticks++ < 100) s.Step(Ctx);
        Assert.Equal(2, s.Wave);
        Assert.InRange(ticks, 42, 43);            // 1,4 s × 30
        Assert.Equal(s.MaxBaseHp, s.BaseHp);
        Assert.Equal(0f, s.Rest);
        Assert.Equal(0, s.NextSpawnIndex);
        Assert.Equal(2, s.ActivePlan!.Wave);
    }

    [Fact(DisplayName = "RF-27.8 Chefe vencido: onda de chefe concluída registra o limite do recuo")]
    public void Boss_Wave_Recorded()
    {
        var s = Quiet(wave: 20);
        Assert.True(s.ActivePlan!.IsBoss);
        s.RestoreNextSpawn(s.ActivePlan.Spawns.Length);
        for (int i = 0; i < 60; i++) s.Step(Ctx);
        Assert.Equal(21, s.Wave);
        Assert.Equal(20, s.LastBossWaveBeaten(1));
    }

    [Theory(DisplayName = "CA-27.7 Estrelas: 1, 2 e 3 nas fronteiras (com e sem queda)")]
    [InlineData(0, 3, 0, 1)]
    [InlineData(1, 3, 0, 1)]
    [InlineData(2, 3, 0, 2)]
    [InlineData(2, 3, 1, 2)]
    [InlineData(3, 3, 1, 2)]
    [InlineData(3, 3, 0, 3)]
    [InlineData(0, 0, 0, 1)]
    public void Stars_Boundaries(int correct, int total, int falls, int expected)
        => Assert.Equal(expected, SimState.ComputeStars(correct, total, falls));

    [Fact(DisplayName = "CA-27.6 Conclusão da fase: mantém torres e créditos, onda 1, desbloqueia a seguinte; estrelas nunca diminuem")]
    public void Stage_Completion()
    {
        var s = Quiet(stage: 1, wave: 60);
        s.Towers.Add(new Tower { Id = s.TakeId(), Type = TowerType.Bolt, SlotIndex = 0, Level = 1 });
        s.Credits = 5000;
        s.BossQuestionsCorrect = 3;           // fase 1: 60 ondas → 3 chefes
        s.RestoreNextSpawn(s.ActivePlan!.Spawns.Length);
        long c = s.Events.Written;
        for (int i = 0; i < 60 && s.Stage == 1; i++) s.Step(Ctx);
        Assert.Equal(2, s.Stage);
        Assert.Equal(1, s.Wave);
        Assert.Equal(2, s.UnlockedStage);
        Assert.Equal(1, s.Towers.Count);
        Assert.Equal(5000, s.Credits);
        Assert.Equal(3, s.StarsOf(1));
        Assert.Equal(0, s.StageFalls);
        Assert.Equal(0, s.BossQuestionsCorrect);
        Assert.Equal(1, s.ActivePlan!.Wave);
        Assert.Contains(Drain(s, ref c), e => e.Type == SimEventType.StageCleared && e.A == 1 && e.B == 3);
        s.RestoreStageProgress(1, 0, 3);
        // nova conclusão com menos estrelas não reduz
        s.Stage = 1; s.Wave = 60; s.StartWave(Ctx); s.WaveClock = -100000f;
        s.RestoreNextSpawn(s.ActivePlan!.Spawns.Length);
        for (int i = 0; i < 60 && s.Stage == 1; i++) s.Step(Ctx);
        Assert.Equal(3, s.StarsOf(1));
    }

    [Fact(DisplayName = "CA-27.6 Scale Set desbloqueia ao entrar na fase 9; fase 10 concluída fica em loop na última onda")]
    public void Scale_Set_And_Last_Stage_Loop()
    {
        var s = Quiet(stage: 8, wave: 100);
        Assert.False(s.IsTowerUnlocked(Ctx, TowerType.ScaleSet));
        s.RestoreNextSpawn(s.ActivePlan!.Spawns.Length);
        for (int i = 0; i < 60 && s.Stage == 8; i++) s.Step(Ctx);
        Assert.Equal(9, s.Stage);
        Assert.True(s.IsTowerUnlocked(Ctx, TowerType.ScaleSet));

        var t = Quiet(stage: 10, wave: 100);
        t.RestoreNextSpawn(t.ActivePlan!.Spawns.Length);
        for (int i = 0; i < 60; i++) t.Step(Ctx);
        Assert.Equal(10, t.Stage);
        Assert.Equal(100, t.Wave);
        Assert.Equal(1, t.UnlockedStage);   // concluir a fase 10 não desbloqueia nada
        Assert.Equal(1, t.StarsOf(10));
        Assert.Equal(100, t.ActivePlan!.Wave);   // onda 100 recomeçou (farm)
    }

    [Fact(DisplayName = "CA-11.2 Hash: campos de T3.8 mudam o StateHash; restore da onda reproduz o hash")]
    public void Hash_Covers_Progress()
    {
        ulong h0 = Quiet().ComputeStateHash();
        Assert.Equal(h0, Quiet().ComputeStateHash());
        foreach (var mutate in new Action<SimState>[]
        {
            s => s.UnlockedStage = 2, s => s.StageFalls = 1, s => s.StageWaveMax = 3, s => s.BossQuestionsCorrect = 1,
            s => s.PresenceMultiplier = 0.25, s => s.XpCarry = 0.5, s => s.RestoreStageProgress(1, 20, 0),
            s => s.RestoreStageProgress(2, 0, 2),
        })
        {
            var s = Quiet();
            mutate(s);
            Assert.NotEqual(h0, s.ComputeStateHash());
        }

        var a = Quiet(wave: 7);
        for (int i = 0; i < 120; i++) a.Step(Ctx);
        var b = Quiet(wave: 7);
        b.WaveClock = a.WaveClock; b.Rest = a.Rest;
        b.RestoreNextSpawn(a.NextSpawnIndex);
        Assert.Equal(a.NextSpawnIndex, b.NextSpawnIndex);
    }

    [Fact(DisplayName = "RNF-11.2 Passos 5 a 9 não alocam em regime (sem trocas de onda)")]
    public void Progress_Steps_Zero_Alloc()
    {
        var s = Quiet();
        for (int i = 0; i < 30; i++) AddEnemy(s, 900f, hp: 1e18);
        s.Step(Ctx);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) s.Step(Ctx);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact(DisplayName = "CA-11.1 Determinismo: partida de 3000 ticks com torres tem o mesmo hash em 2 execuções")]
    public void Full_Match_Deterministic()
    {
        ulong Run()
        {
            var s = NewState();
            s.Credits = 1_000_000;
            s.Apply(Ctx, new BuildTower(TowerType.Bolt, 0));
            s.Apply(Ctx, new BuildTower(TowerType.Cannon, 1));
            for (int i = 0; i < 3000; i++) s.Step(Ctx);
            Assert.True(s.Wave >= 1);
            return s.ComputeStateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
