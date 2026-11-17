using SideWar.Core.Td;
using SideWar.Runtime.Composition;
using SideWar.Runtime.Lifecycle;

namespace SideWar.Runtime.Td;

/// <summary>Estado de interação lido pelo render (seleção e passar do mouse; a UI completa é da TD.8).</summary>
public readonly record struct TdView(int SelectedTowerId, int HoverTowerId, bool CrateHover)
{
    public static TdView None => new(0, 0, false);

    /// <summary>Fantasma do modo construção (TD.8): X lógico (-1 = sem fantasma), validade (laranja se inválido) e torre candidata (alcance; -1 = genérico).</summary>
    public int GhostX { get; init; } = -1;
    public bool GhostValid { get; init; }
    public int GhostDef { get; init; } = -1;

    /// <summary>Ponto já escolhido no modo construção (-1 = ainda escolhendo): fica marcado enquanto o popover de torres está aberto.</summary>
    public int ChosenX { get; init; } = -1;

    /// <summary>TD.16b: modo posicionar ativo (a faixa aceita clique): borda e realce em toda a faixa; fora dele a faixa é click-through e não tem borda.</summary>
    public bool PlacingMode { get; init; }

    /// <summary>Tutorial (TD.15): contorno pulsante em toda a faixa de torres (passo "clique num ponto da faixa").</summary>
    public bool BandHint { get; init; }

    /// <summary>Tutorial (TD.15): X (DIP do espaço de desenho) de um alvo a destacar com anel âmbar (torre a melhorar, caixa-bomba); -1 = nenhum.</summary>
    public float HintX { get; init; } = -1f;
}

/// <summary>Destino dos frames da faixa TD (SceneFrameSink no App; fake nos testes). Só na T-Game.</summary>
public interface ITdFrameSink
{
    void Present(TdMatchState match, double nowSeconds, in TdView view);
}

/// <summary>
/// Render por evento da faixa TD (TD.7, RF-28.15): <b>0 fps em Preparo e em pausa</b> (só redesenha quando o estado muda ou há
/// <see cref="MarkDirty"/>), <b>30 fps em Onda</b> (um frame por tick novo da simulação, que roda a 30 Hz). Participante do laço
/// <i>depois</i> do <see cref="TdGameHost"/>. Sem deadline próprio, exceto o pedido de redesenho pendente; oculto = nunca desenha
/// (o <c>Show</c> pede <see cref="ForceDraw"/>). Só na T-Game.
/// </summary>
public sealed class TdRenderDriver : ILoopParticipant
{
    private readonly TdGameHost _host;
    private readonly AppStateMachine _app;
    private readonly IClock _clock;
    private readonly ITdFrameSink _sink;
    private bool _dirty = true;
    private long _lastFrame = -1;
    private long _lastSignature;
    private TdView _view = TdView.None;
    private double _lastDrawAt = double.NegativeInfinity;

    public TdRenderDriver(TdGameHost host, AppStateMachine app, IClock clock, ITdFrameSink sink)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    }

    /// <summary>Frames apresentados (diagnóstico e testes dos fps).</summary>
    public long Presents { get; private set; }

    public TdView View => _view;

    /// <summary>
    /// Jogador ausente (TD.14: outro app ativo): a animação cai de 30 para <see cref="TdFocusState.AbsentFps"/> fps. Pedidos explícitos
    /// (mouse, seleção, layout) continuam desenhando na hora; a partida segue rodando a 30 Hz.
    /// </summary>
    public bool Absent { get; set; }

    /// <summary>Pedido de redesenho (DPI, monitor, layout, seleção, passar do mouse, Show).</summary>
    public void MarkDirty() => _dirty = true;

    /// <summary>Substitui a visão inteira (UI TD.8: seleção, fantasma e alcance); redesenha só se mudou.</summary>
    public void SetView(in TdView view) => Update(view);

    public void SetSelected(int towerId) => Update(_view with { SelectedTowerId = towerId });

    public void SetHover(int towerId, bool crate) => Update(_view with { HoverTowerId = towerId, CrateHover = crate });

    /// <summary>Desenha já (Show após Hide: um frame completo antes de mostrar a janela).</summary>
    public void ForceDraw() => Draw();

    public long? NextDeadline => _dirty && _app.IsVisible ? _clock.NowTicks : null;

    public void OnWake(IClock clock)
    {
        if (!_app.IsVisible || _host.IsShutdown) return;
        var match = _host.Match;
        bool animate = match.Phase == MatchPhase.Onda && _app.SimulationRunning && match.Frame != _lastFrame;
        if (animate && !_dirty && Absent && clock.NowTicks / (double)clock.TicksPerSecond - _lastDrawAt < 1.0 / TdFocusState.AbsentFps) return;
        if (!animate && !_dirty)
        {
            long signature = Signature(match);
            if (signature == _lastSignature) return;
        }
        Draw();
    }

    private void Update(TdView value)
    {
        if (value == _view) return;
        _view = value;
        _dirty = true;
    }

    private void Draw()
    {
        var match = _host.Match;
        _sink.Present(match, _clock.NowTicks / _clock.TicksPerSecond, _view);
        _lastDrawAt = _clock.NowTicks / (double)_clock.TicksPerSecond;
        _lastFrame = match.Frame;
        _lastSignature = Signature(match);
        _dirty = false;
        Presents++;
    }

    /// <summary>Resumo barato do que o Preparo/pausa pode mudar (comandos: construir, vender, melhorar, posicionar, layout, contagem).</summary>
    private static long Signature(TdMatchState match)
    {
        var h = new HashCode();
        h.Add((int)match.Phase);
        h.Add(match.Credits);
        h.Add(match.Integrity);
        h.Add(match.WaveNumber);
        h.Add(match.CrateActive);
        h.Add(match.Layout?.Width ?? 0);
        h.Add(match.Layout?.FreeZones.Length ?? 0);
        h.Add((int)Math.Ceiling(match.CountdownRemainingSeconds));
        var towers = match.Towers;
        h.Add(towers.Count);
        for (int i = 0; i < towers.Count; i++)
        {
            var t = towers[i];
            h.Add(t.Id);
            h.Add(t.X);
            h.Add(t.Placed);
            h.Add(t.Level);
            h.Add(t.Branch);
        }
        return h.ToHashCode();
    }
}
