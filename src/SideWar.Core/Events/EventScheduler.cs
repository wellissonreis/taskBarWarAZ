using SideWar.Core.Simulation;

namespace SideWar.Core.Events;

public enum SchedulerEventKind
{
    PeakOffered, PeakExpired, PeakActivated, PeakBonusEnded, PeakDiscarded, PeakExtraSpawn,
    CrateSpawned, CrateExpired, CrateCollected, CrateClamped, Cleared,
}

/// <summary>Evento emitido pelo agendador (para banners, números flutuantes e spawn dos extras). <see cref="Value"/>: índice do extra, x da caixa ou ganho.</summary>
public readonly record struct SchedulerEvent(SchedulerEventKind Kind, double Value = 0);

/// <summary>
/// Limites do intervalo de x da caixa (SDD-26 RF-26.11, ADR-034): núcleo (<c>LeakX</c>) e portal, em DIP.
/// O x sorteado fica em <c>[LeakX + 24, PortalX − 24]</c> (ponto médio se o intervalo for menor que 48 DIP).
/// </summary>
public readonly record struct CrateBounds(float LeakX, float PortalX)
{
    public static CrateBounds From(StripLayout layout) => new(layout.LeakX, layout.PortalX);
}

public enum PeakPhase { Waiting, Offer, Bonus }

/// <summary>Estado persistível do agendador (save/restore).</summary>
public readonly record struct EventSchedulerState(
    PeakPhase Peak, double PeakTimer, bool PeakDue, bool BossActive, bool NeedsPeakDraw,
    int ExtrasPending, double NextExtraIn,
    bool CrateActive, double CrateTimer, int CrateX, bool NeedsCrateDraw, double CrateCountdown,
    int PeaksClicked, int CratesCollected);

/// <summary>
/// Agendador de pico de demanda e caixas de suprimentos (SDD-26 RF-26.2, 26.3, 26.11, 26.12), lógica pura.
/// Tempo em segundos de SimTime avançado por <see cref="Advance"/>; sorteios via <see cref="IQuestionRng"/> na
/// ordem de RF-26.13 (pico, caixa, x). O chefe (T6.6) é informado por <see cref="BossBannerShown"/>/<see cref="BossResolved"/>.
/// </summary>
public sealed class EventScheduler
{
    public const int PeakMinSeconds = 480, PeakMaxSeconds = 900;
    public const int CrateMinSeconds = 180, CrateMaxSeconds = 360;
    public const double PeakOfferSeconds = 30, PeakBonusSeconds = 60, CrateLifeSeconds = 45, ExtraSpacingSeconds = 12;
    public const int PeakExtras = 6, CrateMargin = 24;
    public const double PeakFactor = 3.0;
    public const long MinCrateGain = 50;
    private const double Eps = 1e-9;

    private PeakPhase _peak = PeakPhase.Waiting;
    private double _peakTimer;      // Waiting: contagem até o pico; Offer/Bonus: tempo restante
    private bool _peakDue, _bossActive, _needsPeakDraw;
    private int _extrasPending;
    private double _nextExtraIn;
    private bool _skipExtraDt; // o primeiro extra nasce no instante da oferta: o dt do tick da oferta não conta
    private bool _crateActive, _needsCrateDraw;
    private double _crateTimer, _crateCountdown;
    private int _crateX;
    private readonly List<SchedulerEvent> _events = new();

    private EventScheduler() { }

    /// <summary>Início da presença: sorteia o intervalo do pico e o da caixa (RF-26.13, passos 2 e 3).</summary>
    public static EventScheduler Start(IQuestionRng rng)
    {
        var s = new EventScheduler();
        s._peakTimer = DrawPeak(rng);
        s._crateCountdown = DrawCrate(rng);
        return s;
    }

    public static EventScheduler Restore(in EventSchedulerState st) => new()
    {
        _peak = st.Peak, _peakTimer = st.PeakTimer, _peakDue = st.PeakDue, _bossActive = st.BossActive,
        _needsPeakDraw = st.NeedsPeakDraw, _extrasPending = st.ExtrasPending, _nextExtraIn = st.NextExtraIn,
        _crateActive = st.CrateActive, _crateTimer = st.CrateTimer, _crateX = st.CrateX,
        _needsCrateDraw = st.NeedsCrateDraw, _crateCountdown = st.CrateCountdown,
        PeaksClicked = st.PeaksClicked, CratesCollected = st.CratesCollected,
    };

    public EventSchedulerState Snapshot() => new(_peak, _peakTimer, _peakDue, _bossActive, _needsPeakDraw,
        _extrasPending, _nextExtraIn, _crateActive, _crateTimer, _crateX, _needsCrateDraw, _crateCountdown,
        PeaksClicked, CratesCollected);

    public PeakPhase Peak => _peak;
    public bool PeakOffered => _peak == PeakPhase.Offer;
    public bool BonusActive => _peak == PeakPhase.Bonus;
    public bool PeakDue => _peakDue;
    /// <summary>Segundos restantes da oferta ou do bônus (0 fora deles).</summary>
    public double PeakRemaining => _peak == PeakPhase.Waiting ? 0 : _peakTimer;
    /// <summary>Segundos até o próximo pico (só em Waiting).</summary>
    public double PeakCountdown => _peak == PeakPhase.Waiting ? _peakTimer : 0;
    public int ExtrasPending => _extrasPending;
    public bool CrateActive => _crateActive;
    public int CrateX => _crateX;
    public double CrateRemaining => _crateActive ? _crateTimer : 0;
    public double CrateCountdown => _crateCountdown;
    public int PeaksClicked { get; private set; }
    public int CratesCollected { get; private set; }
    /// <summary>Fator de dano das torres: ×3 com bônus ativo (RF-26.3), senão 1.</summary>
    public double DamageFactor => BonusActive ? PeakFactor : 1.0;
    /// <summary>Há banner (pico em oferta ou chefe; o bônus ativo só ocupa o slot sem chefe). Nunca mais de 1 (RF-26.12).</summary>
    public bool BannerVisible => _bossActive || _peak != PeakPhase.Waiting;
    /// <summary>Eventos do último <see cref="Advance"/> ou clique/notificação.</summary>
    public IReadOnlyList<SchedulerEvent> Events => _events;

    /// <summary>Aplica o fator de pico à simulação (ponto único de escrita de <see cref="SimState.PeakDamageFactor"/>).</summary>
    public void ApplyTo(SimState state) => state.PeakDamageFactor = DamageFactor;

    /// <summary>
    /// Avança <paramref name="dt"/> s. Ausente: remove banner/bônus/caixa (RF-26.1), congela contadores e só
    /// deixa nascer os extras da fila (sem bônus). Sorteios pendentes de remoção ocorrem ao voltar a Presente.
    /// </summary>
    public void Advance(double dt, bool present, CrateBounds bounds, IQuestionRng rng)
    {
        _events.Clear();
        if (!present)
        {
            ClearForAbsence();
            StepExtras(dt);
            return;
        }
        if (_needsPeakDraw) { _peakTimer = DrawPeak(rng); _needsPeakDraw = false; }
        if (_needsCrateDraw) { _crateCountdown = DrawCrate(rng); _needsCrateDraw = false; }
        StepPeak(dt, rng);
        StepExtras(dt);
        StepCrate(dt, bounds, rng);
    }

    /// <summary>Banner do chefe apareceu (RF-26.12): descarta oferta de pico (sem missão; sorteia o próximo); bônus ativo continua.</summary>
    public void BossBannerShown(IQuestionRng rng)
    {
        _events.Clear();
        _bossActive = true;
        if (_peak == PeakPhase.Offer)
        {
            _events.Add(new(SchedulerEventKind.PeakDiscarded));
            EndPeakEvent(rng);
        }
    }

    /// <summary>Encontro do chefe resolvido/cancelado: o pico devido aparece agora (RF-26.12).</summary>
    public void BossResolved(bool present)
    {
        _events.Clear();
        _bossActive = false;
        if (_peakDue && present && _peak == PeakPhase.Waiting) { _peakDue = false; Offer(); }
    }

    /// <summary>Clique no banner de pico (≤ 30 s): ativa ×3 por 60 s. Conta para a missão só aqui.</summary>
    public bool ClickPeak()
    {
        _events.Clear();
        if (_peak != PeakPhase.Offer) return false;
        _peak = PeakPhase.Bonus;
        _peakTimer = PeakBonusSeconds;
        PeaksClicked++;
        _events.Add(new(SchedulerEventKind.PeakActivated));
        return true;
    }

    /// <summary>Clique na caixa: ganho <c>max(50, renda × 60)</c>; sorteia o próximo intervalo. Retorna null se não há caixa.</summary>
    public double? ClickCrate(double incomePerSecond, IQuestionRng rng)
    {
        _events.Clear();
        if (!_crateActive) return null;
        double gain = Math.Max((double)MinCrateGain, incomePerSecond * 60.0);
        _crateActive = false;
        CratesCollected++;
        _events.Add(new(SchedulerEventKind.CrateCollected, gain));
        _crateCountdown = DrawCrate(rng);
        return gain;
    }

    /// <summary>Após LayoutChanged: limita o x da caixa ativa ao novo intervalo (ADR-032).</summary>
    public void OnLayoutChanged(CrateBounds bounds)
    {
        _events.Clear();
        if (!_crateActive) return;
        var (lo, hi) = Range(bounds);
        int x = Math.Clamp(_crateX, lo, hi);
        if (x != _crateX) { _crateX = x; _events.Add(new(SchedulerEventKind.CrateClamped, x)); }
    }

    private void StepPeak(double dt, IQuestionRng rng)
    {
        switch (_peak)
        {
            case PeakPhase.Waiting:
                if (_peakDue)
                {
                    if (!_bossActive) { _peakDue = false; Offer(); }
                    return;
                }
                _peakTimer -= dt;
                if (_peakTimer <= Eps)
                {
                    if (_bossActive) { _peakDue = true; _peakTimer = 0; }
                    else Offer();
                }
                break;
            case PeakPhase.Offer:
                _peakTimer -= dt;
                if (_peakTimer <= Eps)
                {
                    _events.Add(new(SchedulerEventKind.PeakExpired));
                    EndPeakEvent(rng);
                }
                break;
            case PeakPhase.Bonus:
                _peakTimer -= dt;
                if (_peakTimer <= Eps)
                {
                    _events.Add(new(SchedulerEventKind.PeakBonusEnded));
                    EndPeakEvent(rng);
                }
                break;
        }
    }

    private void Offer()
    {
        _peak = PeakPhase.Offer;
        _peakTimer = PeakOfferSeconds;
        if (_extrasPending == 0) { _nextExtraIn = 0; _skipExtraDt = true; }
        _extrasPending += PeakExtras;
        _events.Add(new(SchedulerEventKind.PeakOffered));
    }

    private void EndPeakEvent(IQuestionRng rng)
    {
        _peak = PeakPhase.Waiting;
        _peakTimer = DrawPeak(rng);
    }

    private void StepExtras(double dt)
    {
        if (_extrasPending == 0) return;
        if (_skipExtraDt) _skipExtraDt = false; else _nextExtraIn -= dt;
        while (_extrasPending > 0 && _nextExtraIn <= Eps)
        {
            int index = (PeakExtras - 1) - ((_extrasPending - 1) % PeakExtras);
            _events.Add(new(SchedulerEventKind.PeakExtraSpawn, index));
            _extrasPending--;
            if (_extrasPending > 0) _nextExtraIn += ExtraSpacingSeconds;
        }
    }

    private void StepCrate(double dt, CrateBounds bounds, IQuestionRng rng)
    {
        if (_crateActive)
        {
            _crateTimer -= dt;
            if (_crateTimer <= Eps)
            {
                _crateActive = false;
                _events.Add(new(SchedulerEventKind.CrateExpired));
                _crateCountdown = DrawCrate(rng);
            }
            return;
        }
        _crateCountdown -= dt;
        if (_crateCountdown <= Eps)
        {
            _crateX = DrawX(bounds, rng);
            _crateActive = true;
            _crateTimer = CrateLifeSeconds;
            _events.Add(new(SchedulerEventKind.CrateSpawned, _crateX));
        }
    }

    private void ClearForAbsence()
    {
        bool any = false;
        if (_peak != PeakPhase.Waiting) { _peak = PeakPhase.Waiting; _peakTimer = 0; _needsPeakDraw = true; any = true; }
        if (_crateActive) { _crateActive = false; _needsCrateDraw = true; any = true; }
        if (any) _events.Add(new(SchedulerEventKind.Cleared));
    }

    private static (int lo, int hi) Range(CrateBounds b)
    {
        int lo = (int)Math.Ceiling(b.LeakX) + CrateMargin;
        int hi = (int)Math.Floor(b.PortalX) - CrateMargin;
        return (lo, hi);
    }

    private static int DrawX(CrateBounds b, IQuestionRng rng)
    {
        var (lo, hi) = Range(b);
        if (hi - lo < 0 || b.PortalX - b.LeakX < 2 * CrateMargin)
            return (int)Math.Round((b.LeakX + b.PortalX) / 2.0, MidpointRounding.AwayFromZero);
        return lo + rng.NextInt(hi - lo + 1);
    }

    private static int DrawPeak(IQuestionRng rng) => PeakMinSeconds + rng.NextInt(PeakMaxSeconds - PeakMinSeconds + 1);
    private static int DrawCrate(IQuestionRng rng) => CrateMinSeconds + rng.NextInt(CrateMaxSeconds - CrateMinSeconds + 1);
}
