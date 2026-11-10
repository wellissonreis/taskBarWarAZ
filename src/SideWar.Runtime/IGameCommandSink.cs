using SideWar.Core;

namespace SideWar.Runtime;

public interface IGameCommandSink
{
    void Post(IGameCommand command);
}
