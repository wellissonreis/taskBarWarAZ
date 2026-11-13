using SideWar.Core.Simulation;
using SideWar.Core.Prestige;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Input;
using SideWar.Runtime.Panel;
using SideWar.Runtime.Widget;

namespace SideWar.Runtime.Composition;

/// <summary>Lado da plataforma do painel de abas (SDD-24 RF-24.1 a RF-24.13). Tudo na T-Game.</summary>
public interface IPanelPresenter
{
    /// <summary>Estado de rolagem/hover da janela do painel (a janela rola e desenha o cromo; o conteúdo vem do coordenador).</summary>
    PanelViewState View { get; }

    /// <summary>Cria (se preciso), posiciona e mostra a janela sem ativá-la. Depois disto <see cref="View"/> tem o viewport real.</summary>
    void Show();

    void Hide();

    /// <summary>O conteúdo (modelo/layout da aba) mudou: redesenha a janela visível.</summary>
    void Redraw();

    /// <summary>O modo construção mudou de fase (ícone/REPOSICIONAR do Arsenal); a captura real do mouse e o overlay são da TA.8.</summary>
    void BuildModeChanged(BuildOutcome outcome);
}

/// <summary>
/// Painel das 4 abas ligado ao jogo (T6.11b; pontos 4 do REPORT T6.11, SDD-24 RF-24.3 a RF-24.13, ADR-034). Pura e headless:
/// guarda a aba ativa, monta modelo + layout (por mudança de dados a ~1 Hz, não por frame), faz o hit-test do corpo e aplica as
/// ações entre ticks (comandos ao <c>GameHost</c>, <c>MissionsMigrateController</c> para Missões/Migrar, auto-escala direto no
/// host, <see cref="BuildModeController"/> para o Arsenal). A janela só fornece o mouse em DIP e o <see cref="IPanelPresenter"/>.
/// </summary>
public sealed class PanelCoordinator : ILoopParticipant
{
    private const double RefreshSeconds = 1.0;

    private readonly GameRuntime _runtime;
    private readonly IPanelPresenter _view;
    private readonly IPanelTextMetrics _metrics;
    private readonly IClock _clock;
    private readonly Action? _changed;
    private WidgetTab? _active;
    private long _version;
    private long _key;
    private long _nextRefresh;
    private (TabBodyHit Body, ArsenalHit Arsenal) _down;
    private bool _pressedValid;

    public PanelCoordinator(GameRuntime runtime, Core.Missions.MissionService missions, IPanelPresenter view, IPanelTextMetrics metrics,
        IClock clock, Action? changed = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _changed = changed;
        Missions = missions ?? throw new ArgumentNullException(nameof(missions));
        Controller = new MissionsMigrateController(missions, new MigrateArming(clock));
        BuildMode = new BuildModeController(clock.TicksPerSecond);
    }

    public Core.Missions.MissionService Missions { get; }
    public MissionsMigrateController Controller { get; }
    public BuildModeController BuildMode { get; }

    /// <summary>Aba aberta (alimenta <c>WidgetInputs.ActiveTab</c>) ou null = painel fechado.</summary>
    public WidgetTab? ActiveTab => _active;

    public ArsenalLayout? Arsenal { get; private set; }
    public CampaignLayout? Campaign { get; private set; }
    public MissionsLayout? MissionsLayout { get; private set; }
    public MigrateLayout? Migrate { get; private set; }

    /// <summary>Modelos que geraram os layouts de Missões e Migrar (o renderer desenha modelo + layout).</summary>
    public MissionsTabModel? MissionsModel { get; private set; }
    public MigrateTabModel? MigrateModel { get; private set; }

    /// <summary>Torre em construção no Arsenal (borda do ícone) ou null.</summary>
    public BuildOrigin? Building => BuildMode.Phase == BuildPhase.Idle ? null : BuildMode.Origin;

    /// <summary>Incrementa a cada conteúdo remontado (diagnóstico/testes).</summary>
    public long Version => _version;

    // ---------------------------------------------------------------- comandos do widget (RF-23.8) e da cena (ADR-034)

    public void Execute(PanelCommand command)
    {
        switch (command.Kind)
        {
            case PanelCommandKind.ToggleTab: Toggle(command.Tab); break;
            case PanelCommandKind.OpenArsenal: OpenArsenal(command.TowerId); break;
        }
    }

    /// <summary>Mesma aba fecha; outra troca (rolagem zerada, MIGRAR desarmado).</summary>
    public void Toggle(WidgetTab tab)
    {
        if (_active == tab) Close();
        else Open(tab);
    }

    /// <summary>Clique numa torre da cena: abre o Arsenal e rola até a linha dela (uma vez, ao abrir; ADR-034).</summary>
    public void OpenArsenal(int towerId)
    {
        Open(WidgetTab.Arsenal);
        if (Arsenal is { } layout && layout.TryGetTowerRowTop(towerId, out float top))
        {
            _view.View.SetOffset(top);
            _view.Redraw();
        }
    }

    public void Open(WidgetTab tab)
    {
        if (_active != tab) Controller.Cancel(); // troca de aba desarma o MIGRAR
        _active = tab;
        _view.Show();
        _view.View.ResetOffset();
        Rebuild(force: true);
        _nextRefresh = _clock.NowTicks + Ticks(RefreshSeconds);
        _runtime.NotifyInteraction(); // abrir pelo widget é clique (o widget já notifica; idempotente)
        _changed?.Invoke();
    }

    public void Close()
    {
        if (_active is null) return;
        _active = null;
        Controller.Cancel();
        Apply(BuildMode.Cancel(BuildCancelReason.PanelClick));
        _pressedValid = false;
        _view.Hide();
        _changed?.Invoke();
    }

    /// <summary>Clique no X do cromo (<c>OverlayWindow.Clicked(PanelHit.Close)</c>).</summary>
    public void CloseClicked() => Close();

    // ---------------------------------------------------------------- mouse do corpo (DIP relativos ao canto da caixa visível)

    /// <summary>Botão esquerdo pressionado. Ícone/REPOSICIONAR do Arsenal já inicia o modo construção (RF-24.11, limiar de 6 px).</summary>
    public void PointerDown(float xDip, float yDip, BuildPointer cursor)
    {
        _pressedValid = false;
        if (_active is not { } tab || !IsBody(xDip, yDip)) return;
        _down = (HitBody(tab, xDip, yDip), HitArsenal(tab, xDip, yDip));
        _pressedValid = true;
        if (tab == WidgetTab.Arsenal && ArsenalIntent.StartsOnPress(_down.Arsenal)
            && ArsenalIntent.FromHit(_down.Arsenal, _runtime.Host.AutoScaleEnabled) is { } intent)
        {
            Apply(intent.Press(BuildMode, cursor, _clock.NowTicks));
            _view.Redraw();
        }
    }

    /// <summary>Botão solto: age se for o mesmo alvo do pressionamento. Em construção, <c>Pressed → Armed</c>.</summary>
    public void PointerUp(float xDip, float yDip, BuildPointer cursor)
    {
        bool valid = _pressedValid;
        _pressedValid = false;
        if (BuildMode.Phase == BuildPhase.Pressed)
        {
            var s = _runtime.State;
            Apply(BuildMode.Release(cursor, s.Layout, s.Towers, _clock.NowTicks));
            _view.Redraw();
            return;
        }
        if (!valid || _active is not { } tab || !IsBody(xDip, yDip)) return;
        var (body, arsenal) = (HitBody(tab, xDip, yDip), HitArsenal(tab, xDip, yDip));
        if (body != _down.Body || arsenal != _down.Arsenal) return;
        switch (tab)
        {
            case WidgetTab.Arsenal when !arsenal.IsNone: ArsenalClicked(arsenal); break;
            case WidgetTab.Missoes or WidgetTab.Migrar when body.Kind != TabBodyHitKind.None:
                Controller.Handle(body, _runtime.State, _runtime.Context);
                AfterAction();
                break;
            case WidgetTab.Missoes or WidgetTab.Migrar:
                if (Controller.Cancel()) AfterAction(); // clique em qualquer outro ponto desarma o MIGRAR
                break;
        }
    }

    private void ArsenalClicked(ArsenalHit hit)
    {
        if (ArsenalIntent.FromHit(hit, _runtime.Host.AutoScaleEnabled) is not { } intent) return;
        switch (intent.Kind)
        {
            case ArsenalIntentKind.SetAutoScale: _runtime.Host.AutoScaleEnabled = intent.AutoScaleOn; break;
            case ArsenalIntentKind.GameCommand when intent.Command is { } command: _runtime.Commands.Post(command); break;
            default: return; // BeginBuildMode já começou no PointerDown
        }
        AfterAction();
    }

    private void AfterAction()
    {
        _runtime.Host.MarkDirty();
        Rebuild(force: true);
        _changed?.Invoke(); // badge do nav (COLETAR)
    }

    // ---------------------------------------------------------------- laço

    public long? NextDeadline
    {
        get
        {
            if (_active is null) return null;
            long? next = _runtime.App.SimulationRunning ? _nextRefresh : null;
            if (Controller.Arming.Armed) next = Min(next, Controller.Arming.DeadlineTicks);
            if (BuildMode.NextDeadline is { } build) next = Min(next, build);
            return next;
        }
    }

    public void OnWake(IClock clock)
    {
        if (_active is null) return;
        long now = clock.NowTicks;
        bool force = false;
        if (Controller.Arming.Update()) force = true; // expirou os 5 s: volta a MIGRAR
        var expired = BuildMode.OnTick(now);
        if (expired.Changed) Apply(expired);
        if (now >= _nextRefresh)
        {
            _nextRefresh = now + Ticks(RefreshSeconds);
            Rebuild(force);
        }
        else if (force) Rebuild(true);
    }

    // ---------------------------------------------------------------- modelo + layout

    /// <summary>Remonta o conteúdo da aba ativa se os dados exibidos mudaram (ou <paramref name="force"/>); redesenha uma vez.</summary>
    public bool Rebuild(bool force = false)
    {
        if (_active is not { } tab) return false;
        long key = Key(tab);
        if (!force && key == _key) return false;
        _key = key;
        var state = _runtime.State;
        var ctx = _runtime.Context;
        long version = ++_version;
        float content;
        switch (tab)
        {
            case WidgetTab.Arsenal:
                var arsenalModel = ArsenalModelBuilder.Build(state, ctx, _runtime.Host.AutoScaleEnabled);
                Arsenal = ArsenalLayout.Compute(arsenalModel, _metrics);
                content = Arsenal.ContentHeight;
                break;
            case WidgetTab.Fases:
                Campaign = CampaignLayout.Compute(CampaignModelBuilder.Build(state, ctx), _metrics);
                content = Campaign.ContentHeight;
                break;
            case WidgetTab.Missoes:
                var missions = MissionsMigrateModelBuilder.BuildMissions(Missions, ctx.Content.Missions, _runtime.Presence.State,
                    _runtime.Presence.PresentSeconds, version: version);
                MissionsModel = missions;
                MissionsLayout = MissionsLayout.Compute(missions, _metrics);
                content = MissionsLayout.ContentHeight;
                break;
            default:
                var migrate = MissionsMigrateModelBuilder.BuildMigrate(state, ctx, Controller.ChosenRegion, Controller.Arming.Armed, version);
                MigrateModel = migrate;
                Migrate = MigrateLayout.Compute(migrate, _metrics, _view.View.Viewport);
                content = Migrate.ContentHeight;
                break;
        }
        _view.View.SetContent(content, _view.View.Viewport);
        _view.Redraw();
        return true;
    }

    // Assinatura barata do que cada aba mostra: igual = nada a redesenhar (RF-24.13: zero frames sem mudança).
    private long Key(WidgetTab tab)
    {
        var s = _runtime.State;
        var h = new HashCode();
        h.Add((int)tab);
        switch (tab)
        {
            case WidgetTab.Arsenal:
                h.Add(s.Credits);
                h.Add(_runtime.Host.AutoScaleEnabled);
                h.Add(s.Towers.Count);
                for (int i = 0; i < s.Towers.Capacity; i++)
                {
                    if (!s.Towers.IsAlive(i)) continue;
                    ref readonly var t = ref s.Towers.At(i);
                    h.Add(t.Id); h.Add(t.Level); h.Add(t.Placed);
                }
                h.Add(BuildMode.Phase); h.Add(BuildMode.Origin);
                break;
            case WidgetTab.Fases:
                h.Add(s.Stage); h.Add(s.Wave); h.Add(s.StageWaveMax);
                break;
            case WidgetTab.Missoes:
                h.Add(_runtime.Presence.State);
                h.Add((int)(_runtime.Presence.PresentSeconds / 60.0));
                foreach (var m in Missions.Missions) { h.Add(m.Id); h.Add(m.Progress); h.Add(m.Status); }
                break;
            default:
                h.Add(s.Reservas); h.Add(s.StageWaveMax); h.Add(s.RegionIndex); h.Add(s.Migrations);
                h.Add(PrestigeService.PreviewGain(s, _runtime.Context));
                h.Add(Controller.ChosenRegion); h.Add(Controller.Arming.Armed);
                break;
        }
        return h.ToHashCode();
    }

    // ---------------------------------------------------------------- hit-test

    private bool IsBody(float x, float y) =>
        PanelChromeHit.Test(x, y, PanelGeometry.PanelChromeHeightDip + _view.View.Viewport, _view.View.Content, _view.View.Viewport,
            _view.View.Offset) == PanelHit.Body;

    private TabBodyHit HitBody(WidgetTab tab, float x, float y) => tab switch
    {
        WidgetTab.Missoes when MissionsLayout is { } l => l.Hit(x, y, _view.View.Offset, _view.View.Viewport),
        WidgetTab.Migrar when Migrate is { } l => l.Hit(x, y, _view.View.Offset, _view.View.Viewport),
        _ => TabBodyHit.None,
    };

    private ArsenalHit HitArsenal(WidgetTab tab, float x, float y) =>
        tab == WidgetTab.Arsenal && Arsenal is { } l ? l.Hit(x, y, _view.View.Offset, _view.View.Viewport) : ArsenalHit.None;

    private void Apply(BuildOutcome outcome)
    {
        if (outcome.Command is { } command) _runtime.Commands.Post(command);
        if (outcome.Changed || outcome.Command is not null) _view.BuildModeChanged(outcome);
    }

    private long Ticks(double seconds) => (long)(seconds * _clock.TicksPerSecond);
    private static long? Min(long? a, long b) => a is { } x ? Math.Min(x, b) : b;
}
