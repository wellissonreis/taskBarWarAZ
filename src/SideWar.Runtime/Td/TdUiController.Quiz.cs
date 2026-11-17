using SideWar.Core.Content.Td;
using SideWar.Core.Td;
using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Td;

/// <summary>
/// Pergunta-bônus do Preparo (TD.17, SDD-28 RF-28.23, decisão do usuário de 2026-11-17): UMA afirmação Azure V/F de 1 linha (≤ 60 caracteres)
/// com 2 botões grandes e timer visível (<c>rules.bonusQuestion.timerSeconds</c>). Acerto ⇒ <see cref="AnswerBonus"/> com bônus; erro, ignorar
/// ou expirar ⇒ sem penalidade e mostra a resposta certa. Nunca trava o jogo nem impede Iniciar onda (é só um popover). Estado de UI: o
/// sorteio fica fora da simulação; só o <see cref="AnswerBonus"/> (id + resultado) entra no log.
/// </summary>
public sealed partial class TdUiController
{
    public const double QuizResultSeconds = 2.5, QuizWrongSeconds = 3.5;
    public const string QuizExampleId = "q01";

    private enum QuizOutcome : byte { None, Correct, Wrong, Expired }

    private readonly TdQuestionPicker? _quiz;
    private TdQuestion? _quizQ;
    private double _quizStart, _quizResultUntil;
    private QuizOutcome _quizOut;
    private long _quizPaid;
    private int _quizWave = -1;
    private bool _quizExampleDone;
    private double _prepSince;
    private long _phaseKey = -1;

    private bool QuizEnabled => _quiz is not null && _onb is { BonusQuestions: true };

    /// <summary>Pergunta-bônus na tela (aguardando resposta ou mostrando o resultado).</summary>
    public bool QuizActive => _quizQ is not null;

    /// <summary>Afirmação em exibição (testes/goldens).</summary>
    public TdQuestion? QuizQuestion => _quizQ;

    /// <summary>Chip do HUD pulsando: o jogador precisa do HUD e ele está recolhido (reforço do tutorial e dicas de 2–3 palavras).</summary>
    public bool Pulsing => _host.HudMinimized && (_match().CrateActive || (_onb is not null && (HudNeeded(_match()) || ComputeStep(_match()) == TdTutorialStep.HudOpen)));

    private static int QuizNumber(string id)
    {
        int n = 0;
        foreach (char ch in id) if (ch is >= '0' and <= '9') n = n * 10 + (ch - '0');
        return n;
    }

    private void ClearQuiz()
    {
        if (_quizQ is null && _quizOut == QuizOutcome.None) return;
        _quizQ = null;
        _quizOut = QuizOutcome.None;
        Changed?.Invoke();
    }

    private void SyncQuiz(TdMatchState m)
    {
        if (m.Phase != MatchPhase.Preparo) { ClearQuiz(); return; }
        double now = _now();
        if (_quizQ is null)
        {
            if (!QuizEnabled || m.WaveNumber < 1 || m.BonusAnswered || m.NextWaveNumber == 0 || _quizWave == m.NextWaveNumber || Mode != TdUiMode.None) return;
            _quizWave = m.NextWaveNumber;
            _quizQ = _quiz!.Pick(TutorialOn(m) && !_quizExampleDone ? QuizExampleId : null);
            _quizOut = QuizOutcome.None;
            _quizStart = now;
            Changed?.Invoke();
            return;
        }
        if (_quizOut == QuizOutcome.None)
        {
            if (m.BonusAnswered) { ClearQuiz(); return; }
            if (now - _quizStart >= m.Content.Stages.Rules.BonusQuestion.TimerSeconds)
            {
                _quizOut = QuizOutcome.Expired;
                _quizResultUntil = now + QuizWrongSeconds;
                _quizExampleDone = true;
                Changed?.Invoke();
            }
        }
        else if (now >= _quizResultUntil) ClearQuiz();
    }

    private void AnswerQuiz(bool answer)
    {
        var m = _match();
        if (_quizQ is null || _quizOut != QuizOutcome.None || m.Phase != MatchPhase.Preparo) return;
        bool ok = answer == _quizQ.Answer;
        _quizPaid = ok ? m.BonusCreditsFor(m.NextWaveNumber) : 0;
        _send(new AnswerBonus(QuizNumber(_quizQ.Id), ok));
        _quizOut = ok ? QuizOutcome.Correct : QuizOutcome.Wrong;
        _quizResultUntil = _now() + (ok ? QuizResultSeconds : QuizWrongSeconds);
        _quizExampleDone = true;
        Changed?.Invoke();
    }

    private void CloseQuiz()
    {
        _quizExampleDone = true;
        _quizQ = null;
        _quizOut = QuizOutcome.None;
        Changed?.Invoke();
    }

    private bool HudNeeded(TdMatchState m)
    {
        if (!_host.HudMinimized) return false;
        if (_quizQ is not null && m.Phase == MatchPhase.Preparo) return true;
        if (m.Phase != MatchPhase.Preparo) return false;
        if (m.Towers.Count == 0)
        {
            long min = long.MaxValue;
            foreach (var d in m.Content.Towers) if (m.StageId >= d.UnlockStage && d.Cost < min) min = d.Cost;
            return m.Credits >= min;
        }
        return _now() - _prepSince >= HudHintSeconds;
    }

    private bool PulseOn => ((long)(_now() * 2.0) & 1L) == 0L;

    private void TrackPhase(TdMatchState m)
    {
        long key = (long)m.Phase * 1000 + m.WaveNumber;
        if (key == _phaseKey) return;
        _phaseKey = key;
        _prepSince = _now();
    }

    private TdPopoverView BuildQuiz(TdMatchState m)
    {
        var q = _quizQ!;
        var rules = m.Content.Stages.Rules.BonusQuestion;
        bool tutorial = TutorialOn(m) && _quizOut == QuizOutcome.None;
        var s = new Stack(260f);
        if (tutorial) s.Add(TdUiKind.Label, "Exemplo: clique em V ou F", 14f, tone: TdTone.Accent, icon: TdIcon.Question);
        string answerText = q.Answer ? "VERDADEIRO" : "FALSO";
        switch (_quizOut)
        {
            case QuizOutcome.Correct:
                s.Add(TdUiKind.Label, $"Acertou! +{_quizPaid} créditos", 18f, tone: TdTone.Good, icon: TdIcon.Check);
                s.Add(TdUiKind.Label, q.Explanation, 14f, tone: TdTone.Dim);
                break;
            case QuizOutcome.Wrong:
            case QuizOutcome.Expired:
                s.Add(TdUiKind.Label, (_quizOut == QuizOutcome.Expired ? "Tempo! " : "") + $"Era {answerText}.", 18f, tone: TdTone.Bad,
                    icon: _quizOut == QuizOutcome.Expired ? TdIcon.Clock : TdIcon.Cross);
                s.Add(TdUiKind.Label, q.Explanation, 14f, tone: TdTone.Dim);
                break;
            default:
            {
                s.Add(TdUiKind.Label, q.Text, 28f, icon: TdIcon.Question);
                double left = Math.Max(0.0, rules.TimerSeconds - (_now() - _quizStart));
                s.Items.Add(new TdUiItem(new DipRect(Pad, s.Y, s.Width - 2 * Pad, RowH - 4), TdUiKind.Bar, "Tempo",
                    Right: Math.Ceiling(left).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " s", Tone: TdTone.Accent,
                    Fill: (float)Math.Clamp(left / rules.TimerSeconds, 0.0, 1.0)));
                s.Y += RowH;
                float half = (s.Width - 2 * Pad - 2f) / 2f;
                s.Items.Add(new TdUiItem(new DipRect(Pad, s.Y, half, 34f), TdUiKind.Button, "V  Verdadeiro", TdUiAction.QuizTrue, Tone: TdTone.Good,
                    Highlight: tutorial && q.Answer));
                s.Items.Add(new TdUiItem(new DipRect(Pad + half + 2f, s.Y, half, 34f), TdUiKind.Button, "F  Falso", TdUiAction.QuizFalse, Tone: TdTone.Bad,
                    Highlight: tutorial && !q.Answer));
                s.Y += 36f;
                s.Add(TdUiKind.Label, $"Acerte: +{m.BonusCreditsFor(m.NextWaveNumber)} créditos", 14f, tone: TdTone.Dim, icon: TdIcon.Coin);
                if (tutorial)
                    s.Items.Add(new TdUiItem(new DipRect(s.Width - Pad - 96f, s.Y, 96f, 16f), TdUiKind.Button, "Pular", TdUiAction.TutorialSkip, Tone: TdTone.Dim, Icon: TdIcon.Cross));
                if (tutorial) s.Y += 18f;
                break;
            }
        }
        var items = new List<TdUiItem>(s.Items.Count + 1)
        {
            new(new DipRect(s.Width - HeaderH - 2, 2, HeaderH, HeaderH - 4), TdUiKind.Button, "X", TdUiAction.QuizClose),
        };
        items.AddRange(s.Items);
        return new TdPopoverView("Pergunta-bônus", [.. items], s.Width, s.Y + Pad, 0f);
    }
}
