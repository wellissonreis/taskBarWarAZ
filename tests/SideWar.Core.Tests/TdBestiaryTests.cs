using System.Reflection;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class TdBestiaryTests
{
    [Theory]
    [InlineData("chefe_botnet")]
    [InlineData("ransomware")]
    [InlineData("botnet")]
    public void Boss_enraged_discovery_is_emitted_once_at_the_transition(string id)
    {
        var content = TdContentLoader.LoadEmbedded();
        var match = TdMatchState.Create(content, 1, Rng.Seed(7, 54));
        int index = content.Enemies.Enemies.Length + Array.FindIndex(content.Enemies.Bosses, b => b.Id == id);
        var enemies = (List<TdEnemy>)typeof(TdMatchState).GetField("_enemies", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(match)!;
        enemies.Add(new TdEnemy { Id = 1, DefIndex = index, IsBoss = true, Hp = 100, MaxHp = 100 });
        var damage = typeof(TdMatchState).GetMethod("DealDamage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        damage.Invoke(match, [0, 49d]);
        Assert.DoesNotContain(match.Events, e => e.Type == TdEventType.EnemyEncountered);
        damage.Invoke(match, [0, 1d]);
        damage.Invoke(match, [0, 1d]);
        Assert.Single(match.Events, e => e.Type == TdEventType.EnemyEncountered && e.A == index && e.B == 2);
    }

    [Fact]
    public void Spawning_records_normal_and_mutant_separately_including_allies()
    {
        var content = TdContentLoader.LoadEmbedded();
        var match = TdMatchState.Create(content, 1, Rng.Seed(7, 54));
        var make = typeof(TdMatchState).GetMethod("MakeEnemy", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int swarm = Array.FindIndex(content.Enemies.Enemies, e => e.Id == "swarm");
        int ally = Array.FindIndex(content.Enemies.Enemies, e => e.Id == "aliado_botnet");
        make.Invoke(match, [swarm, 1, 100d, (ushort)1]);
        make.Invoke(match, [ally, 1, 100d, (ushort)0]);
        var events = match.Events.Where(e => e.Type == TdEventType.EnemyEncountered).ToArray();
        Assert.Contains(events, e => e.A == swarm && e.B == 0);
        Assert.Contains(events, e => e.A == swarm && e.B == 1);
        Assert.Contains(events, e => e.A == ally && e.B == 0);
        Assert.DoesNotContain(events, e => e.A == ally && e.B != 0);
    }
}
