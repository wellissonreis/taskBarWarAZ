namespace SideWar.Runtime.Td;

/// <summary>
/// Sinal "presente agora" da caixa-bomba (RF-28.19, TD.14): substitui a leitura global do último input do sistema. O jogador está
/// <b>ausente</b> depois de ativar OUTRO app (mudança de foreground para janela que não é do SideWar) e volta a <b>presente</b> ao
/// interagir com o SideWar (mouse sobre a faixa, o HUD ou um popover, clique). Só um sinal binário no host; não gera crédito.
/// </summary>
public interface ITdPresence
{
    bool IsPresent { get; }
}

/// <summary>
/// Estado de foco da faixa TD (TD.14): ausente ⇒ esmaecer faixa e HUD (se ligado), render a 8 fps e caixa-bomba sem presença.
/// Atualizado na T-Game (hook de foreground e eventos de mouse); leitura segura de qualquer thread.
/// </summary>
public sealed class TdFocusState : ITdPresence
{
    public const int DefaultFadePercent = 50;
    public const int AbsentFps = 8;

    private volatile bool _absent;
    private volatile bool _fadeEnabled = true;
    private int _fadePercent = DefaultFadePercent;

    /// <summary>Ausência ou configuração mudaram (a apresentação reaplica opacidade e fps).</summary>
    public event Action? Changed;

    public bool IsPresent => !_absent;
    public bool Absent => _absent;
    public bool FadeEnabled => _fadeEnabled;

    /// <summary>Esmaecido agora: ausente e a opção ligada.</summary>
    public bool FadeActive => _absent && _fadeEnabled;

    /// <summary>Alfa constante (0 a 255) das janelas: 255 normalmente, o nível configurado enquanto esmaecido.</summary>
    public byte Alpha => FadeActive ? (byte)Math.Clamp(Volatile.Read(ref _fadePercent) * 255 / 100, 0, 255) : (byte)255;

    /// <summary>Opção de Configurações (ligar/desligar e nível de 10 a 90%).</summary>
    public void Configure(bool fadeEnabled, int percent)
    {
        Volatile.Write(ref _fadePercent, Math.Clamp(percent, 0, 100));
        _fadeEnabled = fadeEnabled;
        Changed?.Invoke();
    }

    /// <summary>O foreground passou para uma janela que não é do SideWar (RF-28.19).</summary>
    public void OnForeignActivated() => Set(true);

    /// <summary>Mouse sobre a faixa/HUD/popover, clique ou janela do SideWar ativada.</summary>
    public void OnInteraction() => Set(false);

    private void Set(bool absent)
    {
        if (_absent == absent) return;
        _absent = absent;
        Changed?.Invoke();
    }
}
