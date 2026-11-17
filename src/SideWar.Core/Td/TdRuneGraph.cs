using SideWar.Core.Content.Td;

namespace SideWar.Core.Td;

/// <summary>Shortest unowned path, in stable content order, over undirected rune edges.</summary>
public sealed class TdRuneGraph(TdRunesDef runes)
{
    /// <summary>Excludes owned nodes; includes the target. Unknown/unreachable or owned targets return an empty path.</summary>
    public IReadOnlyList<string> PathTo(string target, IEnumerable<string> owned)
    {
        var have = owned.ToHashSet(StringComparer.Ordinal);
        have.Add(runes.CenterId);
        if (have.Contains(target) || !runes.Nodes.Any(n => n.Id == target)) return [];
        var parents = new Dictionary<string, string?>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        foreach (var n in runes.Nodes.Where(n => have.Contains(n.Id)))
        { parents[n.Id] = null; queue.Enqueue(n.Id); }
        while (queue.TryDequeue(out var id))
        {
            var from = runes.Nodes.First(n => n.Id == id);
            foreach (var n in runes.Nodes)
            {
                if (parents.ContainsKey(n.Id) || !(from.Links.Contains(n.Id, StringComparer.Ordinal) || n.Links.Contains(id, StringComparer.Ordinal))) continue;
                parents[n.Id] = id;
                if (n.Id == target)
                {
                    var path = new List<string>();
                    string? cursor = target;
                    while (cursor is not null && !have.Contains(cursor)) { path.Add(cursor); cursor = parents[cursor]; }
                    path.Reverse();
                    return path;
                }
                queue.Enqueue(n.Id);
            }
        }
        return [];
    }
}
