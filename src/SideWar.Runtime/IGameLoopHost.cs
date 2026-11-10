using SideWar.Core;

namespace SideWar.Runtime;

/// <summary>Called exclusively on T-Game. Deadlines use the supplied monotonic clock.</summary>
public interface IGameLoopHost
{
    long? NextDeadline { get; }
    void OnWake(IClock clock);
    void OnCommand(IGameCommand command);
    void Shutdown();
}
