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

    private int _quizKey;

    /// <summary>TD.30b: a pergunta vale em Preparo ou Onda (cartão do cantinho); a cadência é por tempo real, não por onda.</summary>
    private static bool QuizPhaseOk(TdMatchState m) => m.Phase is MatchPhase.Preparo or MatchPhase.Onda;

    private static int QuizKey(TdMatchState m) => m.Options.ContinuousWaves ? m.StageNumber * 1000 + m.WaveNumber : m.NextWaveNumber;

    private static int QuizPayWave(TdMatchState m) => m.Phase == MatchPhase.Preparo ? m.NextWaveNumber : m.WaveNumber;

    private bool QuizEnabled => _quiz is not null && _onb is { BonusQuestions: true };

    /// <summary>Pergunta-bônus na tela (aguardando resposta ou mostrando o resultado).</summary>
    public bool QuizActive => _quizQ is not null;

    /// <summary>Afirmação em exibição (testes/goldens).</summary>
    public TdQuestion? QuizQuestion => _quizQ;

    /// <summary>Chip do HUD pulsando: o jogador precisa do HUD e ele está recolhido (reforço do tutorial e dicas de 2–3 palavras).</summary>
    public bool Pulsing => _host.HudMinimized && _onb is not null && (HudNeeded(_match()) || ComputeStep(_match()) == TdTutorialStep.HudOpen);

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
        UpdateModalPause();
        _quizOut = QuizOutcome.None;
        Changed?.Invoke();
    }

    /// <summary>TD.30b: a cada ~5 min de relógio real (sorteio 4–6 min) uma pergunta, independente de onda, velocidade e fase.</summary>
    public const double QuizMinIntervalSeconds = 240.0, QuizMaxIntervalSeconds = 360.0;

    /// <summary>Um salto de relógio maior que isto entre dois despertares (suspensão, hibernação) não conta como jogo rodando.</summary>
    private const double QuizMaxDeltaSeconds = 30.0;

    private readonly Func<double> _quizDraw = Random.Shared.NextDouble; // [0,1) injetável nos testes
    private TdMatchState? _quizMatch;
    private double _quizClockLast = -1.0, _quizElapsed, _quizInterval = QuizMaxIntervalSeconds;

    /// <summary>Segundos de jogo rodando acumulados desde a última pergunta (testes).</summary>
    public double QuizElapsedSeconds => _quizElapsed;

    /// <summary>Intervalo sorteado para a próxima pergunta (testes).</summary>
    public double QuizIntervalSeconds => _quizInterval;

    private void RestartQuizClock()
    {
        _quizElapsed = 0.0;
        _quizInterval = QuizMinIntervalSeconds + Math.Clamp(_quizDraw(), 0.0, 1.0) * (QuizMaxIntervalSeconds - QuizMinIntervalSeconds);
    }

    /// <summary>Jogo rodando de verdade: sem pausa (usuário ou modal), com a janela visível (boss key) e em Preparo/Onda.</summary>
    private bool QuizClockRunning(TdMatchState m) => QuizPhaseOk(m) && !m.Paused && !_host.Paused && !ModalOpen && !BossHidden;

    private void SyncQuiz(TdMatchState m)
    {
        double now = _now();
        double dt = _quizClockLast < 0.0 ? 0.0 : Math.Clamp(now - _quizClockLast, 0.0, QuizMaxDeltaSeconds);
        _quizClockLast = now;
        if (!ReferenceEquals(m, _quizMatch))
        {
            _quizMatch = m;
            RestartQuizClock(); // app novo/partida nova/retomada: o primeiro intervalo é cheio
            dt = 0.0;
        }
        if (!QuizPhaseOk(m)) { ClearQuiz(); return; }
        int key = QuizKey(m);
        if (_quizQ is null)
        {
            if (!QuizEnabled) return;
            bool tutorial = TutorialOn(m) && !_quizExampleDone;
            bool due;
            if (tutorial) due = m.WaveNumber >= 1 && _quizWave != key && (m.Phase == MatchPhase.Preparo || m.Options.ContinuousWaves); // exemplo guiado do tutorial: logo após a 1ª onda
            else
            {
                if (QuizClockRunning(m)) _quizElapsed += dt;
                due = _quizElapsed >= _quizInterval;
            }
            if (!due || BossHidden || m.Paused || (Mode != TdUiMode.None && !QuickTowersOpen)) return;
            _quizWave = key;
            _quizKey = key;
            _quizQ = _quiz!.Pick(tutorial ? QuizExampleId : null);
            _quizOut = QuizOutcome.None;
            _quizStart = now;
            RestartQuizClock(); // ignorar/expirar/responder reinicia o relógio: não acumula
            UpdateModalPause();
            Changed?.Invoke();
            return;
        }
        if (_quizOut == QuizOutcome.None)
        {
            if (now - _quizStart >= m.Content.Stages.Rules.BonusQuestion.TimerSeconds)
            {
                _send(new AnswerBonus(QuizNumber(_quizQ.Id), false));
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
        if (_quizQ is null || _quizOut != QuizOutcome.None || !QuizPhaseOk(m)) return;
        bool ok = answer == _quizQ.Answer;
        _quizPaid = ok ? m.BonusCreditsFor(QuizPayWave(m)) : 0;
        _send(new AnswerBonus(QuizNumber(_quizQ.Id), ok));
        _quizOut = ok ? QuizOutcome.Correct : QuizOutcome.Wrong;
        _quizResultUntil = _now() + (ok ? QuizResultSeconds : QuizWrongSeconds);
        _quizExampleDone = true;
        Changed?.Invoke();
    }

    private void CloseQuiz()
    {
        _quizExampleDone = true;
        if (_quizQ is not null && _quizOut == QuizOutcome.None) _send(new AnswerBonus(QuizNumber(_quizQ.Id), false));
        _quizQ = null;
        UpdateModalPause();
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
            for (int i = 0; i < m.Content.Towers.Length; i++) if (m.UnlockedTowerStage >= m.Content.Towers[i].UnlockStage && m.NextTowerCost(i) < min) min = m.NextTowerCost(i);
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

    /// <summary>TD.30b: fontes do cartão (DIP; o renderer escala pelo monitor), largura máx. no cantinho e fator de altura de linha.</summary>
    public const float QuizStatementFont = 15f, QuizOptionFont = 14f, QuizWidth = 360f, QuizLineFactor = 1.3f, QuizCharWidth = 0.62f;

    /// <summary>Linhas que o texto ocupa (quebra por palavra; largura de caractere conservadora para não cortar no renderer).</summary>
    public static int QuizLines(string text, float widthDip, float fontDip)
    {
        int cap = Math.Max(1, (int)(widthDip / (fontDip * QuizCharWidth)));
        int lines = 1, cur = 0;
        foreach (var w in text.Split(' '))
        {
            if (cur == 0) cur = w.Length;
            else if (cur + 1 + w.Length <= cap) cur += 1 + w.Length;
            else { lines++; cur = w.Length; }
            while (cur > cap) { lines++; cur -= cap; }
        }
        return lines;
    }

    private static void QuizText(Stack s, string text, float font, TdTone tone = TdTone.Normal, TdIcon icon = TdIcon.None)
    {
        float inner = s.Width - 2 * Pad - (icon != TdIcon.None ? 16f : 0f);
        float h = QuizLines(text, inner, font) * font * QuizLineFactor + 2f;
        s.Items.Add(new TdUiItem(new DipRect(Pad, s.Y, s.Width - 2 * Pad, h), TdUiKind.Label, text, Tone: tone, Icon: icon, FontSize: font));
        s.Y += h + 4f;
    }

    private TdPopoverView BuildQuiz(TdMatchState m)
    {
        var q = _quizQ!;
        var rules = m.Content.Stages.Rules.BonusQuestion;
        bool tutorial = TutorialOn(m) && _quizOut == QuizOutcome.None;
        var s = new Stack(QuizWidth);
        if (tutorial) QuizText(s, "Exemplo: clique em V ou F", QuizOptionFont, TdTone.Accent, TdIcon.Question);
        string answerText = q.Answer ? "VERDADEIRO" : "FALSO";
        switch (_quizOut)
        {
            case QuizOutcome.Correct:
                QuizText(s, $"Acertou! +{_quizPaid} créditos", QuizStatementFont, TdTone.Good, TdIcon.Check);
                QuizText(s, q.Explanation, QuizOptionFont, TdTone.Dim);
                break;
            case QuizOutcome.Wrong:
            case QuizOutcome.Expired:
                QuizText(s, (_quizOut == QuizOutcome.Expired ? "Tempo! " : "") + $"Era {answerText}.", QuizStatementFont, TdTone.Bad,
                    _quizOut == QuizOutcome.Expired ? TdIcon.Clock : TdIcon.Cross);
                QuizText(s, q.Explanation, QuizOptionFont, TdTone.Dim);
                break;
            default:
            {
                QuizText(s, q.Text, QuizStatementFont, icon: TdIcon.Question);
                double left = Math.Max(0.0, rules.TimerSeconds - (_now() - _quizStart));
                s.Items.Add(new TdUiItem(new DipRect(Pad, s.Y, s.Width - 2 * Pad, 22f), TdUiKind.Bar, "Tempo",
                    Right: Math.Ceiling(left).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " s", Tone: TdTone.Accent,
                    Fill: (float)Math.Clamp(left / rules.TimerSeconds, 0.0, 1.0), FontSize: 12f));
                s.Y += 26f;
                float half = (s.Width - 2 * Pad - 4f) / 2f;
                s.Items.Add(new TdUiItem(new DipRect(Pad, s.Y, half, 40f), TdUiKind.Button, "V  Verdadeiro", TdUiAction.QuizTrue, Tone: TdTone.Good,
                    Highlight: tutorial && q.Answer, FontSize: QuizOptionFont));
                s.Items.Add(new TdUiItem(new DipRect(Pad + half + 4f, s.Y, half, 40f), TdUiKind.Button, "F  Falso", TdUiAction.QuizFalse, Tone: TdTone.Bad,
                    Highlight: tutorial && !q.Answer, FontSize: QuizOptionFont));
                s.Y += 44f;
                QuizText(s, $"Acerte: +{m.BonusCreditsFor(QuizPayWave(m))} créditos", QuizOptionFont, TdTone.Dim, TdIcon.Coin);
                if (tutorial)
                    s.Items.Add(new TdUiItem(new DipRect(s.Width - Pad - 96f, s.Y, 96f, 18f), TdUiKind.Button, "Pular", TdUiAction.TutorialSkip, Tone: TdTone.Dim, Icon: TdIcon.Cross, FontSize: 12f));
                if (tutorial) s.Y += 22f;
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
