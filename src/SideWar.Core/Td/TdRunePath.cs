using SideWar.Core.Content.Td;

namespace SideWar.Core.Td;

/// <summary>Menor custo de fragmentos até uma runa não despertada do tipo escolhido.</summary>
public static class TdRunePath
{
    public static IReadOnlyList<string> Find(IReadOnlyList<TdRuneNodeDef> nodes, IReadOnlyCollection<string> frontier,
        Func<string, bool> awakened, Func<string, int> cost, string type)
    {
        var byId = nodes.ToDictionary(n => n.Id);
        var slots = nodes.ToDictionary(n => (n.Ring, n.Sector));
        var best = new Dictionary<string, (long Cost, int Count)>();
        var previous = new Dictionary<string, string?>();
        var queue = new PriorityQueue<string, (long Cost, int Count)>();
        foreach (var id in frontier)
        {
            if (!byId.ContainsKey(id) || awakened(id)) continue;
            var score = ((long)cost(id), 1);
            best[id] = score; previous[id] = null; queue.Enqueue(id, score);
        }
        while (queue.TryDequeue(out var id, out var score))
        {
            if (best[id] != score) continue;
            var node = byId[id];
            if (node.Type == type)
            {
                var path = new List<string>();
                for (string? at = id; at is not null; at = previous[at]) path.Add(at);
                path.Reverse(); return path;
            }
            foreach (var slot in new[] { (node.Ring, (node.Sector + 1) % 16), (node.Ring, (node.Sector + 15) % 16), (node.Ring - 1, node.Sector), (node.Ring + 1, node.Sector) })
            {
                if (!slots.TryGetValue(slot, out var next) || awakened(next.Id)) continue;
                var candidate = (score.Cost + cost(next.Id), score.Count + 1);
                if (best.TryGetValue(next.Id, out var old) && old.CompareTo(candidate) <= 0) continue;
                best[next.Id] = candidate; previous[next.Id] = id; queue.Enqueue(next.Id, candidate);
            }
        }
        return Array.Empty<string>();
    }
}
