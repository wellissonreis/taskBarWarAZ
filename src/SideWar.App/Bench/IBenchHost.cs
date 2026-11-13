using SideWar.Platform.Windows.Threading;
using SideWar.Runtime;

namespace SideWar.App.Bench;

/// <summary>Host da T-Game de um cenário do <c>--bench</c> (T-Game, SDD-16 RF-16.2): o <see cref="BenchRunner"/> o liga e amostra os contadores.</summary>
internal interface IBenchHost : IGameLoopHost, IDisposable
{
    long TaskbarQueries { get; }
    long TaskbarUpdates { get; }
    void Bind(GameThread owner);
    void Setup();
}
