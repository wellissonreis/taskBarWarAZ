namespace SideWar.Runtime.Td;

/// <summary>
/// TD.28b: atalho global de minimizar tudo (boss key). Esconde faixa, chip, painel, cartões e modal de uma vez; a partida CONTINUA rodando
/// (a <c>AppStateMachine</c> segue visível: nada pausa), o jogador conta como ausente (caixa-bomba sem presença) e o render fica parado.
/// O mesmo atalho, o item "Mostrar SideWar" da bandeja ou uma 2ª instância trazem tudo de volta. Estado só em memória: o App sempre abre visível.
/// Só na T-Game.
/// </summary>
public sealed class TdBossKey
{
    private readonly Action<bool> _ui;
    private readonly Action<bool> _scene;
    private readonly TdFocusState _focus;
    private readonly Func<bool> _appVisible;
    private readonly Action _drawFrame;

    /// <param name="ui">Fecha/reabre a UI TD (controlador, janelas, render parado).</param>
    /// <param name="scene">Esconde (false) ou mostra (true) a cena/faixa.</param>
    /// <param name="focus">Estado de presença: escondido = ausente.</param>
    /// <param name="appVisible">A aplicação está visível (se não, a cena já está escondida por outro motivo e não é tocada).</param>
    /// <param name="drawFrame">Um frame completo antes de mostrar a cena (sem conteúdo velho).</param>
    public TdBossKey(Action<bool> ui, Action<bool> scene, TdFocusState focus, Func<bool> appVisible, Action drawFrame)
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _focus = focus ?? throw new ArgumentNullException(nameof(focus));
        _appVisible = appVisible ?? throw new ArgumentNullException(nameof(appVisible));
        _drawFrame = drawFrame ?? throw new ArgumentNullException(nameof(drawFrame));
    }

    public bool Hidden { get; private set; }

    /// <summary>Alterna; devolve o novo estado (verdadeiro = escondido).</summary>
    public bool Toggle()
    {
        Hidden = !Hidden;
        _ui(Hidden);
        if (Hidden)
        {
            _focus.OnForeignActivated();
            if (_appVisible()) _scene(false);
        }
        else if (_appVisible())
        {
            _drawFrame();
            _scene(true);
            _focus.OnInteraction();
        }
        return Hidden;
    }

    /// <summary>Traz tudo de volta se estiver escondido (bandeja "Mostrar SideWar", 2ª instância).</summary>
    public void Show()
    {
        if (Hidden) Toggle();
    }
}
