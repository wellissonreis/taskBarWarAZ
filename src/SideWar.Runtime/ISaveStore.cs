namespace SideWar.Runtime;

public interface ISaveStore
{
    ValueTask SaveSessionAsync(ReadOnlyMemory<byte> utf8Json, CancellationToken ct);
}
