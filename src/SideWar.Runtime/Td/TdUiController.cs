using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using BuildTower = SideWar.Core.Td.BuildTower;
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
public sealed partial class TdUiController
{
    public const float PopoverWidth = 300f, Pad = 6f, RowH = 20f, TwoLineH = 34f, HeaderH = 22f, BadgeH = 14f;
    /// <summary>TD.16b: o modo posicionar (faixa aceita clique) sai sozinho após este tempo sem ação.</summary>
    public const double BuildTimeoutSeconds = 15.0;

    private static readonly string[] TargetLabels = ["Primeiro", "Ultimo", "Forte", "Fraco", "Proximo"];

    private readonly Func<TdMatchState> _match;
    private readonly Action<IGameCommand> _send;
    private readonly ITdUiHost _host;
    private readonly Func<double> _now;
    private readonly ITdOnboardingStore? _onb;

    private TdMatchState? _lastMatch;
    private bool _endShown;
    private bool _winGuide;
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

    public TdUiController(Func<TdMatchState> match, Action<IGameCommand> send, ITdUiHost host, Func<double>? now = null,
        ITdOnboardingStore? onboarding = null, TdQuestionPicker? quiz = null)
    {
        _onb = onboarding;
        _quiz = quiz;
        _match = match ?? throw new ArgumentNullException(nameof(match));
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _now = now ?? (() => 0.0);
    }

    public TdUiMode Mode { get; private set; }

    /// <summary>
    /// Modo posicionar ativo (TD.16b; passo do ponto ou da escolha): é o ÚNICO momento em que a faixa aceita clique (fora dele ela é
    /// click-through e o mouse vai para o app de baixo); o ESC fica registrado.
    /// </summary>
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
        PlacingMode = BuildActive,
        BandHint = Step == TdTutorialStep.Point,
        HintX = HintX(),
    };

    /// <summary>Move o mouse sobre a faixa (X em DIP): só importa no passo do ponto.</summary>
    public void PointerMoved(float xDip)
    {
        if (!ChoosingPoint) return;
        UpdateGhost(xDip);
        Changed?.Invoke();
    }

    /// <summary>
    /// Botão esquerdo na faixa. TD.16b: só existe no modo posicionar (a janela é click-through fora dele); fora do modo devolve falso e não
    /// faz nada (não há inspetor por clique, nem popover ao clicar num ponto livre, nem coleta da caixa na faixa).
    /// </summary>
    public bool SceneLeftClick(float xDip, float yDip, HitTarget target)
    {
        if (!BuildActive) return false;
        Touch();
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
        NoteAction(item.Action);
        switch (item.Action)
        {
            case TdUiAction.TutorialSkip: SkipTutorial(); break;
            case TdUiAction.HowTo: Toggle(TdUiMode.Help); break;
            case TdUiAction.RedoTutorial: RedoTutorial(); break;
            case TdUiAction.TutorialNext: break;
            case TdUiAction.QuizTrue: AnswerQuiz(true); break;
            case TdUiAction.QuizFalse: AnswerQuiz(false); break;
            case TdUiAction.QuizClose: CloseQuiz(); break;
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
            case TdUiAction.HudMinimize: Close(); _host.SetHudMinimized(true); Changed?.Invoke(); break;
            case TdUiAction.HudRestore: _host.SetHudMinimized(false); Changed?.Invoke(); break;
            case TdUiAction.Targeting when Enum.TryParse<TdTargeting>(item.SArg, out var mode):
                _send(new SetTargeting(item.Arg, mode));
                Changed?.Invoke();
                break;
            case TdUiAction.Sell:
                _send(new SellTower(item.Arg));
                if (Mode == TdUiMode.Towers) Changed?.Invoke(); else Close();
                break;
            case TdUiAction.CollectCrate:
                if (_match().CrateActive) _send(new CollectCrate(_match().CrateId));
                Changed?.Invoke();
                break;
            case TdUiAction.TowersOpen: SetMode(TdUiMode.Towers); break;
            case TdUiAction.OpenTower: OpenInspector(item.Arg); break;
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
        TrackPhase(m);
        SyncOnboarding(m);
        SyncQuiz(m);
        if (m.Phase is MatchPhase.Vitoria or MatchPhase.Derrota)
        {
            if (!_endShown)
            {
                _endShown = true;
                _winGuide = m.Phase == MatchPhase.Vitoria && _onb is not null && !_onb.TipSeen("firstwin");
                if (_winGuide) _onb!.MarkTipSeen("firstwin");
                ResetBuild();
                SetMode(TdUiMode.End);
            }
            return;
        }
        if (Mode == TdUiMode.Build)
        {
            if (_chosenX >= 0 && _moveTowerId == 0)
            {
                // Posição ficou inválida (LayoutChanged, torre nova): volta ao passo do ponto com aviso (RF-28.20 passo 6).
                var reason = m.ValidatePlacement(_chosenX, 0, out _);
                if (reason != CommandReason.None || m.Towers.Count >= TdMatchState.TechnicalTowerLimit)
                {
                    _chosenX = -1;
                    _notice = "Posicao ficou invalida: escolha outro ponto";
                    Changed?.Invoke();
                }
            }
            if (_now() - _lastActionAt > BuildTimeoutSeconds) Close();
        }
        else if (Mode == TdUiMode.Inspector && FindTower(m, _selectedTowerId) < 0) SetMode(TdUiMode.Towers);
    }

    // ================================================================ estado

    private void Touch()
    {
        _lastActionAt = _now();
        DismissTip();
    }

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
        bool room = _moveTowerId > 0 || m.Towers.Count < TdMatchState.TechnicalTowerLimit;
        _ghostValid = reason == CommandReason.None && room;
        _ghostX = reason == CommandReason.None ? snapped : logical;
        _notice = reason switch
        {
            CommandReason.None when !room => "Limite tecnico de torres",
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
        var items = new List<TdUiItem>(14);
        int integ = m.Integrity, max = m.IntegrityMax;
        var integTone = integ * 2 > max ? TdTone.Good : integ * 4 > max ? TdTone.Warn : TdTone.Bad;
        int shown = m.Phase == MatchPhase.Onda || m.NextWaveNumber == 0 ? m.WaveNumber : m.NextWaveNumber;
        shown = Math.Clamp(shown, 1, Math.Max(1, m.TotalWaves));
        string coreTip = "Integridade do núcleo: se chegar a 0 você perde a fase. Inimigos que chegam ao núcleo tiram integridade.";

        if (_host.HudMinimized)
        {
            bool needed = _onb is not null && (HudNeeded(m) || ComputeStep(m) == TdTutorialStep.HudOpen);
            bool crateChip = m.CrateActive; // TD.16b: o chip pisca enquanto há caixa (abra o HUD ou use o atalho)
            items.Add(new TdUiItem(new DipRect(0, 0, TdHudView.ChipWidth, TdHudView.ChipHeight), TdUiKind.Button,
                $"{integ}/{max}  Onda {shown}/{m.TotalWaves}", TdUiAction.HudRestore, Icon: TdIcon.Heart, Tone: integTone,
                Tip: crateChip ? "Caixa-bomba ativa! Abra o HUD ou use o atalho." : "Clique para abrir", RightIcon: TdIcon.Expand,
                Right: crateChip ? "Caixa!" : needed ? "Abrir HUD" : null, Highlight: (needed || crateChip) && PulseOn));
            return new TdHudView([.. items], TdHudView.ChipWidth, TdHudView.ChipHeight, Minimized: true);
        }

        items.Add(new TdUiItem(new DipRect(4, 3, 104, 26), TdUiKind.Label, $"Núcleo {integ}/{max}", Icon: TdIcon.Heart, Tone: integTone, Tip: coreTip));
        items.Add(new TdUiItem(new DipRect(112, 3, 92, 26), TdUiKind.Label, $"Créditos {m.Credits}", Icon: TdIcon.Coin, Tone: TdTone.Accent,
            Tip: "Créditos: moeda da partida. Use para construir torres (cada igual custa mais); vender devolve parte. Cada onda concluída rende bônus."));
        items.Add(new TdUiItem(new DipRect(208, 3, 100, 26), TdUiKind.Label, $"Onda {shown} de {m.TotalWaves}", Icon: TdIcon.Wave,
            Tip: $"Onda atual: {shown} de {m.TotalWaves}. Sobreviva a todas as ondas da fase para vencer."));
        bool crate = m.CrateActive;
        items.Add(new TdUiItem(new DipRect(312, 3, 116, 26), TdUiKind.Button, crate ? "Coletar caixa" : "Caixa", TdUiAction.CollectCrate, Icon: TdIcon.Box,
            Enabled: crate, Tone: crate ? TdTone.Warn : TdTone.Dim, Highlight: crate && PulseOn,
            Tip: crate ? "Caixa-bomba ativa: colete antes que ela exploda suas torres. Também funciona pelo atalho (Configurações > Atalhos)."
                : "Caixa-bomba: quando aparecer, este botão pisca. Colete pelo botão ou pelo atalho (Configurações > Atalhos)."));
        items.Add(new TdUiItem(new DipRect(432, 3, 24, 26), TdUiKind.Button, "", TdUiAction.HudMinimize, Icon: TdIcon.Collapse,
            Tip: "Recolher"));

        var (startText, startRight, startEnabled, startTone, startTip) = StartButton(m);
        if (startEnabled && StartBlockedFor(m)) (startEnabled, startTone, startTip, startRight) = (false, TdTone.Dim, "Coloque 1 torre primeiro", null);
        items.Add(new TdUiItem(new DipRect(4, 33, 138, 28), TdUiKind.Button, startText, TdUiAction.StartWave, Right: startRight, Enabled: startEnabled,
            Tone: startTone, Icon: m.Phase == MatchPhase.Onda ? TdIcon.FastForward : TdIcon.Play, RightCoin: startRight is not null, Tip: startTip));

        var abilities = m.AbilityStatuses;
        for (int i = 0; i < 3; i++)
        {
            var rect = new DipRect(146 + i * 68, 33, 64, 28);
            if (i >= abilities.Count) { items.Add(new TdUiItem(rect, TdUiKind.Button, "--", Enabled: false, Tone: TdTone.Dim, Icon: TdIcon.Lock)); continue; }
            var st = abilities[i];
            var def = m.Content.Abilities[i];
            string label = AbilityShort(st.Id);
            var icon = st.Unlocked ? AbilityIcon(st.Id) : TdIcon.Lock;
            string text = !st.Unlocked ? label : st.ActiveTicks > 0 ? "ATIVA" : st.CooldownTicks > 0 ? Math.Ceiling(st.CooldownTicks / 30.0).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " s" : label;
            bool ready = st.Unlocked && st.CooldownTicks == 0 && st.UsesLeft != 0 && m.Phase == MatchPhase.Onda;
            float fill = def.CooldownSeconds > 0 && st.CooldownTicks > 0 ? Math.Clamp(st.CooldownTicks / (float)(def.CooldownSeconds * 30.0), 0f, 1f) : 0f;
            string unlock = def.UnlockStage is int us ? $"desbloqueia na fase {us}" : "desbloqueia na árvore de selos (Mapa e selos)";
            string tip = !st.Unlocked ? $"{def.Name}: bloqueada, {unlock}. {def.Effect}"
                : $"{def.Name}: {def.Effect} Recarga {def.CooldownSeconds:0} s." + (st.CooldownTicks > 0 ? " Recarregando." : st.UsesLeft == 0 ? " Sem usos nesta fase." : m.Phase != MatchPhase.Onda ? " Só durante a onda." : " Pronta: clique para usar.");
            items.Add(new TdUiItem(rect, TdUiKind.Button, text, TdUiAction.Ability, SArg: st.Id, Enabled: ready,
                Tone: !st.Unlocked ? TdTone.Dim : st.ActiveTicks > 0 ? TdTone.Good : TdTone.Normal, Fill: fill, Sub: label, Icon: icon, Tip: tip));
        }
        items.Add(new TdUiItem(new DipRect(350, 33, 74, 28), TdUiKind.Button, BuildActive ? "Cancelar" : "Construir", TdUiAction.BuildToggle,
            Icon: BuildActive ? TdIcon.Cross : TdIcon.Build, Selected: BuildActive, Enabled: m.Phase is MatchPhase.Preparo or MatchPhase.Onda,
            Tip: BuildActive ? "Cancelar: sai do modo posicionar e a faixa volta a deixar seus cliques passarem. ESC e botão direito também cancelam."
                : "Construir torre: a faixa passa a aceitar clique; escolha um ponto livre e a torre. Fora desse modo a faixa não captura o mouse."));
        items.Add(new TdUiItem(new DipRect(428, 33, 28, 28), TdUiKind.Button, "", TdUiAction.Menu, Icon: TdIcon.Menu, Selected: Mode == TdUiMode.Menu,
            Tip: "Menu: próxima onda, mapa de fases e selos, pausar, configurações e sair."));
        return new TdHudView(HighlightHud(items), TdHudView.DefaultWidth, TdHudView.DefaultHeight);
    }

    /// <summary>Dica do item do HUD sob o ponto (null = sem dica). A janela do tooltip posiciona acima do item.</summary>
    public static TdTooltipView? TooltipFor(TdHudView hud, int index) =>
        index >= 0 && index < hud.Items.Length && hud.Items[index].Tip is { } tip
            ? new TdTooltipView(tip, hud.Items[index].Rect.X + hud.Items[index].Rect.W / 2f, hud.Items[index].Rect.Y)
            : null;

    private static string AbilityShort(string id) => id switch { "scale" => "Escalar", "firewall" => "Firewall", "snapshot" => "Snapshot", _ => id };

    private static TdIcon AbilityIcon(string id) => id switch { "scale" => TdIcon.Scale, "firewall" => TdIcon.Firewall, "snapshot" => TdIcon.Snapshot, _ => TdIcon.Scale };

    private static (string Text, string? Right, bool Enabled, TdTone Tone, string Tip) StartButton(TdMatchState m)
    {
        var rules = m.Content.Stages.Rules;
        switch (m.Phase)
        {
            case MatchPhase.Preparo when m.NextWaveNumber > 0:
            {
                long bonus = 0;
                if (m.CountdownRemainingSeconds > 0)
                    bonus = (long)Math.Ceiling(m.CountdownRemainingSeconds * (rules.CallNext.BonusBase + rules.CallNext.BonusPerWave * m.NextWaveNumber) * (1 - 1e-12));
                return ("Iniciar onda", bonus > 0 ? $"+{bonus}" : null, true, TdTone.Good,
                    "Iniciar onda: começa a próxima onda de inimigos (atalho Ctrl+Alt+Espaço)." + (bonus > 0 ? $" Iniciar antes da contagem rende +{bonus} créditos." : ""));
            }
            case MatchPhase.Onda when m.NextWaveNumber > 0:
            {
                foreach (var a in m.ActiveWaves) if (!a.SpawnsDone) return ("Onda chegando", null, false, TdTone.Dim, "A onda ainda está surgindo; só dá para chamar a próxima depois que todos os inimigos aparecerem.");
                int w = m.NextWaveNumber;
                double eta = m.EtaSeconds();
                long bonus = (long)Math.Ceiling(eta * (rules.CallNext.BonusBase + rules.CallNext.BonusPerWave * w) * (1 - 1e-12));
                long cap = (long)Math.Ceiling(rules.CallNext.BonusCapFactor * m.WaveBonusFor(w) * (1 - 1e-12));
                if (bonus > cap) bonus = cap;
                return ("Chamar próxima", bonus > 0 ? $"+{bonus}" : null, true, TdTone.Accent,
                    $"Chamar próxima onda agora, antes do tempo: rende +{bonus} créditos de bônus, mas os inimigos chegam juntos.");
            }
            case MatchPhase.Onda: return ("Última onda", null, false, TdTone.Dim, "Esta é a última onda: derrote todos os inimigos para vencer.");
            default: return ("Fim da fase", null, false, TdTone.Dim, "A partida terminou.");
        }
    }

    // ================================================================ popover

    /// <summary>Modelo do popover atual (null = fechado). A janela posiciona acima da faixa em <see cref="TdPopoverView.AnchorX"/>.</summary>
    public TdPopoverView? BuildPopover()
    {
        var m = _match();
        if (Mode == TdUiMode.None && _quizQ is not null && m.Phase == MatchPhase.Preparo) return BuildQuiz(m);
        var view = Mode switch
        {
            TdUiMode.Build => BuildChooser(m),
            TdUiMode.Inspector => BuildInspector(m),
            TdUiMode.Preview => BuildPreview(m),
            TdUiMode.Menu => BuildMenu(m),
            TdUiMode.Towers => BuildTowers(m),
            TdUiMode.Map => BuildMap(m),
            TdUiMode.End => BuildEnd(m),
            TdUiMode.Help => BuildHelp(m),
            _ => null,
        };
        return DecoratePopover(m, view);
    }

    private sealed class Stack(float width)
    {
        public readonly List<TdUiItem> Items = [];
        public float Y = HeaderH + Pad;
        public float Width = width;

        public void Add(TdUiKind kind, string text, float h, TdUiAction action = TdUiAction.None, int arg = 0, string? sarg = null,
            string? right = null, string? sub = null, bool enabled = true, bool selected = false, TdTone tone = TdTone.Normal, TdIcon icon = TdIcon.None)
        {
            Items.Add(new TdUiItem(new DipRect(Pad, Y, Width - 2 * Pad, h), kind, text, action, arg, sarg, right, sub, enabled, selected, tone, Icon: icon));
            Y += h + 2;
        }
    }

    private static void AddBadges(Stack s, bool crown, int medals, string? right)
    {
        s.Items.Add(new TdUiItem(new DipRect(Pad, s.Y, s.Width - 2 * Pad, BadgeH), TdUiKind.Badges, "", Arg: medals, Selected: crown, Right: right));
        s.Y += BadgeH + 2;
    }

    private static void AddRankBar(Stack s, TdRankProgress r)
    {
        string right = r.Next is { } n ? $"{r.Total}/{r.NextThreshold} p. > {TdRank.DisplayName(n)}" : $"{r.Total} p.";
        s.Items.Add(new TdUiItem(new DipRect(Pad, s.Y, s.Width - 2 * Pad, RowH - 2), TdUiKind.Bar, $"Patente: {TdRank.DisplayName(r.Current)}", Right: right,
            Tone: TdTone.Accent, Fill: (float)r.Fraction));
        s.Y += RowH;
    }

    private static string MedalNames(TdMedals m)
    {
        var parts = new List<string>(4);
        if (m.HasFlag(TdMedals.SemVender)) parts.Add("Sem vender");
        if (m.HasFlag(TdMedals.SemHabilidades)) parts.Add("Sem habilidades");
        if (m.HasFlag(TdMedals.Pressa)) parts.Add("Pressa");
        if (m.HasFlag(TdMedals.Desafio)) parts.Add("Desafio");
        return string.Join(", ", parts);
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
        s.Add(TdUiKind.Label, $"Torres {m.Towers.Count}   Creditos {m.Credits}", RowH - 4, tone: TdTone.Dim);
        var towers = m.Content.Towers;
        for (int i = 0; i < towers.Length; i++)
        {
            var d = towers[i];
            bool locked = m.StageId < d.UnlockStage;
            int owned = m.TowerCount(i);
            long price = m.NextTowerCost(i); // custo crescente por tipo (SDD-29 §5.1)
            bool poor = m.Credits < price;
            string right = locked ? $"fase {d.UnlockStage}" : $"${price}";
            string sub = locked ? $"Desbloqueia na fase {d.UnlockStage}" : $"alc {d.Range:0}  |  Fraqueza: {d.WeaknessText}";
            s.Add(TdUiKind.Button, owned > 0 ? $"{d.Name}  x{owned}" : d.Name, 44f, TdUiAction.ChooseTower, sarg: d.Id, right: right, sub: sub, enabled: !locked && !poor,
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
        string title = $"{d.Name} x{m.TowerCount(t.DefIndex)}";
        s.Add(TdUiKind.Label, $"Alcance {d.Range:0}", RowH - 4, tone: TdTone.Dim);
        s.Add(TdUiKind.Label, $"Fraqueza: {d.WeaknessText}", TwoLineH - 8, tone: TdTone.Warn);
        if (!t.Placed)
            s.Add(TdUiKind.Button, "SEM POSICAO: reposicionar", RowH, TdUiAction.Reposition, arg: t.Id, tone: TdTone.Warn);

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
        long refund = m.SellValue(t.Id); // fungivel: taxa x custo(tipo, n-1) (SDD-29 §5.2)
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

    /// <summary>
    /// TD.16b: acesso às torres sem clicar na faixa (click-through): lista cada torre com "Vender" (reembolso) e abre o inspetor pelo nome;
    /// torre SEM POSIÇÃO ganha "Reposicionar" (modo posicionar). Simples de propósito: a modal TD.24a (SDD-29) substitui.
    /// </summary>
    private static TdPopoverView BuildTowers(TdMatchState m)
    {
        var s = new Stack(PopoverWidth);
        s.Add(TdUiKind.Label, $"Torres {m.Towers.Count}", RowH - 4, tone: TdTone.Dim);
        if (m.Towers.Count == 0) s.Add(TdUiKind.Label, "Nenhuma torre. Use Construir.", RowH, tone: TdTone.Dim);
        float sellW = 98f;
        for (int i = 0; i < m.Towers.Count; i++)
        {
            var t = m.Towers[i];
            var d = m.Content.Towers[t.DefIndex];
            long refund = m.SellValue(t.Id);
            s.Items.Add(new TdUiItem(new DipRect(Pad, s.Y, PopoverWidth - 3 * Pad - sellW, RowH + 2), TdUiKind.Button,
                $"{d.Name} x{m.TowerCount(t.DefIndex)}", TdUiAction.OpenTower, t.Id, Tone: t.Placed ? TdTone.Normal : TdTone.Warn));
            s.Items.Add(new TdUiItem(new DipRect(PopoverWidth - Pad - sellW, s.Y, sellW, RowH + 2), TdUiKind.Button,
                "Vender", TdUiAction.Sell, t.Id, Right: $"+${refund}", Tone: TdTone.Bad));
            s.Y += RowH + 4;
            if (!t.Placed) s.Add(TdUiKind.Button, "SEM POSICAO: reposicionar", RowH - 2, TdUiAction.Reposition, arg: t.Id, tone: TdTone.Warn);
        }
        return Finish("Torres", s, 0);
    }

    private TdPopoverView BuildMenu(TdMatchState m)
    {
        var s = new Stack(260f);
        AddRankBar(s, _host.Rank);
        s.Add(TdUiKind.Button, "Como jogar", RowH, TdUiAction.HowTo, tone: TdTone.Accent);
        s.Add(TdUiKind.Button, "Torres (vender)", RowH, TdUiAction.TowersOpen);
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
            AddRankBar(s, _host.Rank);
            foreach (var st in m.Content.Stages.Stages)
            {
                bool open = _host.StageUnlocked(st.Id);
                int stars = _host.StageStars(st.Id);
                string right = open ? new string('*', stars) + new string('-', 3 - stars) : "bloqueada";
                s.Add(TdUiKind.Button, $"{st.Id}. {st.Title}", RowH + 2, TdUiAction.SelectStage, st.Id, right: right, sub: null, enabled: open,
                    selected: st.Id == m.StageId, tone: open ? TdTone.Normal : TdTone.Dim);
                if (open)
                {
                    int best = _host.StageBestScore(st.Id);
                    AddBadges(s, _host.StageCrown(st.Id), _host.StageMedals(st.Id), best > 0 ? $"Recorde {best}" : "sem recorde");
                }
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
        var s = new Stack(260f);
        bool won = m.Phase == MatchPhase.Vitoria;
        var r = m.Result;
        string title = won ? $"VITORIA  {new string('*', m.Stars)}{new string('-', 3 - m.Stars)}" : "DERROTA";
        s.Add(TdUiKind.Label, won ? $"Fase {m.StageId} concluida. Integridade perdida: {m.IntegrityLost}" : $"O nucleo caiu na onda {m.WaveNumber}/{m.TotalWaves}.", TwoLineH - 8, tone: won ? TdTone.Good : TdTone.Bad);
        if (won)
        {
            var o = _host.LastOutcome;
            AddBadges(s, r.Crown, (int)r.Medals, r.Crown ? "COROA" : null);
            var earned = MedalNames(r.Medals);
            s.Add(TdUiKind.Label, earned.Length > 0 ? $"Medalhas: {earned}" : "Sem medalhas desta vez.", TwoLineH - 8, tone: TdTone.Dim);
            s.Add(TdUiKind.Label, $"Pontuacao: {r.Score}", RowH - 4, right: o.NewRecord ? "NOVO RECORDE" : $"Recorde {_host.StageBestScore(m.StageId)}",
                tone: o.NewRecord ? TdTone.Accent : TdTone.Normal);
            if (o.BonusSeals > 0) s.Add(TdUiKind.Label, $"Selos bonus: +{o.BonusSeals}", RowH - 4, tone: TdTone.Good);
            AddRankBar(s, _host.Rank);
            if (_winGuide)
            {
                s.Add(TdUiKind.Label, "Coroa: vença sem perder núcleo.", 14f, tone: TdTone.Accent, icon: TdIcon.Crown);
                s.Add(TdUiKind.Label, "Medalhas: desafios extras.", 14f, tone: TdTone.Accent, icon: TdIcon.Medal);
                s.Add(TdUiKind.Label, "Patente: soma dos recordes.", 14f, tone: TdTone.Accent, icon: TdIcon.Target);
            }
        }
        else
        {
            var (ri, reason, ti, tip) = DefeatTexts(m);
            s.Add(TdUiKind.Label, reason, 16f, tone: TdTone.Bad, icon: ri);
            s.Add(TdUiKind.Label, tip, 16f, tone: TdTone.Accent, icon: ti);
        }
        s.Add(TdUiKind.Button, "Repetir fase", RowH + 2, TdUiAction.Restart);
        if (won && m.StageId < _host.StageCount && _host.StageUnlocked(m.StageId + 1))
            s.Add(TdUiKind.Button, "Proxima fase", RowH + 2, TdUiAction.NextStage, tone: TdTone.Good);
        s.Add(TdUiKind.Button, "Mapa e selos", RowH + 2, TdUiAction.MapOpen);
        return Finish(title, s, 0);
    }
}
