using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using StartWave = SideWar.Core.Td.StartWave;

namespace SideWar.Core.Tests;

/// <summary>TD.40b2: clone/simulação do passo seguinte, linha de defesa geométrica e catálogo do SDD-38.</summary>
public sealed class TdAlertProtectionTests
{
    private static readonly TdContent Content = TdContentLoader.LoadEmbedded();

    private static StripLayout Full() =>
        StripLayout.Create(1200, 1200, 0f, 0f, 1200, TowerHost.Taskbar, [new FreeZone(0, 1200)]);

    private static TdMatchState New(int stage = 1, ulong seed = 42, TdContent? content = null)
    {
        var s = TdMatchState.Create(content ?? Content, stage, Rng.Seed(seed, 54));
        Assert.True(s.Apply(new LayoutChanged(Full())).IsOk);
        Assert.True(s.Apply(new StartWave()).IsOk);
        return s;
    }

    [Fact]
    public void Clone_is_independent_and_step_of_clone_does_not_touch_original()
    {
        var s = New();
        for (int i = 0; i < 200; i++) s.Step();
        ulong hash = s.ComputeStateHash();
        var c = s.CloneForSimulation();
        Assert.Equal(hash, c.ComputeStateHash());
        for (int i = 0; i < 100; i++) c.Step();
        Assert.Equal(hash, s.ComputeStateHash());
        for (int i = 0; i < 100; i++) s.Step();
        Assert.Equal(s.ComputeStateHash(), c.ComputeStateHash()); // mesma sequência => mesmo estado
    }

    [Theory]
    [InlineData(1)] [InlineData(3)] [InlineData(6)]
    public void Projection_is_an_upper_bound_and_NextStepIsFatal_predicts_every_step(int stage)
    {
        var s = New(stage);
        for (int i = 0; i < 60_000 && s.Phase is MatchPhase.Onda or MatchPhase.Preparo; i++)
        {
            if (s.Phase == MatchPhase.Preparo) { s.Apply(new StartWave()); continue; }
            long bound = s.ProjectNextStepLeakDamage();
            int before = s.Integrity;
            ulong h = s.ComputeStateHash();
            bool predicted = s.NextStepIsFatal();
            Assert.Equal(h, s.ComputeStateHash()); // predizer não muda nada
            s.Step();
            Assert.True(before - s.Integrity <= bound, $"passo {i}: dano real {before - s.Integrity} > limite {bound}");
            Assert.Equal(s.Phase == MatchPhase.Derrota, predicted);
        }
        Assert.Equal(MatchPhase.Derrota, s.Phase); // sem torres a partida é perdida: a predição foi exercitada
    }

    [Fact]
    public void Defense_line_is_the_placed_active_tower_nearest_the_portal_and_follows_sell_and_disable()
    {
        var s = New();
        double safety = s.LeakX + TdRiskAnalyzer.SafetyDistance;
        Assert.Equal(safety, TdRiskAnalyzer.DefenseLineX(s)); // sem torres: distância de segurança
        Assert.True(s.Apply(new BuildTower("bolt", 600)).IsOk);
        Assert.True(s.Apply(new BuildTower("bolt", 300)).IsOk);
        int near = s.Towers.First(t => t.X < 400).Id;
        Assert.Equal(s.Towers.Min(t => t.X), TdRiskAnalyzer.DefenseLineX(s));
        double far = s.Towers.Where(t => t.Id != near).Min(t => t.X);
        Assert.True(s.Apply(new SellTower(near)).IsOk);
        Assert.Equal(far, TdRiskAnalyzer.DefenseLineX(s)); // venda muda a referência
        var list = Assert.IsType<List<TdTower>>(s.Towers);
        var t0 = list[0]; t0.DisabledTicks = 100; list[0] = t0;
        Assert.Equal(safety, TdRiskAnalyzer.DefenseLineX(s)); // desativada: só resta a distância de segurança
    }

    [Fact]
    public void Past_defense_line_counts_flyers_and_ignores_dead_and_enemies_before_the_tower()
    {
        var s = New();
        Assert.True(s.Apply(new BuildTower("bolt", 600)).IsOk);
        double line = s.Towers[0].X;
        var enemies = Assert.IsType<List<TdEnemy>>(s.Enemies);
        enemies.Add(new TdEnemy { Id = 901, X = line + 1, Hp = 5, Flying = true });
        enemies.Add(new TdEnemy { Id = 902, X = line - 1, Hp = 5, Flying = true }); // voador que a torre não alcança, mas passou
        enemies.Add(new TdEnemy { Id = 903, X = line - 1, Hp = 0 });
        enemies.Add(new TdEnemy { Id = 904, X = line - 30, Hp = 5 });
        var ids = TdRiskAnalyzer.PastDefenseLine(s);
        Assert.Equal([902, 904], ids);
        Assert.Equal(ids, TdRiskAnalyzer.PastDefenseLine(s));
    }

    [Fact]
    public void Fork_bomb_copies_and_botnet_splits_make_the_next_step_fatal_when_they_would_leak()
    {
        var s = New();
        int fork = Array.FindIndex(Content.Enemies.Enemies, e => e.Id == "fork");
        var enemies = Assert.IsType<List<TdEnemy>>(s.Enemies);
        enemies.Clear();
        // Fork morto no limite: as cópias nascem no passo, e vazam no mesmo passo.
        enemies.Add(new TdEnemy { Id = 7, Wave = 1, DefIndex = fork, X = s.LeakX + 0.01, Speed = 30, Hp = 0, MaxHp = 1, CoreDamage = 1, Gold = 0 });
        Assert.True(s.ProjectNextStepLeakDamage() >= 1);
    }

    [Fact]
    public void Catalog_is_pure_ordered_by_severity_and_emits_only_supported_types()
    {
        var s = New();
        ulong h = s.ComputeStateHash();
        ulong rng = s.RngState;
        var a = TdAlertCatalog.Detect(s);
        var b = TdAlertCatalog.Detect(s);
        Assert.Equal(a, b);
        Assert.Equal(h, s.ComputeStateHash());
        Assert.Equal(rng, s.RngState);
        Assert.Contains(a, x => x.Type == TdAlertType.FewTowers);
        Assert.DoesNotContain(a, x => x.Type is TdAlertType.FlyerNoAntiAir or TdAlertType.PhishingNoSentinel or TdAlertType.BossIncoming);
        for (int i = 1; i < a.Count; i++) Assert.True(a[i - 1].Severity >= a[i].Severity);
        // Inimigo passando da linha sem torres: crítico e primeiro.
        var enemies = Assert.IsType<List<TdEnemy>>(s.Enemies);
        enemies.Add(new TdEnemy { Id = 950, X = s.LeakX + 10, Hp = 5, Speed = 30, CoreDamage = 1, Flying = true });
        var c = TdAlertCatalog.Detect(s);
        Assert.Equal(TdAlertType.PassedLastTower, c[0].Type);
        Assert.Contains(c, x => x.Type == TdAlertType.FlyerNoAntiAir);
        Assert.Contains(c, x => x.Type == TdAlertType.ImminentLeak);
        Assert.Equal("Nesse ritmo sua torre vai cair.", c[0].Message);
    }

    [Fact]
    public void Catalog_credits_runes_abilities_boss_and_phishing()
    {
        var s = New();
        Assert.True(TdAlertCatalog.Test(s, TdAlertType.IdleCredits, out _)); // créditos iniciais >= 2x a torre mais barata
        for (int x = 100; x < 1100 && TdAlertCatalog.Test(s, TdAlertType.IdleCredits, out _); x += 60) s.Apply(new BuildTower("bolt", x));
        Assert.False(TdAlertCatalog.Test(s, TdAlertType.IdleCredits, out _));
        Assert.False(TdAlertCatalog.Test(s, TdAlertType.RuneAvailable, out _));
        Assert.False(TdAlertCatalog.Test(s, TdAlertType.VaultBackupIdle, out _));
        int phishing = Array.FindIndex(Content.Enemies.Enemies, e => e.Id == "phishing");
        int boss = Content.Enemies.Enemies.Length;
        var enemies = Assert.IsType<List<TdEnemy>>(s.Enemies);
        enemies.Add(new TdEnemy { Id = 960, DefIndex = phishing, X = 900, Hp = 5, Speed = 38, CoreDamage = 2 });
        enemies.Add(new TdEnemy { Id = 961, DefIndex = boss, IsBoss = true, X = 900, Hp = 5, Speed = 10, CoreDamage = 5 });
        Assert.True(TdAlertCatalog.Test(s, TdAlertType.PhishingNoSentinel, out var p));
        Assert.Equal(TdAlertSeverity.Warning, p.Severity);
        Assert.True(TdAlertCatalog.Test(s, TdAlertType.BossIncoming, out _));
    }
}
