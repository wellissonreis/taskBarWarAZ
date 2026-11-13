using SideWar.Core.Simulation;
using SideWar.Runtime.Panel;
using SideWar.Runtime.Presence;

namespace SideWar.Runtime.Composition;

/// <summary>
/// Lado da plataforma do card "Bem-vindo de volta" e da pergunta do COLETAR ×2 (SDD-24). Tudo na T-Game; as chamadas são
/// idempotentes e nunca ativam janela.
/// </summary>
public interface IOfflineCardPresenter
{
    /// <summary>Mostra o card (<c>OfflineCardWindow</c>, NOACTIVATE) e o redesenha.</summary>
    void ShowCard();

    /// <summary>Fecha o card (recolhido ou coletado).</summary>
    void HideCard();

    /// <summary>Redesenha o card aberto (a <see cref="OfflineCardController.Version"/> mudou).</summary>
    void RefreshCard();

    /// <summary>Abre a janela da pergunta com o modelo da sessão (45 s).</summary>
    void ShowQuestion(OfflineQuestionSession session, int stage);

    /// <summary>Redesenha a pergunta (timer ou resultado).</summary>
    void RefreshQuestion(OfflineQuestionSession session, int stage);

    void HideQuestion();

    /// <summary>O badge "+X" do widget mudou (<see cref="OfflineCardController.BadgeCredits"/>).</summary>
    void BadgeChanged();
}

/// <summary>
/// Liga o card "Bem-vindo de volta" (T6.4/T6.4b) à raiz de composição (T4.10; SDD-07 RF-07.6 passo 3, SDD-25
/// RF-25.9/25.10/25.11). Pura e headless: a plataforma só fornece o <see cref="IOfflineCardPresenter"/> e repassa o mouse.
/// <list type="bullet">
/// <item><see cref="OnHide"/> guarda o <c>OfflineSnapshot</c> (instante, estado vivo); <see cref="OnRestoreContext"/> roda
/// <c>OfflineCalculator.Compute</c> → <c>OfflineResultApplier.Apply</c> (entre ticks) → <c>controller.Present</c>.</item>
/// <item>É um <see cref="ILoopParticipant"/>: acorda no prazo de 60 s do card, no tick de 1 s e nos 45 s da pergunta, e só
/// com a cena visível (oculto = 0 wakes).</item>
/// <item>Todo evento com <c>Interaction</c> conta como presença (RF-25.1); <c>Collected</c> vai ao Core entre ticks.</item>
/// </list>
/// </summary>
public sealed class OfflineReturnCoordinator : ILoopParticipant
{
    private const double ResultShownSeconds = 2.0;

    private readonly GameRuntime _runtime;
    private readonly IClock _clock;
    private readonly Func<DateTime> _utcNow;
    private readonly IOfflineCardPresenter _view;
    private readonly Action<string> _log;
    private readonly OfflineCalculator _calculator;

    private OfflineSnapshot? _baseline;
    private OfflineQuestionSession? _question;
    private long _questionLast;
    private long _questionTickAt;
    private long _resultCloseAt;
    private int _questionStage;

    public OfflineReturnCoordinator(GameRuntime runtime, OfflineCardController card, IOfflineCardPresenter view, IClock clock,
        Func<DateTime>? utcNow = null, Action<string>? log = null, OfflineCalculator? calculator = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Card = card ?? throw new ArgumentNullException(nameof(card));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _log = log ?? (_ => { });
        _calculator = calculator ?? new OfflineCalculator(runtime.Content);
    }

    public OfflineCardController Card { get; }

    /// <summary>Pergunta aberta (COLETAR ×2) ou null.</summary>
    public OfflineQuestionSession? Question => _question;

    public int QuestionStage => _questionStage;

    /// <summary>Há baseline para o próximo <see cref="OnRestoreContext"/>.</summary>
    public bool HasBaseline => _baseline is not null;

    // ---------------------------------------------------------------- ciclo de vida (efeitos Hide / RestoreContext)

    /// <summary>
    /// Início do app com sessão restaurada: o estado vivo já é o do <c>session.json</c>; só faltam o instante gravado e a
    /// auto-escala (RF-25.6). Chamar antes de <c>CompleteStartup</c>.
    /// </summary>
    public void SetBaseline(DateTime savedUtc, bool autoScaleOn, double autoScaleAccumSeconds) =>
        _baseline = Snapshot(savedUtc, autoScaleOn, autoScaleAccumSeconds);

    /// <summary>Efeito <c>Hide</c>: congela o snapshot offline (nada é simulado enquanto oculto).</summary>
    public void OnHide() => _baseline = Snapshot(_utcNow(), _runtime.Host.AutoScaleEnabled, _runtime.Host.AutoScaleAccumSeconds);

    /// <summary>
    /// Efeito <c>RestoreContext</c> (antes do primeiro frame): calcula, aplica entre ticks e apresenta o card se couber.
    /// Reabre o card restaurado (<c>session.json</c>) e rearma o prazo de 60 s.
    /// </summary>
    public void OnRestoreContext()
    {
        var baseline = _baseline;
        _baseline = null;
        bool presented = baseline is not null && ComputeAndApply(baseline);
        // Card/pergunta pendentes (restaurados ou apresentados agora): reabre e rearma a contagem de 60 s.
        switch (Card.State)
        {
            case OfflineCardState.Open:
                Card.NotifyMousePress();
                if (!presented) _view.ShowCard();
                break;
            case OfflineCardState.AskingQuestion:
                _view.ShowCard();
                break;
        }
        _questionLast = _clock.NowTicks;
        _view.BadgeChanged();
    }

    private bool ComputeAndApply(OfflineSnapshot baseline)
    {
        OfflineResult result;
        try { result = _calculator.Compute(baseline, _utcNow()); }
        catch (ArgumentException e)
        {
            _log($"offline: skipped ({e.Message})");
            return false;
        }
        if (result.Report.TempoAcumuladoS <= 0) return false; // relógio para trás / sem tempo: nada a aplicar
        var state = _runtime.State;
        OfflineResultApplier.Apply(state, _runtime.Context, result);
        _runtime.Host.AutoScaleAccumSeconds = result.State.AutoScaleAccumSeconds;
        _runtime.Host.MarkDirty();
        foreach (var line in result.Logs) _log(line);
        if (!result.ShowCard) return false;
        Handle(Card.Present(result));
        return true;
    }

    // ---------------------------------------------------------------- laço

    public long? NextDeadline
    {
        get
        {
            if (!_runtime.App.IsVisible) return null;
            long? next = null;
            if (Card.State == OfflineCardState.Open && Card.DeadlineTicks > 0) next = Card.DeadlineTicks;
            if (_question is not null)
            {
                long q = _resultCloseAt != 0 ? _resultCloseAt : _questionTickAt;
                next = next is null ? q : Math.Min(next.Value, q);
            }
            return next;
        }
    }

    public void OnWake(IClock clock)
    {
        long now = clock.NowTicks;
        if (!_runtime.App.IsVisible)
        {
            _questionLast = now; // oculto: a pergunta não corre
            return;
        }
        if (Card.State == OfflineCardState.Open && Card.DeadlineTicks > 0 && now >= Card.DeadlineTicks)
            Handle(Card.Update());
        if (_question is { } q)
        {
            if (_resultCloseAt != 0)
            {
                if (now >= _resultCloseAt) CloseQuestionWindow();
            }
            else if (now >= _questionTickAt)
            {
                double dt = (now - _questionLast) / clock.TicksPerSecond;
                _questionLast = now;
                if (q.Advance(dt)) Handle(OfflineQuestionFlow.Timeout(_runtime.State, _runtime.Context, Card, q), q);
                else
                {
                    _questionTickAt = now + (long)clock.TicksPerSecond;
                    _view.RefreshQuestion(q, _questionStage);
                }
            }
        }
    }

    // ---------------------------------------------------------------- mouse do card (SDD-24 RF-24.11)

    /// <summary>Botão do mouse pressionado no card (qualquer lugar): reinicia o contador de 60 s (hover não chama).</summary>
    public void CardMouseDown()
    {
        Card.NotifyMousePress(); // o laço relê NextDeadline depois de cada mensagem
    }

    /// <summary>Soltar sobre o mesmo alvo do clique: COLETAR ×2 abre a pergunta, COLETAR +X coleta ×1.</summary>
    public void CardClick(OfflineCardHitKind hit)
    {
        switch (hit)
        {
            case OfflineCardHitKind.CollectX2: Handle(Card.PressCollectX2()); break;
            case OfflineCardHitKind.Collect: Handle(Card.PressCollect()); break;
        }
    }

    /// <summary>Botão pressionado numa janela do card/pergunta: conta como interação de presença (RF-25.1, CA-24.12).</summary>
    public void NotifyInteraction() => _runtime.NotifyInteraction();

    /// <summary>Clique no badge "+X" do widget.</summary>
    public void BadgeClicked() => Handle(Card.ReopenFromBadge());

    // ---------------------------------------------------------------- pergunta (COLETAR ×2)

    /// <summary>Alternativa escolhida: acerto ×2, erro ×1. A janela mostra o resultado por 2 s.</summary>
    public void QuestionAnswered(int optionIndex)
    {
        if (_question is not { IsResolved: false } q) return;
        if (optionIndex < 0 || optionIndex >= q.Question.Options.Length) return;
        Handle(OfflineQuestionFlow.Answer(_runtime.State, _runtime.Context, Card, q, optionIndex), q);
    }

    /// <summary>Janela da pergunta fechada: sem resposta = ×1.</summary>
    public void QuestionClosedByUser()
    {
        if (_question is not { } q) return;
        if (q.IsResolved) { CloseQuestionWindow(); return; }
        Handle(OfflineQuestionFlow.Close(_runtime.State, _runtime.Context, Card, q.ReportId));
        CloseQuestionWindow();
    }

    private void BeginQuestion(long reportId)
    {
        var session = OfflineQuestionFlow.Begin(_runtime.State, _runtime.Context, reportId);
        if (session is null)
        {
            Handle(OfflineQuestionFlow.Close(_runtime.State, _runtime.Context, Card, reportId)); // sem pergunta: ×1
            return;
        }
        _question = session;
        _questionStage = _runtime.State.Stage;
        _questionLast = _clock.NowTicks;
        _questionTickAt = _clock.NowTicks + (long)_clock.TicksPerSecond;
        _resultCloseAt = 0;
        _view.ShowQuestion(session, _questionStage);
    }

    private void CloseQuestionWindow()
    {
        _question = null;
        _resultCloseAt = 0;
        _view.HideQuestion();
    }

    // ---------------------------------------------------------------- eventos do controlador

    private void Handle(OfflineCardEvent ev, OfflineQuestionSession? resolved = null)
    {
        if (ev.Kind == OfflineCardEventKind.Ignored) return;
        if (ev.Interaction) _runtime.NotifyInteraction(); // RF-25.1: abrir, recolher e clicar no card são presença
        switch (ev.Kind)
        {
            case OfflineCardEventKind.Presented:
            case OfflineCardEventKind.Reopened:
                if (_runtime.App.IsVisible) _view.ShowCard();
                break;
            case OfflineCardEventKind.Collapsed:
                _view.HideCard();
                break;
            case OfflineCardEventKind.QuestionRequested:
                _view.RefreshCard();
                BeginQuestion(ev.ReportId);
                break;
            case OfflineCardEventKind.Collected:
                if (ev.Command is { } command)
                {
                    _runtime.State.Apply(_runtime.Context, command); // entre ticks (laço/mouse da T-Game), entra no CommandLog
                    _runtime.Host.MarkDirty();
                }
                _view.HideCard();
                if (resolved is not null && _question == resolved)
                {
                    _resultCloseAt = _clock.NowTicks + (long)(ResultShownSeconds * _clock.TicksPerSecond);
                    _view.RefreshQuestion(resolved, _questionStage);
                }
                else if (_question is not null) CloseQuestionWindow();
                break;
        }
        _view.BadgeChanged();
    }

    // ---------------------------------------------------------------- snapshot offline do estado vivo (RF-25.6)

    private OfflineSnapshot Snapshot(DateTime savedUtc, bool autoScaleOn, double accumSeconds)
    {
        var s = _runtime.State;
        var towers = new List<OfflineTower>();
        for (int i = 0; i < s.Towers.Capacity; i++)
        {
            if (!s.Towers.IsAlive(i)) continue;
            ref readonly var t = ref s.Towers.At(i);
            towers.Add(new OfflineTower(t.Id, t.Type, t.Placed, t.Level));
        }
        towers.Sort((a, b) => a.Id.CompareTo(b.Id));
        var regions = _runtime.Content.Regions;
        string region = regions[Math.Clamp(s.RegionIndex, 0, regions.Length - 1)].Id;
        return new OfflineSnapshot(savedUtc.ToUniversalTime(), s.Stage, s.Wave, Math.Max(s.StageWaveMax, s.Wave), region,
            s.Reservas, s.Level, s.Xp, s.Credits, s.Carry, towers, autoScaleOn, accumSeconds);
    }
}
