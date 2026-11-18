using SideWar.Core.Content.Td;

namespace SideWar.Core.Td;

public sealed record TdAdvice(string Trait, string TowerId);

public static class TdAdvisor
{
    public static TdAdvice? Suggest(TdContent content, IReadOnlyList<TdLeakStat> leaks, int unlockedStage)
    {
        var damage = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var leak in leaks)
        {
            var enemy = content.Enemies.Enemies.FirstOrDefault(e => !leak.IsBoss && e.Id == leak.EnemyId);
            var boss = content.Enemies.Bosses.FirstOrDefault(e => leak.IsBoss && e.Id == leak.EnemyId);
            foreach (var trait in (enemy?.Traits ?? boss?.Traits ?? []).Distinct(StringComparer.Ordinal))
                damage[trait] = damage.GetValueOrDefault(trait) + Math.Max(0, leak.Damage);
        }
        var traitWinner = TdContentValidator.Traits.Where(t => damage.GetValueOrDefault(t) > 0)
            .OrderByDescending(t => damage[t]).FirstOrDefault();
        if (traitWinner is null) return null;
        var tower = content.Towers.Where(t => t.UnlockStage <= unlockedStage && t.Strong.Contains(traitWinner))
            .OrderBy(t => t.Cost).ThenBy(t => t.Id, StringComparer.Ordinal).FirstOrDefault();
        if (tower is null)
        {
            var top = leaks.Where(l => l.Damage > 0).OrderByDescending(l => l.Damage).FirstOrDefault();
            var counters = top.IsBoss ? content.Enemies.Bosses.FirstOrDefault(e => e.Id == top.EnemyId)?.Counters
                : content.Enemies.Enemies.FirstOrDefault(e => e.Id == top.EnemyId)?.Counters;
            var ids = (counters ?? []).Select(c => c.Split('.')[0]).ToHashSet(StringComparer.Ordinal);
            tower = content.Towers.Take(5).Where(t => t.UnlockStage <= unlockedStage && ids.Contains(t.Id))
                .OrderBy(t => t.Cost).ThenBy(t => t.Id, StringComparer.Ordinal).FirstOrDefault();
        }
        return tower is null ? null : new TdAdvice(traitWinner, tower.Id);
    }
}
