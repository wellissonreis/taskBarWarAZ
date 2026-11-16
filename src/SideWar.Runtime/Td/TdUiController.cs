using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using BuildTower = SideWar.Core.Td.BuildTower;
using UpgradeTower = SideWar.Core.Td.UpgradeTower;
using SetTargeting = SideWar.Core.Td.SetTargeting;
using SideWar.Core.Td;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Input;

namespace SideWar.Runtime.Td;

/// <summary>
/// Controlador de UI da partida TD (TD.8, SDD-28 RF-28.14/RF-28.20): HUD compacto, modo construção (fantasma + popover de torre),
/// inspetor, preview de onda, menu, Mapa/árvore e cards de fim de partida. Lógica pura (sem janelas): lê o <see cref="TdMatchState"/>,
/// devolve modelos de layout (<see cref="TdHudView"/>, <see cref="TdPopoverView"/>) e envia comandos por <c>send</c>. O modo é estado de UI:
/// não entra no log nem no hash (RF-28.20.7). Só na T-Game.
/// </summary>
public sealed class TdUiController
{
    public const float PopoverWidth = 300f, Pad = 6f, RowH = 20f, TwoLineH = 34f, HeaderH = 22f;
    public const double BuildTimeoutSeconds = 30.0;

    private static readonly string[] TargetLabels = ["Primeiro", "Ultimo", "Forte", "Fraco", "Proximo"];

    private readonly Func<TdMatchState> _match;
    private readonly Action<IGameCommand> _send;
    private readonly ITdUiHost _host;
    private readonly Func<double> _now;

    private TdMatchState? _lastMatch;
    private bool _endShown;
    private int _chosenX = -1;       // X lógico já escolhido (passo "escolher a torre"); -1 = ainda no passo do ponto
    private int _moveTowerId;        // > 0: reposicionando uma torre SEM POSIÇÃO (sem escolha de tipo)
    private int _ghostX = -1;
    private bool _ghostValid;
    private int _hoverDef = -1;
    private int _selectedTowerId;
    private string? _notice;
    private bool _mapSeals;
    private double _lastActionAt;
    private float _anchorX;

    public TdUiController(Func<TdMatchState> match, Action<IGameCommand> send, ITdUiHost host, Func<double>? now = null)
    {
        _match = match ?? throw new ArgumentNullException(nameof(match));
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _now = now ?? (() => 0.0);
    }

    public TdUiMode Mode { get; private set; }

    /// <summary>Modo construção ativo (passo do ponto ou da escolha): a cena precisa de mouse e o ESC fica registrado.</summary>
    public bool BuildActive => Mode == TdUiMode.Build;

    /// <summary>Algum popover aberto (o ESC fecha).</summary>
    public bool PopoverOpen => Mode != TdUiMode.None;

    /// <summary>Passo do ponto: o fantasma segue o mouse e o clique escolhe o ponto.</summary>
    public bool ChoosingPoint => Mode == TdUiMode.Build && _chosenX < 0;

    public int SelectedTowerId => _selectedTowerId;
    public string? Notice => _notice;

    /// <summary>Notifica a apresentação (HUD/popover/faixa) de que algo mudou.</summary>
    public event Action? Changed;

    // ================================================================ entradas

    /// <summary>Visão da faixa: seleção, fantasma e alcance da torre candidata.</summary>
    public TdView View => new(_selectedTowerId, 0, false)
    {
        GhostX = BuildActive ? _ghostX : -1,
        GhostValid = _ghostValid,
        GhostDef = _hoverDef >= 0 ? _hoverDef : -1,
        ChosenX = BuildActive ? _chosenX : -1,
    };

    /// <summary>Move o mouse sobre a faixa (X em DIP): só importa no passo do ponto.</summary>
    public void PointerMoved(float xDip)
    {
        if (!ChoosingPoint) return;
        UpdateGhost(xDip);
        Changed?.Invoke();
    }

    /// <summary>Botão esquerdo na faixa. Devolve verdadeiro se tratou o clique.</summary>
    public bool SceneLeftClick(float xDip, float yDip, HitTarget target)
    {
        Touch();
        if (target.Kind == HitKind.TdCrate) return false; // o App coleta
        if (target.Kind == HitKind.TdTower && !(BuildActive && _moveTowerId == 0))
        {
            OpenInspector(target.TowerId);
            return true;
        }
        if (BuildActive)
        {
            // Clique no passo do ponto (ou novo ponto durante a escolha): valida e avança.
            UpdateGhost(xDip);
            if (!_ghostValid) { Changed?.Invoke(); return true; }
            _anchorX = xDip;
            if (_moveTowerId > 0)
            {
                _send(new MoveTower(_moveTowerId, _ghostX));
                Close();
            }
            else
            {
                _chosenX = _ghostX;
                _notice = null;
                Changed?.Invoke();
            }
            return true;
        }
        // Fora do modo construção: clique num ponto livre abre o popover de torres ali (RF-28.20 passo 2/3).
        if (Mode == TdUiMode.None || Mode == TdUiMode.Inspector || Mode == TdUiMode.Preview)
        {
            EnterBuild();
            UpdateGhost(xDip);
            if (_ghostValid)
            {
                _anchorX = xDip;
                _chosenX = _ghostX;
                _notice = null;
            }
            else
            {
                Close(); // ponto inválido fora do modo construção: nada acontece
                return true;
            }
            Changed?.Invoke();
            return true;
        }
        Close();
        return true;
    }

    public void SceneRightClick()
    {
        if (PopoverOpen) Close();
    }

    /// <summary>ESC / clique direito / ocultação da cena / 30 s: cancela sem tocar no jogo (RF-28.20 passo 5).</summary>
    public void Cancel()
    {
        if (!PopoverOpen && _selectedTowerId == 0) return;
        Close();
    }

    public void Hover(TdUiItem? item)
    {
        int def = -1;
        if (Mode == TdUiMode.Build && item is { Action: TdUiAction.ChooseTower, SArg: { } id })
        {
            var towers = _match().Content.Towers;
            for (int i = 0; i < towers.Length; i++) if (towers[i].Id == id) def = i;
        }
        if (def == _hoverDef) return;
        _hoverDef = def;
        Changed?.Invoke();
    }

    /// <summary>Clique num botão do HUD ou do popover.</summary>
    public void Activate(in TdUiItem item)
    {
        if (!item.Clickable) return;
        Touch();
        switch (item.Action)
        {
            case TdUiAction.Close: Close(); break;
            case TdUiAction.BuildToggle:
                if (BuildActive) Close(); else EnterBuild();
                break;
            case TdUiAction.ChooseTower when _chosenX >= 0 && item.SArg is not null:
                _send(new BuildTower(item.SArg, _chosenX));
                Close();
                break;
            case TdUiAction.StartWave:
                _send(new TdWaveRequest());
                Changed?.Invoke();
                break;
            case TdUiAction.Ability when item.SArg is not null:
                _send(new UseAbility(item.SArg));
                Changed?.Invoke();
                break;
            case TdUiAction.Menu: Toggle(TdUiMode.Menu); break;
            case TdUiAction.Upgrade: _send(new UpgradeTower(item.Arg)); Changed?.Invoke(); break;
            case TdUiAction.Branch when item.SArg is not null: _send(new ChooseBranch(item.Arg, item.SArg)); Changed?.Invoke(); break;
            case TdUiAction.Targeting when Enum.TryParse<TdTargeting>(item.SArg, out var mode):
                _send(new SetTargeting(item.Arg, mode));
                Changed?.Invoke();
                break;
            case TdUiAction.Sell: _send(new SellTower(item.Arg)); Close(); break;
            case TdUiAction.Reposition:
                EnterBuild();
                _moveTowerId = item.Arg;
                Changed?.Invoke();
                break;
            case TdUiAction.Preview: Toggle(TdUiMode.Preview); break;
            case TdUiAction.MapOpen: SetMode(TdUiMode.Map); break;
            case TdUiAction.MapTab: _mapSeals = item.Arg == 1; Changed?.Invoke(); break;
            case TdUiAction.SelectStage: _host.StartStage(item.Arg); Close(); break;
            case TdUiAction.BuyNode when item.SArg is not null: _host.PurchaseNode(item.SArg); Changed?.Invoke(); break;
            case TdUiAction.Pause: _host.TogglePause(); Close(); break;
            case TdUiAction.Settings: Close(); _host.OpenSettings(); break;
            case TdUiAction.Exit: _host.RequestExit(); break;
            case TdUiAction.Restart: _host.StartStage(_match().StageId); Close(); break;
            case TdUiAction.NextStage: _host.StartStage(_match().StageId + 1); Close(); break;
        }
    }

    /// <summary>Chamado a cada despertar da UI: fim de partida, validade da escolha, inspetor e os 30 s do modo construção.</summary>
    public void Sync()
    {
        var m = _match();
        if (!ReferenceEquals(m, _lastMatch))
        {
            bool first = _lastMatch is null;
            _lastMatch = m;
            if (first) goto Checked;
            _endShown = false;
            _selectedTowerId = 0;
            if (Mode != TdUiMode.None) { ResetBuild(); Mode = TdUiMode.None; }
            Changed?.Invoke();
        }
    Checked:
        if (m.Phase is MatchPhase.Vitoria or MatchPhase.Derrota)
        {
            if (!_endShown) { _endShown = true; ResetBuild(); SetMode(TdUiMode.End); }
            return;
        }
        if (Mode == TdUiMode.Build)
        {
            if (_chosenX >= 0 && _moveTowerId == 0)
            {
                // Posição ficou inválida (LayoutChanged, torre nova): volta ao passo do ponto com aviso (RF-28.20 passo 6).
                var reason = m.ValidatePlacement(_chosenX, 0, out _);
                if (reason != CommandReason.None || m.Towers.Count >= m.MaxTowers)
                {
                    _chosenX = -1;
                    _notice = "Posicao ficou invalida: escolha outro ponto";
                    Changed?.Invoke();
                }
            }
            if (_now() - _lastActionAt > BuildTimeoutSeconds) Close();
        }
        else if (Mode == TdUiMode.Inspector && FindTower(m, _selectedTowerId) < 0) Close();
    }

    // ================================================================ estado

    private void Touch() => _lastActionAt = _now();

    private void EnterBuild()
    {
        ResetBuild();
        Mode = TdUiMode.Build;
        _selectedTowerId = 0;
        Touch();
        Changed?.Invoke();
    }

    private void ResetBuild()
    {
        _chosenX = -1;
        _moveTowerId = 0;
        _ghostX = -1;
        _ghostValid = false;
        _hoverDef = -1;
        _notice = null;
    }

    private void Close()
    {
        ResetBuild();
        Mode = TdUiMode.None;
        _selectedTowerId = 0;
        Changed?.Invoke();
    }

    private void SetMode(TdUiMode mode)
    {
        ResetBuild();
        Mode = mode;
        if (mode != TdUiMode.Inspector) _selectedTowerId = 0;
        Touch();
        Changed?.Invoke();
    }

    private void Toggle(TdUiMode mode) => SetMode(Mode == mode ? TdUiMode.None : mode);

    private void OpenInspector(int towerId)
    {
        SetMode(TdUiMode.Inspector);
        _selectedTowerId = towerId;
        Changed?.Invoke();
    }

    private void UpdateGhost(float xDip)
    {
        var m = _match();
        int w = TdBand.WidthDip(m);
        int logical = TdPlacement.ToLogical((int)MathF.Round(Math.Clamp(xDip, 0f, w)), w);
        var reason = m.ValidatePlacement(logical, _moveTowerId, out int snapped);
        bool room = _moveTowerId > 0 || m.Towers.Count < m.MaxTowers;
        _ghostValid = reason == CommandReason.None && room;
        _ghostX = reason == CommandReason.None ? snapped : logical;
        _notice = reason switch
        {
            CommandReason.None when !room => "Limite de torres atingido",
            CommandReason.None => null,
            CommandReason.Overlap => "Ponto ocupado por outra torre",
            _ => "Fora da zona livre",
        };
    }

    private static int FindTower(TdMatchState m, int id)
    {
        for (int i = 0; i < m.Towers.Count; i++) if (m.Towers[i].Id == id) return i;
        return -1;
    }

    // ================================================================ HUD

    public TdHudView BuildHud()
    {
        var m = _match();
        var items = new List<TdUiItem>(10);
        float w = TdHudView.DefaultWidth;
        int integ = m.Integrity, max = m.IntegrityMax;
        var integTone = integ * 2 > max ? TdTone.Good : integ * 4 > max ? TdTone.Warn : TdTone.Bad;
        items.Add(new TdUiItem(new DipRect(2, 2, 76, 14), TdUiKind.Label, $"INT {integ}/{max}", Tone: integTone));
        items.Add(new TdUiItem(new DipRect(80, 2, 78, 14), TdUiKind.Label, $"$ {m.Credits}", Tone: TdTone.Accent));
        int shown = m.Phase == MatchPhase.Onda || m.NextWaveNumber == 0 ? m.WaveNumber : m.NextWaveNumber;
        items.Add(new TdUiItem(new DipRect(2, 18, 66, 18), TdUiKind.Label, $"ONDA {Math.Clamp(shown, 1, Math.Max(1, m.TotalWaves))}/{m.TotalWaves}"));

        var (startText, startRight, startEnabled, startTone) = StartButton(m);
        items.Add(new TdUiItem(new DipRect(70, 18, 88, 18), TdUiKind.Button, startText, TdUiAction.StartWave, Right: startRight, Enabled: startEnabled, Tone: startTone));

        var abilities = m.AbilityStatuses;
        for (int i = 0; i < 3; i++)
        {
            var rect = new DipRect(2 + i * 30, 40, 28, 20);
            if (i >= abilities.Count) { items.Add(new TdUiItem(rect, TdUiKind.Button, "--", Enabled: false, Tone: TdTone.Dim)); continue; }
            var st = abilities[i];
            var def = m.Content.Abilities[i];
            string label = AbilityShort(st.Id);
            string text = !st.Unlocked ? "--" : st.ActiveTicks > 0 ? "ON" : st.CooldownTicks > 0 ? Math.Ceiling(st.CooldownTicks / 30.0).ToString("0", System.Globalization.CultureInfo.InvariantCulture) : label;
            bool ready = st.Unlocked && st.CooldownTicks == 0 && st.UsesLeft != 0 && m.Phase == MatchPhase.Onda;
            float fill = def.CooldownSeconds > 0 && st.CooldownTicks > 0 ? Math.Clamp(st.CooldownTicks / (float)(def.CooldownSeconds * 30.0), 0f, 1f) : 0f;
            items.Add(new TdUiItem(rect, TdUiKind.Button, text, TdUiAction.Ability, SArg: st.Id, Enabled: ready,
                Tone: !st.Unlocked ? TdTone.Dim : st.ActiveTicks > 0 ? TdTone.Good : TdTone.Normal, Fill: fill, Sub: label));
        }
        items.Add(new TdUiItem(new DipRect(94, 40, 38, 20), TdUiKind.Button, "TORRE", TdUiAction.BuildToggle,
            Selected: BuildActive, Enabled: m.Phase is MatchPhase.Preparo or MatchPhase.Onda));
        items.Add(new TdUiItem(new DipRect(134, 40, 24, 20), TdUiKind.Button, "...", TdUiAction.Menu, Selected: Mode == TdUiMode.Menu));
        return new TdHudView([.. items], w, TdHudView.DefaultHeight);
    }

    private static string AbilityShort(string id) => id switch { "scale" => "ESC", "firewall" => "FW", "snapshot" => "SNP", _ => id.Length > 3 ? id[..3].ToUpperInvariant() : id.ToUpperInvariant() };

    private static (string Text, string? Right, bool Enabled, TdTone Tone) StartButton(TdMatchState m)
    {
        var rules = m.Content.Stages.Rules;
        switch (m.Phase)
        {
            case MatchPhase.Preparo when m.NextWaveNumber > 0:
            {
                long bonus = 0;
                if (m.CountdownRemainingSeconds > 0)
                    bonus = (long)Math.Ceiling(m.CountdownRemainingSeconds * (rules.CallNext.BonusBase + rules.CallNext.BonusPerWave * m.NextWaveNumber) * (1 - 1e-12));
                return ("> INICIAR", bonus > 0 ? $"+{bonus}" : null, true, TdTone.Good);
            }
            case MatchPhase.Onda when m.NextWaveNumber > 0:
            {
                foreach (var a in m.ActiveWaves) if (!a.SpawnsDone) return ("ONDA...", null, false, TdTone.Dim);
                int w = m.NextWaveNumber;
                double eta = m.EtaSeconds();
                long bonus = (long)Math.Ceiling(eta * (rules.CallNext.BonusBase + rules.CallNext.BonusPerWave * w) * (1 - 1e-12));
                long cap = (long)Math.Ceiling(rules.CallNext.BonusCapFactor * m.WaveBonusFor(w) * (1 - 1e-12));
                if (bonus > cap) bonus = cap;
                return ("CHAMAR", bonus > 0 ? $"+{bonus}" : null, true, TdTone.Accent);
            }
            case MatchPhase.Onda: return ("ULTIMA", null, false, TdTone.Dim);
            default: return ("FIM", null, false, TdTone.Dim);
        }
    }

    // ================================================================ popover

    /// <summary>Modelo do popover atual (null = fechado). A janela posiciona acima da faixa em <see cref="TdPopoverView.AnchorX"/>.</summary>
    public TdPopoverView? BuildPopover()
    {
        var m = _match();
        return Mode switch
        {
            TdUiMode.Build => BuildChooser(m),
            TdUiMode.Inspector => BuildInspector(m),
            TdUiMode.Preview => BuildPreview(m),
            TdUiMode.Menu => BuildMenu(m),
            TdUiMode.Map => BuildMap(m),
            TdUiMode.End => BuildEnd(m),
            _ => null,
        };
    }

    private sealed class Stack(float width)
    {
        public readonly List<TdUiItem> Items = [];
        public float Y = HeaderH + Pad;
        public float Width = width;

        public void Add(TdUiKind kind, string text, float h, TdUiAction action = TdUiAction.None, int arg = 0, string? sarg = null,
            string? right = null, string? sub = null, bool enabled = true, bool selected = false, TdTone tone = TdTone.Normal)
        {
            Items.Add(new TdUiItem(new DipRect(Pad, Y, Width - 2 * Pad, h), kind, text, action, arg, sarg, right, sub, enabled, selected, tone));
            Y += h + 2;
        }
    }

    private static TdPopoverView Finish(string title, Stack s, float anchorX, string? iconTower = null)
    {
        var items = new List<TdUiItem>(s.Items.Count + 1)
        {
            new(new DipRect(s.Width - HeaderH - 2, 2, HeaderH, HeaderH - 4), TdUiKind.Button, "X", TdUiAction.Close),
        };
        items.AddRange(s.Items);
        return new TdPopoverView(title, [.. items], s.Width, s.Y + Pad, anchorX, iconTower);
    }

    private static string TraitText(string[] traits)
    {
        var parts = new List<string>(4);
        foreach (var t in traits)
        {
            string text = t switch { "flying" => "voador", "armored" => "blindado", "regen" => "regenera", "drain" => "drena", "boss" => "chefe", "fast" => "rapido", "swarm" => "enxame", "durable" => "duravel", "basic" => "", _ => t };
            if (text.Length > 0) parts.Add(text);
        }
        return string.Join(", ", parts);
    }

    private static long Ceil(double v) => v <= 0 ? 0 : (long)Math.Ceiling(v * (1.0 - 1e-12));

    private TdPopoverView BuildChooser(TdMatchState m)
    {
        var s = new Stack(PopoverWidth);
        string title = _moveTowerId > 0 ? "Reposicionar torre" : _chosenX < 0 ? "Construir: escolha o ponto" : "Construir: escolha a torre";
        if (_chosenX < 0)
        {
            s.Add(TdUiKind.Label, _moveTowerId > 0 ? "Clique num ponto livre da faixa." : "Clique num ponto livre da faixa para construir.", RowH, tone: TdTone.Normal);
            s.Add(TdUiKind.Label, "ESC ou botao direito cancela.", RowH - 4, tone: TdTone.Dim);
            if (_notice is not null) s.Add(TdUiKind.Label, _notice, RowH - 4, tone: TdTone.Warn);
            return Finish(title, s, _anchorX);
        }
        s.Add(TdUiKind.Label, $"Torres {m.Towers.Count}/{m.MaxTowers}   Creditos {m.Credits}", RowH - 4, tone: TdTone.Dim);
        var towers = m.Content.Towers;
        for (int i = 0; i < towers.Length; i++)
        {
            var d = towers[i];
            bool locked = m.StageId < d.UnlockStage;
            bool poor = m.Credits < d.Cost;
            string right = locked ? $"fase {d.UnlockStage}" : $"${d.Cost}";
            string sub = locked ? $"Desbloqueia na fase {d.UnlockStage}" : $"alc {d.Range:0}  |  Fraqueza: {d.WeaknessText}";
            s.Add(TdUiKind.Button, d.Name, 44f, TdUiAction.ChooseTower, sarg: d.Id, right: right, sub: sub, enabled: !locked && !poor,
                tone: locked ? TdTone.Dim : poor ? TdTone.Bad : TdTone.Normal);
        }
        if (_notice is not null) s.Add(TdUiKind.Label, _notice, RowH - 4, tone: TdTone.Warn);
        return Finish("Construir: escolha a torre", s, _anchorX);
    }

    private TdPopoverView BuildInspector(TdMatchState m)
    {
        int ti = FindTower(m, _selectedTowerId);
        var s = new Stack(PopoverWidth);
        if (ti < 0) return Finish("Torre", s, _anchorX);
        var t = m.Towers[ti];
        var d = m.Content.Towers[t.DefIndex];
        string branch = t.Branch > 0 ? $" ramo {d.Branches[t.Branch - 1].Id}" : "";
        string title = $"{d.Name} nivel {t.Level}{branch}";
        s.Add(TdUiKind.Label, $"Alcance {d.Range:0}", RowH - 4, tone: TdTone.Dim);
        s.Add(TdUiKind.Label, $"Fraqueza: {d.WeaknessText}", TwoLineH - 8, tone: TdTone.Warn);
        if (!t.Placed)
            s.Add(TdUiKind.Button, "SEM POSICAO: reposicionar", RowH, TdUiAction.Reposition, arg: t.Id, tone: TdTone.Warn);

        // Evolução: níveis lineares, ramo A/B no nível 3 e capstone no 4.
        int linear = d.Levels.Length;
        if (t.Level < linear)
        {
            long cost = Ceil(d.Cost * d.Levels[t.Level].CostFactor);
            s.Add(TdUiKind.Button, $"Melhorar para nivel {t.Level + 1}", RowH + 4, TdUiAction.Upgrade, arg: t.Id, right: $"${cost}", enabled: m.Credits >= cost && m.Phase is MatchPhase.Preparo or MatchPhase.Onda);
        }
        else if (t.Branch == 0 && t.Level == linear)
        {
            for (int b = 0; b < d.Branches.Length; b++)
            {
                var br = d.Branches[b];
                long cost = Ceil(d.Cost * br.CostFactor);
                s.Add(TdUiKind.Button, $"Ramo {br.Id}: {br.Name}", 44f, TdUiAction.Branch, arg: t.Id, sarg: br.Id, right: $"${cost}",
                    sub: $"{br.Gain}  Fraqueza: {br.Weakness}", enabled: m.Credits >= cost);
            }
        }
        else if (t.Branch > 0 && t.Level == linear + 1)
        {
            var cap = d.Branches[t.Branch - 1].Capstone;
            long cost = Ceil(d.Cost * cap.CostFactor);
            s.Add(TdUiKind.Button, $"Capstone: {cap.Name}", 44f, TdUiAction.Upgrade, arg: t.Id, right: $"${cost}", sub: cap.Effect, enabled: m.Credits >= cost);
        }
        else s.Add(TdUiKind.Label, "Nivel maximo.", RowH - 4, tone: TdTone.Good);

        if (d.Kind == "attack")
        {
            s.Add(TdUiKind.Header, "Mira", RowH - 6);
            float x = Pad, bw = (PopoverWidth - 2 * Pad - 8) / 5f;
            for (int k = 0; k < 5; k++)
            {
                s.Items.Add(new TdUiItem(new DipRect(x, s.Y, bw, RowH), TdUiKind.Button, TargetLabels[k], TdUiAction.Targeting, t.Id,
                    ((TdTargeting)k).ToString(), Selected: t.Targeting == k));
                x += bw + 2;
            }
            s.Y += RowH + 2;
        }
        long refund = (long)Math.Floor(t.Invested * m.Content.Stages.Rules.SellRefund + 1e-9);
        s.Add(TdUiKind.Button, $"Vender ({Math.Round(m.Content.Stages.Rules.SellRefund * 100):0}%)", RowH, TdUiAction.Sell, arg: t.Id, right: $"+${refund}", tone: TdTone.Bad);
        return Finish(title, s, t.Placed ? m.TowerDip(t) : _anchorX, d.Id);
    }

    private static TdPopoverView BuildPreview(TdMatchState m)
    {
        var s = new Stack(PopoverWidth);
        int next = m.NextWaveNumber;
        if (next == 0)
        {
            s.Add(TdUiKind.Label, "Todas as ondas ja foram iniciadas.", RowH);
            return Finish("Proxima onda", s, 0);
        }
        var waves = m.Content.Waves.Stages.FirstOrDefault(x => x.Stage == m.StageId)?.Waves;
        var wave = waves?.FirstOrDefault(x => x.Number == next);
        string title = $"Onda {next}/{m.TotalWaves}{(wave?.Rest == true ? " (respiro)" : "")}";
        if (wave is null) { s.Add(TdUiKind.Label, "Sem dados da onda.", RowH); return Finish(title, s, 0); }
        s.Add(TdUiKind.Label, $"Bonus ao concluir: +${m.WaveBonusFor(next)}", RowH - 4, tone: TdTone.Accent);
        foreach (var g in wave.Groups)
        {
            string name = g.Enemy, traits = "";
            bool boss = false;
            var e = m.Content.Enemies.Enemies.FirstOrDefault(x => x.Id == g.Enemy);
            if (e is not null) { name = e.Name; traits = TraitText(e.Traits); }
            else if (m.Content.Enemies.Bosses.FirstOrDefault(x => x.Id == g.Enemy) is { } b) { name = b.Name; traits = TraitText(b.Traits); boss = true; }
            s.Add(TdUiKind.Label, $"{g.Count}x {name}", RowH - 2, right: traits.Length > 0 ? traits : null, tone: boss ? TdTone.Bad : TdTone.Normal);
        }
        return Finish(title, s, 0);
    }

    private TdPopoverView BuildMenu(TdMatchState m)
    {
        var s = new Stack(190f);
        s.Add(TdUiKind.Button, "Proxima onda", RowH, TdUiAction.Preview);
        s.Add(TdUiKind.Button, "Mapa de fases e selos", RowH, TdUiAction.MapOpen);
        s.Add(TdUiKind.Button, _host.Paused ? "Retomar" : "Pausar", RowH, TdUiAction.Pause);
        s.Add(TdUiKind.Button, "Configuracoes", RowH, TdUiAction.Settings);
        s.Add(TdUiKind.Button, "Sair", RowH, TdUiAction.Exit, tone: TdTone.Bad);
        return Finish("Menu", s, 0);
    }

    private TdPopoverView BuildMap(TdMatchState m)
    {
        var s = new Stack(PopoverWidth);
        float half = (PopoverWidth - 2 * Pad - 2) / 2f;
        s.Items.Add(new TdUiItem(new DipRect(Pad, s.Y, half, RowH), TdUiKind.Button, "Fases", TdUiAction.MapTab, 0, Selected: !_mapSeals));
        s.Items.Add(new TdUiItem(new DipRect(Pad + half + 2, s.Y, half, RowH), TdUiKind.Button, "Selos", TdUiAction.MapTab, 1, Selected: _mapSeals));
        s.Y += RowH + 4;
        if (!_mapSeals)
        {
            foreach (var st in m.Content.Stages.Stages)
            {
                bool open = _host.StageUnlocked(st.Id);
                int stars = _host.StageStars(st.Id);
                string right = open ? new string('*', stars) + new string('-', 3 - stars) : "bloqueada";
                s.Add(TdUiKind.Button, $"{st.Id}. {st.Title}", RowH + 2, TdUiAction.SelectStage, st.Id, right: right, sub: null, enabled: open,
                    selected: st.Id == m.StageId, tone: open ? TdTone.Normal : TdTone.Dim);
            }
            s.Add(TdUiKind.Label, "Clicar numa fase reinicia a partida.", RowH - 4, tone: TdTone.Dim);
        }
        else
        {
            s.Add(TdUiKind.Label, $"Selos disponiveis: {_host.SealsAvailable}  (valem na proxima partida)", RowH - 4, tone: TdTone.Accent);
            foreach (var b in m.Content.Meta.Branches)
            {
                s.Add(TdUiKind.Header, b.Name, RowH - 6);
                foreach (var n in b.Nodes)
                {
                    bool bought = _host.NodePurchased(n.Id);
                    s.Add(TdUiKind.Button, n.Name, RowH - 2, TdUiAction.BuyNode, sarg: n.Id, right: bought ? "COMPRADO" : $"{n.Cost} selo{(n.Cost > 1 ? "s" : "")}",
                        enabled: !bought && _host.SealsAvailable >= n.Cost, tone: bought ? TdTone.Good : TdTone.Normal);
                }
            }
        }
        return Finish(_mapSeals ? "Selos: arvore de melhorias" : "Mapa de fases", s, 0);
    }

    private TdPopoverView BuildEnd(TdMatchState m)
    {
        var s = new Stack(220f);
        bool won = m.Phase == MatchPhase.Vitoria;
        string title = won ? $"VITORIA  {new string('*', m.Stars)}{new string('-', 3 - m.Stars)}" : "DERROTA";
        s.Add(TdUiKind.Label, won ? $"Fase {m.StageId} concluida. Integridade perdida: {m.IntegrityLost}" : $"O nucleo caiu na onda {m.WaveNumber}/{m.TotalWaves}.", TwoLineH - 8, tone: won ? TdTone.Good : TdTone.Bad);
        s.Add(TdUiKind.Button, "Repetir fase", RowH + 2, TdUiAction.Restart);
        if (won && m.StageId < _host.StageCount && _host.StageUnlocked(m.StageId + 1))
            s.Add(TdUiKind.Button, "Proxima fase", RowH + 2, TdUiAction.NextStage, tone: TdTone.Good);
        s.Add(TdUiKind.Button, "Mapa e selos", RowH + 2, TdUiAction.MapOpen);
        return Finish(title, s, 0);
    }
}
