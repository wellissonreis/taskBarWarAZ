using System.Reflection;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Core.Tests;

/// <summary>
/// BAL-1: todo inimigo de uma fase tem pelo menos uma torre de DANO com vantagem, liberada na patente mínima da fase, com efeito real em
/// combate no Extremo. A Sentinel é complemento: revela os camuflados para que o dano alcance antes da boca da base.
/// </summary>
public sealed class TdWeaknessMatrixTests
{
    private static readonly TdContent Content = TdContentLoader.LoadEmbedded();

    private static IEnumerable<(string Id, string[] Traits)> Enemies =>
        Content.Enemies.Enemies.Select(e => (e.Id, e.Traits)).Concat(Content.Enemies.Bosses.Select(b => (b.Id, b.Traits)));

    /// <summary>Pares (fase, inimigo) presentes nas ondas.</summary>
    public static IEnumerable<object[]> Present() =>
        from stage in Content.Waves.Stages
        from enemy in stage.Waves.SelectMany(w => w.Groups).Select(g => g.Enemy).Distinct()
        select new object[] { stage.Stage, enemy };

    private static IEnumerable<TdTowerDef> Unlocked(int stage) =>
        Content.Towers.Where(t => t.MinRank <= Content.Stages.Stages[stage - 1].MinRank);

    private static IEnumerable<TdTowerDef> Advantage(int stage, string[] traits) =>
        Unlocked(stage).Where(t => t.Damage > 0 && t.Strong.Intersect(traits).Any() && !t.Weak.Intersect(traits).Any()
            && (!traits.Contains("flying") || t.Targets == "both"));

    /// <summary>Dano (HP + escudo) que a torre causa em 20 s num inimigo parado, longe da base, no Extremo.</summary>
    private static double Probe(string enemyId, bool revealed, params string[] towers)
    {
        var content = Content with
        {
            Stages = Content.Stages with { Stages = Content.Stages.Stages.Select(s => s with { StartingCredits = 100_000, Waves = 1 }).ToArray() },
            Waves = new TdWavesDef(Content.Waves.Stages.Select(s => new TdStageWavesDef(s.Stage, [new TdWaveDef(1, false, 1, [new TdWaveGroupDef(enemyId, 1, 0, 1)])])).ToArray()),
        };
        var m = TdMatchState.Create(content, 6, Rng.Seed(1, 54), null,
            TdMatchOptions.Classic with { Difficulty = TdDifficulty.Extreme, InitialReputation = TdRank.Threshold(TdRankLevel.Arquiteto) });
        Assert.True(m.Apply(new LayoutChanged(TdSanityBot.TypicalLayout())).IsOk);
        int x = 870; // a 130 unidades do alvo: fora do alcance de dreno dos parasitas e dentro do das torres
        foreach (var tower in towers)
        {
            while (!m.Apply(new BuildTower(tower, x)).IsOk) x += 11;
            x += 55;
        }
        typeof(TdMatchState).GetMethod("BeginWave", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(m, [1]);
        var list = (List<TdEnemy>)m.Enemies;
        for (int i = 0; i < 100 && list.Count == 0; i++) m.Step();
        var enemy = list[0];
        enemy.X = 1000; enemy.Speed = 0; enemy.Hp = 1_000_000; enemy.MaxHp = 1; // MaxHp abaixo do HP: a regeneração não mascara o dano
        if (revealed) enemy.SentinelMarkTicks = 100_000; // camuflado já descoberto: mede só a vantagem de dano
        list[0] = enemy;
        double before = enemy.Hp + enemy.ShieldHp;
        for (int i = 0; i < 600; i++) m.Step();
        var after = list.First(e => e.Id == enemy.Id);
        return before - after.Hp - after.ShieldHp;
    }

    [Theory]
    [MemberData(nameof(Present))]
    public void Every_present_enemy_has_an_effective_unlocked_answer(int stage, string enemyId)
    {
        var traits = Enemies.Single(e => e.Id == enemyId).Traits;
        var answers = Advantage(stage, traits).ToArray();
        Assert.True(answers.Length > 0, $"fase {stage}: {enemyId} sem resposta");
        Assert.True(answers.Any(a => Probe(enemyId, traits.Contains("stealth"), a.Id) > 0), $"fase {stage}: {answers[0].Id} não fere {enemyId}");
    }

    [Theory]
    [InlineData("phishing")]
    [InlineData("dnstunnel")]
    public void Sentinel_is_what_lets_towers_hit_camouflaged_enemies(string enemyId)
    {
        double alone = Probe(enemyId, false, "frost");
        double marked = Probe(enemyId, false, "frost", "sentinel");
        Assert.Equal(0, alone);
        Assert.True(marked > 0, "marcado pela Sentinel, o camuflado passa a receber dano");
    }

    [Fact]
    public void Every_stage_one_enemy_is_answered_by_a_starting_tower()
    {
        foreach (var enemyId in Content.Waves.Stages[0].Waves.SelectMany(w => w.Groups).Select(g => g.Enemy).Distinct())
            Assert.Contains(Advantage(1, Enemies.Single(e => e.Id == enemyId).Traits), t => t.MinRank == 0);
        Assert.True(Advantage(1, ["swarm"]).Count() >= 2, "Enxame: pelo menos duas torres iniciais");
    }
}
