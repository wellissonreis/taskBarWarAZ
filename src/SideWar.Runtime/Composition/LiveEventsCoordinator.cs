using SideWar.Core.Missions;
using SideWar.Core.Simulation;
using SideWar.Runtime.Hosting;
using SideWar.Runtime.LiveEvents;
using SideWar.Runtime.Panel;
using SideWar.Runtime.Presence;

namespace SideWar.Runtime.Composition;

/// <summary>
/// Lado da plataforma dos eventos ao vivo (SDD-22 RF-22.8, SDD-26): banner de pico/escalada/chefe, caixa de suprimentos e
/// card de pergunta do chefe. Tudo na T-Game; as chamadas são idempotentes e nunca ativam janela.
/// </summary>
public interface ILiveEventsPresenter
{
    /// <summary>A visão mudou (chip a 1 Hz, banner/caixa entrando ou saindo): mostra, redesenha ou oculta os hotspots.</summary>
    void ViewChanged(LiveEventsView view);

    /// <summary>O card do chefe mudou (aberto, timer, resultado) ou foi recolhido (<c>null</c>).</summary>
    void BossCardChanged(QuestionCardModel? card);
}

/// <summary>
/// Liga eventos ao vivo e missões à raiz de composição (T6.11b; pontos 1–3 e 5 do REPORT T6.11). Pura e headless: a plataforma
/// só fornece o <see cref="ILiveEventsPresenter"/> e repassa cliques. É <see cref="IGameTickHook"/> (conta eventos e tempo
/// presente por tick, sem tocar o estado) e <see cref="ILoopParticipant"/> (depois do <c>GameHost</c>: vira o dia, aplica
/// as coletas entre ticks, remonta a visão a ~1 Hz e após cada clique).
/// <list type="bullet">
/// <item><c>MissionService.Consume</c>/<c>AddPresentSeconds</c> por tick (só em Presente); <c>Sync</c> na entrada e na virada do dia
/// (a cada 30 s), com cada <c>GrantMissionReward</c> aplicado entre ticks (<c>CommandLog</c>).</item>
/// <item>Banner/caixa: <c>LiveEventsViewBuilder.Build</c>; só notifica se a visão (igualdade por valor) mudou.</item>
/// <item>Clique no banner de chefe → <see cref="BossQuestionFlow.Open"/>; pico e caixa → comando ao <c>GameHost</c>. Todo clique é presença.</item>
/// </list>
/// </summary>
public sealed class LiveEventsCoordinator : ILoopParticipant, IGameTickHook
{
    private const double RefreshSeconds = 1.0;
    private const double DayCheckSeconds = 30.0;

    private readonly GameRuntime _runtime;
    private readonly ILiveEventsPresenter _view;
    private readonly IClock _clock;
    private readonly Func<DateOnly> _today;
    private readonly Action<string> _log;
    private readonly Action? _missionsChanged;
    private long _cursor;
    private long _nextRefresh;
    private long _nextDayCheck;
    private bool _dirty = true;
    private int _lastBadge = -1;
    private LiveEventsView _current = LiveEventsView.Empty;
    private QuestionCardModel? _card;

    public LiveEventsCoordinator(GameRuntime runtime, MissionService missions, ILiveEventsPresenter view, IClock clock,
        Func<DateOnly>? today = null, Action<string>? log = null, Action? missionsChanged = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        Missions = missions ?? throw new ArgumentNullException(nameof(missions));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _today = today ?? (() => DateOnly.FromDateTime(DateTime.Now));
        _log = log ?? (_ => { });
        _missionsChanged = missionsChanged;
        Boss = new BossQuestionFlow(clock);
        _cursor = runtime.State.Events.Written;
    }

    public MissionService Missions { get; }
    public BossQuestionFlow Boss { get; }

    /// <summary>Visão vigente (igualdade por valor).</summary>
    public LiveEventsView Current => _current;

    /// <summary>Segundos restantes do chip "×3 m:ss" do widget (0 = sem chip), RF-23.3.</summary>
    public int WidgetChipSeconds => _current.WidgetChip?.Seconds ?? 0;

    /// <summary>Badge do nav: missões concluídas e não coletadas.</summary>
    public int MissionBadge => Missions.Badge;

    /// <summary>Entrada do app (antes do primeiro frame): sorteia/restaura o dia e coleta a virada pendente.</summary>
    public void Start()
    {
        SyncDay();
        _nextDayCheck = _clock.NowTicks + Ticks(DayCheckSeconds);
        _dirty = true;
    }

    // ---------------------------------------------------------------- por tick (hook do GameHost; não altera a simulação)

    public void BeforeStep(SimState state, GameTickContext context) { }

    public void AfterStep(SimState state, GameTickContext context)
    {
        Missions.Consume(state, ref _cursor);
        if (state.Present) Missions.AddPresentSeconds(Timing.LoopConstants.TickSeconds);
    }

    // ---------------------------------------------------------------- laço

    public long? NextDeadline
    {
        get
        {
            if (!_runtime.App.IsVisible) return null;
            if (_dirty) return _clock.NowTicks;
            long next = _nextDayCheck;
            if (_runtime.App.SimulationRunning) next = Math.Min(next, _nextRefresh);
            if (Boss.CloseDeadlineTicks != 0) next = Math.Min(next, Boss.CloseDeadlineTicks);
            return next;
        }
    }

    public void OnWake(IClock clock)
    {
        if (!_runtime.App.IsVisible) return;
        long now = clock.NowTicks;
        if (now >= _nextDayCheck)
        {
            _nextDayCheck = now + Ticks(DayCheckSeconds);
            SyncDay();
        }
        bool closeDue = Boss.CloseDeadlineTicks != 0 && now >= Boss.CloseDeadlineTicks;
        if (_dirty || closeDue || (_runtime.App.SimulationRunning && now >= _nextRefresh)) Refresh(now);
    }

    private void Refresh(long now)
    {
        _dirty = false;
        _nextRefresh = now + Ticks(RefreshSeconds);
        var state = _runtime.State;
        var view = LiveEventsViewBuilder.Build(state);
        if (!view.Equals(_current))
        {
            _current = view;
            _view.ViewChanged(view);
            _runtime.Host.MarkDirty(); // o chip do widget acompanha a visão
        }
        Boss.Refresh(state, _runtime.Context);
        if (!Equals(Boss.Card, _card))
        {
            _card = Boss.Card;
            _view.BossCardChanged(_card);
        }
        int badge = Missions.Badge;
        if (badge != _lastBadge)
        {
            _lastBadge = badge;
            _missionsChanged?.Invoke();
        }
    }

    /// <summary>Virada do dia: coleta as concluídas (DP-27.13) e aplica cada comando entre ticks (entra no CommandLog).</summary>
    public void SyncDay()
    {
        var grants = new List<GrantMissionReward>();
        Missions.Sync(_today(), grants);
        foreach (var grant in grants)
        {
            var r = _runtime.State.Apply(_runtime.Context, grant);
            if (!r.IsOk) _log($"missions: grant {grant.MissionId} rejected ({r.Reason})");
        }
        if (grants.Count > 0) _runtime.Host.MarkDirty();
        _dirty = true;
    }

    /// <summary>Uma mudança de dados feita fora do laço (coleta no painel): remonta a visão no próximo wake.</summary>
    public void MarkDirty() => _dirty = true;

    // ---------------------------------------------------------------- cliques (HotspotWindow.Clicked / card do chefe)

    /// <summary>Clique no banner: chefe abre o card (Core); pico escala ×3; "defesa escalada" não é clicável.</summary>
    public void BannerClicked() => Click(LiveClickTarget.Banner);

    /// <summary>Clique na caixa de suprimentos.</summary>
    public void CrateClicked() => Click(LiveClickTarget.Crate);

    private void Click(LiveClickTarget target)
    {
        _runtime.NotifyInteraction(); // RF-25.1 / RF-26.15: todo clique é presença
        var view = _current;
        if (target == LiveClickTarget.Banner && view.Banner is { Clickable: true, Kind: LiveBannerKind.Boss })
            Boss.Open(_runtime.State, _runtime.Context);
        else if (LiveEventsViewBuilder.CommandFor(view, target) is { } command)
            _runtime.Commands.Post(command); // aplicado entre ticks pelo GameHost
        _runtime.Host.MarkDirty();
        _dirty = true;
    }

    /// <summary>Alternativa A–D do card do chefe.</summary>
    public void BossAnswered(int optionIndex)
    {
        _runtime.NotifyInteraction();
        Boss.Answer(_runtime.State, _runtime.Context, optionIndex);
        _runtime.Host.MarkDirty();
        _dirty = true;
    }

    /// <summary>Minimizar/fechar o card: só esconde; o tempo corre (RF-26.10).</summary>
    public void BossClosed()
    {
        _runtime.NotifyInteraction();
        Boss.Close();
        _dirty = true;
    }

    /// <summary>Botão pressionado numa janela do card do chefe: presença.</summary>
    public void NotifyInteraction() => _runtime.NotifyInteraction();

    private long Ticks(double seconds) => (long)(seconds * _clock.TicksPerSecond);
}
