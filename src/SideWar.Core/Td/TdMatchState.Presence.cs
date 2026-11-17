using SideWar.Core.Simulation;

namespace SideWar.Core.Td;

// Presença do usuário (sinal do log). Mantida após a remoção da caixa-bomba (TD.30a): continua sendo um sinal registrado e hasheado.
public sealed partial class TdMatchState
{
    private bool _present;

    /// <summary>Último sinal "presente agora" registrado no log (<see cref="SetPresence"/>).</summary>
    public bool Present => _present;

    private CommandResult DoSetPresence(SetPresence cmd)
    {
        if (_present == cmd.Present) return CommandResult.Ok;
        _present = cmd.Present;
        return CommandResult.Ok;
    }
}
