using System.Reflection;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class TdSpecialBossTests
{
    private static readonly string[] Ids = ["botnet", "kraken", "golem", "ronin", "trojan", "hydra"];

    [Fact]
    public void SpecialBossesAndAlliesAppearInTheirRegions()
    {
        var content = TdContentLoader.LoadEmbedded();
        for (int i = 0; i < Ids.Length; i++)
        {
            var groups = content.Waves.Stages[i].Waves[^1].Groups;
            Assert.Contains(groups, g => g.Enemy == "chefe_" + Ids[i] && g.Count == 1);
            Assert.Contains(groups, g => g.Enemy == "aliado_" + Ids[i]);
            Assert.Contains(content.Enemies.Bosses, b => b.Id == "chefe_" + Ids[i] && b.Stage == i + 1);
        }
    }

    [Fact]
    public void BossEnragesAtHalfHealthAndMovesSixtyPercentFaster()
    {
        var content = TdContentLoader.LoadEmbedded();
        var match = TdMatchState.Create(content, 1, Rng.Seed(1, 2));
        int index = content.Enemies.Enemies.Length + Array.FindIndex(content.Enemies.Bosses, b => b.Id == "chefe_botnet");
        var enemies = (List<TdEnemy>)typeof(TdMatchState).GetField("_enemies", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(match)!;
        enemies.Add(new TdEnemy { Id = 1, DefIndex = index, IsBoss = true, X = 1000, Speed = 20, Hp = 100, MaxHp = 100 });
        var damage = typeof(TdMatchState).GetMethod("DealDamage", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var move = typeof(TdMatchState).GetMethod("MoveEnemies", BindingFlags.NonPublic | BindingFlags.Instance)!;
        damage.Invoke(match, [0, 49.0]);
        Assert.False(enemies[0].Enraged);
        move.Invoke(match, null);
        double before = 1000 - enemies[0].X;
        damage.Invoke(match, [0, 1.0]);
        Assert.True(enemies[0].Enraged);
        double x = enemies[0].X;
        move.Invoke(match, null);
        Assert.Equal(before * 1.6, x - enemies[0].X, 8);
    }
}
