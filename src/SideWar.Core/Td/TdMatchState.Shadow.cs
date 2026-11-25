using SideWar.Core.Simulation;

namespace SideWar.Core.Td;

/// <summary>Modo sombra: farmar sem risco. Constantes documentadas e testadas.</summary>
public static class TdShadow
{
    /// <summary>Fração (%) das recompensas em sombra: fragmentos de runa, selos e créditos de abate/onda/Cofre (sem risco, rende menos).</summary>
    public const int RewardPercent = 35;

    /// <summary>Aplica a fração das recompensas a uma quantidade (arredonda para baixo).</summary>
    public static long Scale(long amount) => Math.Max(0, amount) * RewardPercent / 100;
}

public sealed partial class TdMatchState
{
    private bool _shadow;
    private int _shadowRequest = -1; // -1 nenhum, 0 sair, 1 entrar (pendente até a fronteira entre ondas)
    private long _shadowRemainder;
    private long _shadowCreditRemainder;

    /// <summary>Créditos ganhos jogando (abate, onda, Cofre). Em sombra só <see cref="TdShadow.RewardPercent"/>%; o resto fracionário acumula (determinístico).</summary>
    private long Income(long amount)
    {
        if (!_shadow || amount <= 0) return amount;
        _shadowCreditRemainder += amount * TdShadow.RewardPercent;
        long paid = _shadowCreditRemainder / 100;
        _shadowCreditRemainder %= 100;
        return paid;
    }

    /// <summary>Em sombra: XP de reputação congelado (sem ganho, perda, rebaixamento ou multiplicador) e recompensas permanentes reduzidas.</summary>
    public bool Shadow => _shadow;
    /// <summary>Pedido pendente para a próxima fronteira entre ondas: true = entrar, false = sair, null = nenhum.</summary>
    public bool? ShadowPending => _shadowRequest < 0 ? null : _shadowRequest == 1;
    /// <summary>Fragmentos de runa e ondas ganhos em sombra nesta partida (fora do hash; alimentam o resumo da ausência).</summary>
    public long ShadowFragments { get; private set; }
    public int ShadowWaves { get; private set; }

    private CommandResult DoSetShadow(SetShadow cmd)
    {
        if (cmd.On == _shadow && _shadowRequest < 0) return CommandResult.Ok;
        _shadowRequest = cmd.On == _shadow ? -1 : cmd.On ? 1 : 0;
        if (Phase != MatchPhase.Onda) { ApplyShadowRequest(); if (_shadow && Phase == MatchPhase.Preparo && !_countdownActive) RestartCountdown(); }
        return CommandResult.Ok;
    }

    private void ApplyShadowRequest()
    {
        if (_shadowRequest < 0) return;
        bool on = _shadowRequest == 1;
        _shadowRequest = -1;
        if (on == _shadow) return;
        _shadow = on;
        _streak = 0; // entrar ou sair: a sequência recomeça do zero
        _shadowRemainder = 0;
        _shadowCreditRemainder = 0;
        // A contagem automática só existia por causa da sombra (auto-início Desligado): ao sair não pode ficar uma contagem congelada na tela.
        if (!on && AutoStart == AutoStartMode.Off && Phase == MatchPhase.Preparo) { _countdownActive = false; _countdownTicks = 0; }
        Emit(TdEventType.ShadowChanged, on ? 1 : 0, 0);
    }
}
