using SideWar.Core.Td;
using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Td;

/// <summary>
/// TD.30e: cartão "runas no primeiro dano". Quando o núcleo sofre dano pela primeira vez (vazamento: a integridade cai) o jogador vê, UMA vez por
/// perfil (dica <see cref="RuneTipKey"/> em <c>td.tipsSeen</c>, vale mesmo com o tutorial concluído; "Refazer tutorial" a reativa), um cartão curto no
/// cantinho junto ao botão Runas. Nunca rouba foco nem pausa; fecha sozinho ao abrir a janela de runas ou após <see cref="RuneCardSeconds"/> s de
/// exibição; respeita a boss key e a pausa; espera a vez se outro cartão do cantinho (pergunta, aviso, fim) está na tela.
/// </summary>
public sealed partial class TdUiController
{
    public const string RuneTipKey = "runes-first-damage";
    public const double RuneCardSeconds = 20.0;
    public const string RuneCardText = "Você levou dano. Runas reforçam sua defesa: cada fragmento desperta uma runa vizinha. Veja o ramo que quer seguir e monte sua estratégia.";

    private TdMatchState? _rcMatch;
    private int _rcIntegrity;
    private bool _rcPending, _rcShown;
    private double _rcLeft, _rcClock = -1.0;

    /// <summary>O 1º dano foi visto e o cartão ainda espera a vez (testes).</summary>
    public bool RuneCardPending => _rcPending;

    /// <summary>Cartão em exibição (some durante a pausa e com a boss key; o tempo só corre enquanto visível).</summary>
    public bool RuneCardOpen => _rcShown && !BossHidden && !MatchPaused;

    private bool MatchPaused => _match().Paused || _host.Paused;

    private bool QuizShowing(TdMatchState m) => _quizQ is not null && QuizPhaseOk(m);

    /// <summary>O jogador tem a janela de runas à mão: partida em jogo e HUD aberto (o botão Runas existe).</summary>
    private bool RuneWindowReachable(TdMatchState m) => m.Phase is MatchPhase.Preparo or MatchPhase.Onda && !_host.HudMinimized;

    private bool CanShowRuneCard(TdMatchState m) =>
        !BossHidden && !MatchPaused && !ModalOpen && !BuildActive && !RuneWindowOpen && !QuizShowing(m) && !NoticeOpen && !EndCardOpen
        && (Mode == TdUiMode.None || QuickTowersOpen) && RuneWindowReachable(m);

    private void SyncRuneCard(TdMatchState m)
    {
        if (_onb is null) return;
        double now = _now();
        double dt = _rcClock < 0.0 ? 0.0 : Math.Clamp(now - _rcClock, 0.0, QuizMaxDeltaSeconds);
        _rcClock = now;
        if (!ReferenceEquals(_rcMatch, m)) { _rcMatch = m; _rcIntegrity = m.Integrity; }
        else
        {
            if (m.Integrity < _rcIntegrity && !_rcShown && !_onb.TipSeen(RuneTipKey)) _rcPending = true;
            _rcIntegrity = m.Integrity;
        }
        if (_rcPending && !_rcShown && CanShowRuneCard(m))
        {
            _rcShown = true;
            _rcPending = false;
            _rcLeft = RuneCardSeconds;
            _onb.MarkTipSeen(RuneTipKey); // contado ao exibir: nunca repete
            Changed?.Invoke();
            return;
        }
        if (!_rcShown) return;
        if (ModalOpen || RuneWindowOpen || !RuneWindowReachable(m)) { DismissRuneCard(); return; }
        if (BossHidden || MatchPaused || QuizShowing(m) || NoticeOpen) return; // esperando: o tempo congela
        _rcLeft -= dt;
        if (_rcLeft <= 0.0) DismissRuneCard();
    }

    private void DismissRuneCard()
    {
        if (!_rcShown) return;
        _rcShown = false;
        Changed?.Invoke();
    }

    /// <summary>Nova partida (ou "Refazer tutorial"): zera o cartão; <paramref name="clearSeen"/> também esquece a dica para ela voltar a valer.</summary>
    private void ResetRuneCard(bool clearSeen)
    {
        _rcShown = _rcPending = false;
        _rcMatch = null;
        if (clearSeen) _onb?.ClearTip(RuneTipKey);
    }

    private TdPopoverView BuildRuneCard()
    {
        var s = new Stack(QuizWidth);
        QuizText(s, RuneCardText, QuizOptionFont, icon: TdIcon.Star);
        float half = (s.Width - 2 * Pad - 4f) / 2f;
        s.Items.Add(new TdUiItem(new DipRect(Pad, s.Y, half, 36f), TdUiKind.Button, "Abrir runas", TdUiAction.RuneTutorialOpen, Tone: TdTone.Accent, FontSize: QuizOptionFont));
        s.Items.Add(new TdUiItem(new DipRect(Pad + half + 4f, s.Y, half, 36f), TdUiKind.Button, "Entendi", TdUiAction.RuneTutorialDismiss, FontSize: QuizOptionFont));
        s.Y += 40f;
        var items = new List<TdUiItem>(s.Items.Count + 1)
        {
            new(new DipRect(s.Width - HeaderH - 2, 2, HeaderH, HeaderH - 4), TdUiKind.Button, "X", TdUiAction.RuneTutorialDismiss),
        };
        items.AddRange(s.Items);
        return new TdPopoverView("Runas", [.. items], s.Width, s.Y + Pad + TdPopoverView.ArrowH, 0f, ArrowX: Math.Clamp(RuneButtonArrowX(s.Width), 14f, s.Width - 14f));
    }
}
