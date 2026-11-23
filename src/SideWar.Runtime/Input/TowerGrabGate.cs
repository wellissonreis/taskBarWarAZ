namespace SideWar.Runtime.Input;

/// <summary>
/// Estado puro do "pegar torre" na faixa TD (fusão por arrasto): a faixa é click-through (<c>WS_EX_TRANSPARENT</c>); só enquanto o cursor
/// está SOBRE uma torre colocada (ou com o botão pressionado/capturado) ela deixa de ser, para receber o botão. Fora das torres o clique
/// continua passando para o app de baixo. A plataforma alimenta <see cref="SetOver"/> (poll do cursor e WM_MOUSEMOVE) e consulta
/// <see cref="Hot"/> para decidir o estilo da janela; nunca mexe em foco nem em modo.
/// </summary>
public sealed class TowerGrabGate
{
    /// <summary>Cursor sobre uma torre colocada que pode ser arrastada.</summary>
    public bool Over { get; private set; }

    /// <summary>Botão pressionado sobre uma torre, com o mouse capturado até soltar.</summary>
    public bool Captured { get; private set; }

    /// <summary>A janela precisa receber o botão (não pode estar click-through).</summary>
    public bool Hot => Over || Captured;

    /// <summary>Atualiza o "sobre torre"; durante a captura o valor é ignorado (continua quente até soltar). Devolve true se <see cref="Hot"/> mudou.</summary>
    public bool SetOver(bool over)
    {
        bool before = Hot;
        if (!Captured) Over = over;
        return Hot != before;
    }

    /// <summary>Botão pressionado: só começa a captura se a janela estava quente (o pressionar chegou porque a torre estava sob o cursor).</summary>
    public bool BeginCapture()
    {
        if (!Over || Captured) return false;
        Captured = true;
        return true;
    }

    /// <summary>Botão solto ou captura perdida: termina a captura e reavalia pelo ponto final. Devolve true se <see cref="Hot"/> mudou.</summary>
    public bool EndCapture(bool overAtEnd)
    {
        bool before = Hot;
        Captured = false;
        Over = overAtEnd;
        return Hot != before;
    }

    /// <summary>Cena escondida ou modo TD desligado: volta a ser totalmente click-through.</summary>
    public void Reset()
    {
        Over = false;
        Captured = false;
    }

    /// <summary>Estilo de click-through efetivo: o do modo, a menos que uma torre esteja sendo pegada.</summary>
    public bool WantsTransparent(bool modeTransparent) => modeTransparent && !Hot;

    /// <summary>
    /// A janela deve receber a mensagem de mouse? No modo Interativo sempre; no Passivo só quente. O movimento sem captura NÃO é repassado
    /// ao jogo (passar o mouse sobre a torre não conta como presença).
    /// </summary>
    public bool ReceivesMouse(bool interactive) => interactive || Hot;
}
