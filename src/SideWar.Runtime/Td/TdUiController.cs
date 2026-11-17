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
    private bool _confirmRestart;
    public bool QuickTowersOpen { get; private set; }
    private bool _endCorner;
    /// <summary>Cartão automático de fim de fase no cantinho (sem modal, sem foco); clicar no chip abre a modal central.</summary>
    public bool EndCardOpen => Mode == TdUiMode.End && _endCorner;
    /// <summary>TD.28b: aviso discreto "Fase N ✓" ao concluir uma fase da corrida; some sozinho em <see cref="StageNoticeSeconds"/> s, sem foco, sem pausa.</summary>
    public const double StageNoticeSeconds = 4.0;
    private string? _noticeText;
    private double _noticeUntil;
    private int _seenStages = -1;
    private bool _pinnedOpen;
    /// <summary>TD.28b: atalho global "minimizar tudo" ativo: nenhum cartão, painel ou modal é aberto e a pergunta-bônus espera.</summary>
    public bool BossHidden { get; private set; }
    private bool PanelPinned => _host.GameOptions.PanelPinned;
    private bool PinnedIdle => QuickTowersOpen && PanelPinned && !BuildActive;
    public bool NoticeOpen => _noticeText is not null && !BossHidden && _now() < _noticeUntil;
    public string? NoticeText => NoticeOpen ? _noticeText : null;
    /// <summary>Qualquer popover pequeno ancorado ao chip: painel rápido, pergunta-bônus, aviso de fase ou cartão de fim. Nunca ativa janela.</summary>
    public bool CornerPopover => QuickTowersOpen || EndCardOpen || NoticeOpen || ((Mode == TdUiMode.None || QuickTowersOpen) && QuizActive);
    private int _chosenX = -1;       // X lógico já escolhido (passo "escolher a torre"); -1 = ainda no passo do ponto
    private int _moveTowerId;        // > 0: reposicionando uma torre SEM POSIÇÃO (sem escolha de tipo)
    private int _ghostX = -1;
    private bool _ghostValid;
    private int _hoverDef = -1;
    private int _selectedTowerId;
    private string? _notice;
    private string? _placementType;
    private int _modalCount;
    public int ModalCount => _modalCount;
    public bool ModalOpen => !QuickTowersOpen && !BuildActive && Mode != TdUiMode.None && !EndCardOpen;
    private bool _mapSeals;
    private int _towerPage;
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
    public bool BuildActive => Mode == TdUiMode.Build && (_placementType is not null || _moveTowerId > 0);

    /// <summary>Algum popover aberto (o ESC fecha). O painel de torres fixo (TD.28b) em repouso não conta: não rouba o ESC dos outros apps.</summary>
    public bool PopoverOpen => !PinnedIdle && (QuickTowersOpen || Mode != TdUiMode.None || QuizActive);

    /// <summary>Passo do ponto: o fantasma segue o mouse e o clique escolhe o ponto.</summary>
    public bool ChoosingPoint => BuildActive && _chosenX < 0;

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
            SetMode(TdUiMode.Towers);
        }
        else
        {
            _send(new BuildTower(_placementType!, _ghostX));
            if (QuickTowersOpen) { ResetBuild(); Changed?.Invoke(); }
            else SetMode(TdUiMode.Build);
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
        if (BuildActive) return;
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
            case TdUiAction.QuickTowers:
                if (QuickTowersOpen) Close();
                else if (!ModalOpen && _match().Phase is not (MatchPhase.Vitoria or MatchPhase.Derrota)) OpenQuickTowers();
                break;
            case TdUiAction.BuildToggle:
                if (BuildActive) Close(); else SetMode(TdUiMode.Build);
                break;
            case TdUiAction.ChooseTower when item.SArg is not null:
                _placementType = item.SArg;
                _hoverDef = Array.FindIndex(_match().Content.Towers, t => t.Id == _placementType);
                UpdateModalPause();
                Changed?.Invoke();
                break;
            case TdUiAction.StartWave when !ModalOpen:
                _send(new TdWaveRequest());
                Changed?.Invoke();
                break;
            case TdUiAction.ResumeAbility when item.SArg is not null:
                Close();
                _send(new UseAbility(item.SArg));
                Changed?.Invoke(); break;
            case TdUiAction.Ability when !ModalOpen && item.SArg is not null:
                _send(new UseAbility(item.SArg));
                Changed?.Invoke();
                break;
            case TdUiAction.Menu: Toggle(TdUiMode.Menu); break;
            case TdUiAction.HudMinimize: Close(); _host.SetHudMinimized(true); Changed?.Invoke(); break;
            case TdUiAction.HudRestore: SetMode(_match().Phase is MatchPhase.Vitoria or MatchPhase.Derrota ? TdUiMode.End : TdUiMode.Build); break;
            case TdUiAction.ModalTab: SetMode((TdUiMode)item.Arg); break;
            case TdUiAction.Targeting when Enum.TryParse<TdTargeting>(item.SArg, out var mode):
                _send(new SetTargeting(item.Arg, mode));
                Changed?.Invoke();
                break;
            case TdUiAction.Sell:
                _send(new SellTower(item.Arg));
                if (Mode == TdUiMode.Towers) Changed?.Invoke(); else Close();
                break;
            case TdUiAction.CollectCrate when !ModalOpen:
                if (_match().CrateActive) _send(new CollectCrate(_match().CrateId));
                Changed?.Invoke();
                break;
            case TdUiAction.TowersOpen: SetMode(TdUiMode.Towers); break;
            case TdUiAction.GameOption:
            {
                var o = _host.GameOptions;
                var next = item.Arg switch
                {
                    0 => o with { BandHeight = o.BandHeight < 64 ? 64 : o.BandHeight < 88 ? 88 : 40 },
                    1 => o with { AutoStart = !o.AutoStart },
                    2 => o with { Sound = !o.Sound },
                    3 => o with { Volume = ((o.Volume / 20 + 1) % 6) * 20 },
                    4 => o with { Questions = !o.Questions },
                    5 => o with { Fade = !o.Fade },
                    6 => o with { Continuous = !o.Continuous },
                    7 => o with { PanelPinned = !o.PanelPinned },
                    8 => o with { GameSpeed = o.GameSpeed % 3 + 1 },
                    _ => o,
                };
                _host.SetGameOptions(next);
                Changed?.Invoke(); break;
            }
            case TdUiAction.AutoStartToggle:
                _send(new SetAutoStart(_match().AutoStart == AutoStartMode.Off ? AutoStartMode.Countdown : AutoStartMode.Off, 15));
                Changed?.Invoke(); break;
            case TdUiAction.TowerPage: _towerPage = Math.Max(0, _towerPage + item.Arg); Changed?.Invoke(); break;
            case TdUiAction.OpenTower: OpenInspector(item.Arg); break;
            case TdUiAction.Reposition:
                EnterBuild();
                _moveTowerId = item.Arg;
                UpdateModalPause();
                Changed?.Invoke();
                break;
            case TdUiAction.Preview: Toggle(TdUiMode.Preview); break;
            case TdUiAction.MapOpen: SetMode(TdUiMode.Map); break;
            case TdUiAction.MapTab: _mapSeals = item.Arg == 1; Changed?.Invoke(); break;
            case TdUiAction.SelectStage: Close(); _host.StartStage(item.Arg); break;
            case TdUiAction.BuyNode when item.SArg is not null: _host.PurchaseNode(item.SArg); Changed?.Invoke(); break;
            case TdUiAction.Pause: _host.TogglePause(); Close(); break;
            case TdUiAction.Settings: _host.OpenSettings(); break;
            case TdUiAction.Exit: _host.RequestExit(); break;
            case TdUiAction.RestartPrompt: _confirmRestart = true; Changed?.Invoke(); break;
            case TdUiAction.RestartCancel: _confirmRestart = false; Changed?.Invoke(); break;
            case TdUiAction.Restart: { int stage = _match().Options.PersistentRun ? _host.RunStartStage : _match().StageId; Close(); _host.StartStage(stage); break; }
            case TdUiAction.NextStage: { int stage = _match().StageId + 1; Close(); _host.StartStage(stage); break; }
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
            _seenStages = m.StageResults.Count;
            _noticeText = null;
            if (first) goto Checked;
            _modalCount = 0;
            _endShown = false;
            _endCorner = false;
            _selectedTowerId = 0;
            if (Mode != TdUiMode.None) { ResetBuild(); Mode = TdUiMode.None; }
            Changed?.Invoke();
        }
    Checked:
        TrackPhase(m);
        SyncNotice(m);
        SyncOnboarding(m);
        SyncQuiz(m);
        UpdateModalPause();
        if (m.Phase is MatchPhase.Vitoria or MatchPhase.Derrota)
        {
            if (!_endShown)
            {
                _endShown = true;
                _winGuide = m.Phase == MatchPhase.Vitoria && _onb is not null && !_onb.TipSeen("firstwin");
                if (_winGuide) _onb!.MarkTipSeen("firstwin");
                ResetBuild();
                SetMode(TdUiMode.End);
                _endCorner = true;
            }
            return;
        }
        MaintainPinned();
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
            if (!QuickTowersOpen && _now() - _lastActionAt > BuildTimeoutSeconds) Close();
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
        UpdateModalPause();
        _selectedTowerId = 0;
        Touch();
        Changed?.Invoke();
    }

    private void ResetBuild()
    {
        _placementType = null;
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
        QuickTowersOpen = false;
        _pinnedOpen = false;
        _noticeText = null;
        _endCorner = false;
        _confirmRestart = false;
        Mode = TdUiMode.None;
        if (QuizActive) CloseQuiz();
        UpdateModalPause();
        _selectedTowerId = 0;
        MaintainPinned();
        Changed?.Invoke();
    }

    private void OpenQuickTowers()
    {
        QuickTowersOpen = true; Mode = TdUiMode.Build; ResetBuild(); UpdateModalPause(); Changed?.Invoke();
    }

    /// <summary>TD.28b: com o painel fixo ligado, reabre o painel rápido sempre que nada mais está aberto; ao desligar fecha o que o fixo abriu.</summary>
    private void MaintainPinned()
    {
        var m = _match();
        bool want = PanelPinned && !BossHidden && m.Phase is not (MatchPhase.Vitoria or MatchPhase.Derrota);
        if (want && !QuickTowersOpen && Mode == TdUiMode.None) { OpenQuickTowers(); _pinnedOpen = true; }
        else if (!want && _pinnedOpen && QuickTowersOpen && !BuildActive) Close();
    }

    /// <summary>
    /// TD.28b (atalho global de minimizar tudo): esconde/mostra tudo. Ao esconder fecha modal, painel, cartões e pergunta (a partida segue);
    /// ao mostrar reabre o painel fixo e, se a corrida já acabou, o cartão de fim.
    /// </summary>
    public void SetBossHidden(bool hidden)
    {
        if (BossHidden == hidden) return;
        BossHidden = hidden;
        if (hidden) { Close(); return; }
        _endShown = false;
        MaintainPinned();
        Changed?.Invoke();
    }

    private void SyncNotice(TdMatchState m)
    {
        int n = m.StageResults.Count;
        if (n > _seenStages)
        {
            _seenStages = n;
            if (BossHidden) return;
            var r = m.StageResults[^1];
            var o = _host.LastOutcome;
            _noticeText = $"Fase {r.StageNumber}" + (r.Crown ? " +coroa" : "") + (o.NewRecord ? " +recorde" : "");
            _noticeUntil = _now() + StageNoticeSeconds;
            Changed?.Invoke();
        }
        else if (_noticeText is not null && _now() >= _noticeUntil)
        {
            _noticeText = null;
            Changed?.Invoke();
        }
    }

    private TdPopoverView BuildNotice()
    {
        var s = new Stack(200f);
        s.Add(TdUiKind.Label, _noticeText!, 18f, tone: TdTone.Good, icon: TdIcon.Check);
        return Finish("Fase concluida", s, 0);
    }

    private void SetMode(TdUiMode mode)
    {
        ResetBuild();
        QuickTowersOpen = false;
        _endCorner = false;
        _confirmRestart = false;
        Mode = mode;
        UpdateModalPause();
        if (mode != TdUiMode.Inspector) _selectedTowerId = 0;
        if (mode == TdUiMode.None) MaintainPinned();
        Touch();
        Changed?.Invoke();
    }

    private void UpdateModalPause()
    {
        int count = ModalOpen ? 1 : 0;
        if (_modalCount == count) return;
        bool wasOpen = _modalCount > 0;
        _modalCount = count;
        if (wasOpen != (count > 0)) _send(count > 0 ? new Pause() : new Resume());
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
        bool run = m.Options.PersistentRun;
        string stageText = run ? $"Fase {m.StageNumber}{(m.Cycle > 0 ? $"·C{m.Cycle + 1}" : "")}  " : "";
        string coreTip = "Integridade do núcleo: se chegar a 0 você perde a corrida. Inimigos que chegam ao núcleo tiram integridade."
            + (run ? " Fase N conta a corrida inteira; depois da última fase o ciclo recomeça mais forte (C2, C3…)." : "")
            + $" Esconder tudo: {_host.BossHotkey}.";

        bool needed = _onb is not null && (HudNeeded(m) || ComputeStep(m) == TdTutorialStep.HudOpen);
        items.Add(new TdUiItem(new DipRect(0, 0, 300, 28), TdUiKind.Button,
            $"{integ}/{max}  {stageText}Onda {shown}/{m.TotalWaves}  ¢{m.Credits}{(ModalOpen ? " ⏸" : "")}",
            TdUiAction.HudRestore, Icon: TdIcon.Heart, Tone: integTone, Tip: coreTip, RightIcon: TdIcon.Expand, Highlight: (needed || m.CrateActive) && PulseOn));
        items.Add(new TdUiItem(new DipRect(304, 0, 26, 28), TdUiKind.Button, "", TdUiAction.QuickTowers,
            Enabled: !ModalOpen, Icon: TdIcon.Menu, Tip: PanelPinned ? "Painel de torres fixo (Jogo ou Configurações)" : "Torres", Selected: QuickTowersOpen));
        // TD.28c: velocidade do jogo (1x/2x/3x): um clique alterna; não muda com a pausa da modal.
        int speed = Math.Clamp(_host.GameOptions.GameSpeed, 1, 3);
        items.Add(new TdUiItem(new DipRect(334, 0, 30, 28), TdUiKind.Button, $"{speed}x", TdUiAction.GameOption, 8,
            Enabled: !ModalOpen, Tone: speed > 1 ? TdTone.Accent : TdTone.Normal, Tip: $"Velocidade do jogo: {speed}x (clique para alternar 1x/2x/3x)."));
        float crateX = 368;
        // TD.28b: com ondas contínuas o botão Iniciar onda só existe no Preparo inicial; depois as ondas seguem sozinhas.
        if (!m.Options.ContinuousWaves || m.Phase == MatchPhase.Preparo)
        {
            var (text, right, enabled, tone, tip) = StartButton(m);
            items.Add(new TdUiItem(new DipRect(368, 0, 126, 28), TdUiKind.Button, text, TdUiAction.StartWave,
                Right: right, Enabled: enabled && !StartBlockedFor(m) && !ModalOpen, Tone: tone, Tip: StartBlockedFor(m) ? "Coloque 1 torre primeiro" : tip, Icon: m.Phase == MatchPhase.Onda ? TdIcon.FastForward : TdIcon.Play, RightCoin: right is not null));
            crateX = 498;
        }
        items.Add(new TdUiItem(new DipRect(crateX, 0, 116, 28), TdUiKind.Button, "Coletar caixa", TdUiAction.CollectCrate,
            Enabled: m.CrateActive && !ModalOpen, Icon: TdIcon.Box, Highlight: m.CrateActive && PulseOn, Tip: "Coletar caixa: Ctrl+Alt+C"));
        return new TdHudView(HighlightHud(items), crateX + 116, 28, Minimized: true, Wave: shown, TotalWaves: m.TotalWaves);
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
        if (BossHidden) return null;
        if (!BuildActive && (Mode == TdUiMode.None || QuickTowersOpen) && _quizQ is not null && QuizPhaseOk(m)) return BuildQuiz(m);
        if (!BuildActive && NoticeOpen && (Mode == TdUiMode.None || QuickTowersOpen)) return BuildNotice();
        if (QuickTowersOpen) return BuildQuickTowers(m);
        if (BuildActive) return null;
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
            TdUiMode.Runes => Finish("Runas", Placeholder(), 0),
            _ => null,
        };
        return view is null ? null : EndCardOpen ? DecoratePopover(m, view) : Modalize(DecoratePopover(m, view));
    }

    private TdPopoverView BuildQuickTowers(TdMatchState m)
    {
        var items = new List<TdUiItem>();
        for (int i = 0; i < m.Content.Towers.Length; i++)
        {
            var d = m.Content.Towers[i];
            long price = m.NextTowerCost(i);
            items.Add(new TdUiItem(new DipRect(8 + i * 80, 38, 76, 88), TdUiKind.Button, d.Name,
                TdUiAction.ChooseTower, SArg: d.Id, Right: $"${price}", Enabled: m.StageId >= d.UnlockStage && m.Credits >= price,
                Selected: _placementType == d.Id));
        }
        return new TdPopoverView(BuildActive ? "TORRES   Toque na faixa" : "TORRES   Escolha uma torre", [.. items], 416, 136, 0);
    }

    private static Stack Placeholder()
    {
        var s = new Stack(PopoverWidth);
        s.Add(TdUiKind.Label, "Runas chegam em breve", RowH);
        s.Add(TdUiKind.Label, "Esta aba ainda não está disponível. Volte em uma próxima atualização.", TwoLineH);
        s.Add(TdUiKind.Label, "Em breve", RowH, tone: TdTone.Accent);
        return s;
    }

    /// <summary>Composição do Main.dc.html para Runas, Torres, Jogo e Como jogar (modal 720×480, conteúdo a partir de y=100).</summary>
    private void RelayoutTab(TdPopoverView view, List<TdUiItem> items)
    {
        TdUiItem Put(TdUiItem it, float x, float y, float w, float h) => it with { Rect = new DipRect(x, y, w, h) };
        var src = view.Items;
        foreach (var it in src) if (it.Action == TdUiAction.Close) items.Add(Put(it, 674, 4, 36, 36));
        switch (Mode)
        {
            case TdUiMode.Runes:
                items.Add(new(new DipRect(160, 262, 400, 26), TdUiKind.Label, "Runas chegam em breve", Icon: TdIcon.Lock, SArg: "c18"));
                items.Add(new(new DipRect(160, 296, 400, 20), TdUiKind.Label, "Esta aba ainda não está disponível. Volte em uma próxima atualização.", SArg: "c13"));
                items.Add(new(new DipRect(300, 328, 120, 24), TdUiKind.Label, "Em breve", SArg: "badge"));
                break;
            case TdUiMode.Help:
            {
                int n = 0;
                foreach (var it in src)
                {
                    if (it.Action == TdUiAction.Close) continue;
                    if (it.Action == TdUiAction.RedoTutorial) { items.Add(Put(it, 16, 400, 180, 40)); continue; }
                    int row = n / 2, col = n % 2; n++;
                    items.Add(Put(it, 16 + col * 352, 108 + row * 36, 336, 30));
                }
                break;
            }
            case TdUiMode.Towers:
            {
                var m = _match();
                items.Add(new(new DipRect(16, 104, 400, 18), TdUiKind.Label, $"Torres em campo: {m.Towers.Count}", Tone: TdTone.Dim, SArg: "hdr"));
                items.Add(new(new DipRect(500, 104, 204, 18), TdUiKind.Label, $"Venda devolve {Math.Round(m.Content.Stages.Rules.SellRefund * 100):0}%", Tone: TdTone.Dim, SArg: "hdrR"));
                if (m.Towers.Count == 0)
                {
                    items.Add(new(new DipRect(16, 132, 688, 320), TdUiKind.Label, "", SArg: "empty"));
                    items.Add(new(new DipRect(160, 268, 400, 26), TdUiKind.Label, "Nenhuma torre em campo", Icon: TdIcon.Build, SArg: "c18"));
                    items.Add(new(new DipRect(160, 302, 400, 20), TdUiKind.Label, "Construa uma torre para gerenciá-la aqui. Você pode vender a qualquer momento.", SArg: "c13"));
                    items.Add(new(new DipRect(260, 336, 200, 40), TdUiKind.Button, "Ir para Construir", TdUiAction.ModalTab, (int)TdUiMode.Build, SArg: "primary"));
                    break;
                }
                int row = 0;
                foreach (var it in src)
                {
                    if (it.Action is TdUiAction.Close or TdUiAction.None) continue;
                    float y = 132 + row * 52;
                    if (it.Action == TdUiAction.OpenTower) items.Add(Put(it, 16, y, 560, 46));
                    else if (it.Action == TdUiAction.Sell) { items.Add(Put(it, 584, y, 120, 46)); row++; }
                    else if (it.Action == TdUiAction.Reposition) items.Add(Put(it, 330, y + 6, 240, 34));
                    else items.Add(Put(it, 16 + (it.Arg > 0 ? 360 : 0), 132 + 8 * 52 + 4, 336, 34));
                }
                break;
            }
            case TdUiMode.Menu:
            {
                float lx = 16, rx = 368, w = 336;
                int ab = 0, opt = 0;
                foreach (var it in src)
                {
                    switch (it.Action)
                    {
                        case TdUiAction.None when it.Kind == TdUiKind.Bar: items.Add(Put(it, lx, 104, w, 64)); break;
                        case TdUiAction.ResumeAbility: items.Add(Put(it, lx + ab++ * 114, 196, 108, 56)); break;
                        case TdUiAction.Preview: items.Add(Put(it, lx, 282, 165, 40)); break;
                        case TdUiAction.TowersOpen: items.Add(Put(it, lx + 171, 282, 165, 40)); break;
                        case TdUiAction.MapOpen: items.Add(Put(it, lx, 328, 165, 40)); break;
                        case TdUiAction.HowTo: items.Add(Put(it, lx + 171, 328, 165, 40)); break;
                        case TdUiAction.RestartPrompt: items.Add(Put(it, lx, 376, w, 40)); break;
                        case TdUiAction.Restart: items.Add(Put(it, lx, 376, 165, 40)); break;
                        case TdUiAction.None when it.Text == "Reiniciar?": items.Add(Put(it, lx, 358, w, 14) with { SArg = "sec", Tone = TdTone.Warn }); break;
                        case TdUiAction.RestartCancel: items.Add(Put(it, lx + 171, 376, 165, 40)); break;
                        case TdUiAction.GameOption: items.Add(Put(it, rx, 122 + opt++ * 34, w, 32)); break;
                        case TdUiAction.Exit: items.Add(Put(it, rx, 402, w, 40)); break;
                    }
                }
                items.Add(new(new DipRect(lx, 178, w, 14), TdUiKind.Label, "HABILIDADES", SArg: "sec"));
                items.Add(new(new DipRect(lx, 264, w, 14), TdUiKind.Label, "AÇÕES", SArg: "sec"));
                items.Add(new(new DipRect(rx, 104, w, 14), TdUiKind.Label, "CONFIGURAÇÕES", SArg: "sec"));
                break;
            }
        }
    }

    private TdPopoverView? Modalize(TdPopoverView? view)
    {
        if (view is null) return null;
        var items = new List<TdUiItem>();
        var modes = new[] { TdUiMode.Build, TdUiMode.Runes, TdUiMode.Towers, TdUiMode.Menu, TdUiMode.Help };
        string[] labels = ["Construir", "Runas", "Torres", "Jogo", "Como jogar"];
        for (int i = 0; i < modes.Length; i++)
            items.Add(new(new DipRect(16 + i * 136, 52, 132, 36), TdUiKind.Button, labels[i],
                TdUiAction.ModalTab, (int)modes[i], Selected: Mode == modes[i]));
        float factor = Math.Min(1f, 402f / Math.Max(1, view.Height));
        int towerRow = 0;
        if (Mode is TdUiMode.Menu or TdUiMode.Towers or TdUiMode.Help or TdUiMode.Runes)
        {
            RelayoutTab(view, items);
            return new TdPopoverView($"{view.Title}   ¢{_match().Credits}", [.. items], 720, 480, 0);
        }
        foreach (var item in view.Items)
        {
            var r = item.Rect;
            if (item.Action == TdUiAction.Close)
            {
                items.Add(item with { Rect = new DipRect(674, 4, 36, 36) });
                continue;
            }
            if (Mode == TdUiMode.Build && item.Kind == TdUiKind.Label && towerRow == 0)
            {
                items.Add(item with { Rect = new DipRect(16, 103, 688, 18) });
                continue;
            }
            if (item.Action == TdUiAction.ChooseTower)
            {
                items.Add(item with { Rect = new DipRect(16, 130 + towerRow++ * 64, 688, 58) });
                continue;
            }
            items.Add(item with { Rect = new DipRect(r.X * 2.2f + 20, 96 + r.Y * factor * .92f, r.W * 2.2f, r.H * factor * .92f) });
        }
        return new TdPopoverView($"{view.Title}   ¢{_match().Credits}", [.. items], 720, 480, 0);
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
        if (BuildActive)
        {
            s.Add(TdUiKind.Label, _moveTowerId > 0 ? "Clique num ponto livre da faixa." : "Clique num ponto livre da faixa para construir.", RowH, tone: TdTone.Normal);
            s.Add(TdUiKind.Label, "ESC ou botao direito cancela.", RowH - 4, tone: TdTone.Dim);
            if (_notice is not null) s.Add(TdUiKind.Label, _notice, RowH - 4, tone: TdTone.Warn);
            return Finish(title, s, _anchorX);
        }
        s.Add(TdUiKind.Label, "Escolha uma torre e toque na faixa para posicionar.", RowH - 4, tone: TdTone.Dim);
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
    private TdPopoverView BuildTowers(TdMatchState m)
    {
        var s = new Stack(PopoverWidth);
        s.Add(TdUiKind.Label, $"Torres {m.Towers.Count}", RowH - 4, tone: TdTone.Dim);
        if (m.Towers.Count == 0) s.Add(TdUiKind.Label, "Nenhuma torre. Use Construir.", RowH, tone: TdTone.Dim);
        float sellW = 98f;
        _towerPage = Math.Min(_towerPage, Math.Max(0, (m.Towers.Count - 1) / 8));
        for (int i = _towerPage * 8; i < Math.Min(m.Towers.Count, (_towerPage + 1) * 8); i++)
        {
            var t = m.Towers[i];
            var d = m.Content.Towers[t.DefIndex];
            long refund = m.SellValue(t.Id);
            s.Items.Add(new TdUiItem(new DipRect(Pad, s.Y, PopoverWidth - 3 * Pad - sellW, RowH + 2), TdUiKind.Button,
                $"{d.Name} x{m.TowerCount(t.DefIndex)}", TdUiAction.OpenTower, t.Id, SArg: d.Id, Tone: t.Placed ? TdTone.Normal : TdTone.Warn));
            s.Items.Add(new TdUiItem(new DipRect(PopoverWidth - Pad - sellW, s.Y, sellW, RowH + 2), TdUiKind.Button,
                "Vender", TdUiAction.Sell, t.Id, Right: $"+${refund}", Tone: TdTone.Bad));
            s.Y += RowH + 4;
            if (!t.Placed) s.Add(TdUiKind.Button, "SEM POSICAO: reposicionar", RowH - 2, TdUiAction.Reposition, arg: t.Id, tone: TdTone.Warn);
        }
        if (m.Towers.Count > 8)
        {
            s.Add(TdUiKind.Button, "Anterior", RowH, TdUiAction.TowerPage, -1, enabled: _towerPage > 0);
            s.Add(TdUiKind.Button, "Próximas", RowH, TdUiAction.TowerPage, 1, enabled: (_towerPage + 1) * 8 < m.Towers.Count);
        }
        return Finish("Torres", s, 0);
    }

    private TdPopoverView BuildMenu(TdMatchState m)
    {
        var s = new Stack(260f);
        AddRankBar(s, _host.Rank);
        foreach (var ability in m.AbilityStatuses)
        {
            bool ready = m.Phase == MatchPhase.Onda && ability.Unlocked && ability.CooldownTicks == 0 && ability.UsesLeft != 0;
            s.Add(TdUiKind.Button, AbilityShort(ability.Id), RowH, TdUiAction.ResumeAbility, sarg: ability.Id,
                enabled: ready, right: ready ? "Usar e fechar" : "Pausado");
        }
        if (_confirmRestart)
        {
            s.Add(TdUiKind.Label, "Reiniciar?", RowH, tone: TdTone.Warn);
            s.Add(TdUiKind.Button, "Sim", RowH, TdUiAction.Restart, tone: TdTone.Bad);
            s.Add(TdUiKind.Button, "Nao", RowH, TdUiAction.RestartCancel);
        }
        else s.Add(TdUiKind.Button, m.Options.PersistentRun ? "Reiniciar corrida" : "Reiniciar fase", RowH, TdUiAction.RestartPrompt);
        s.Add(TdUiKind.Button, "Como jogar", RowH, TdUiAction.HowTo, tone: TdTone.Accent);
        s.Add(TdUiKind.Button, "Torres (vender)", RowH, TdUiAction.TowersOpen);
        s.Add(TdUiKind.Button, "Proxima onda", RowH, TdUiAction.Preview);
        s.Add(TdUiKind.Button, "Mapa de fases e selos", RowH, TdUiAction.MapOpen);

        var options = _host.GameOptions;
        s.Add(TdUiKind.Button, $"Faixa: {options.BandHeight} DIP", RowH, TdUiAction.GameOption, 0);
        s.Add(TdUiKind.Button, $"Velocidade: {Math.Clamp(options.GameSpeed, 1, 3)}x", RowH, TdUiAction.GameOption, 8);
        s.Add(TdUiKind.Button, options.AutoStart ? "Auto-início: ligado" : "Auto-início: desligado", RowH, TdUiAction.GameOption, 1);
        s.Add(TdUiKind.Button, options.Sound ? "Som: ligado" : "Som: desligado", RowH, TdUiAction.GameOption, 2);
        s.Add(TdUiKind.Button, $"Volume: {options.Volume}%", RowH, TdUiAction.GameOption, 3);
        s.Add(TdUiKind.Button, options.Questions ? "Perguntas: ligadas" : "Perguntas: desligadas", RowH, TdUiAction.GameOption, 4);
        s.Add(TdUiKind.Button, options.Fade ? "Esmaecer: ligado" : "Esmaecer: desligado", RowH, TdUiAction.GameOption, 5);
        s.Add(TdUiKind.Button, options.Continuous ? "Ondas contínuas: ligado" : "Ondas contínuas: desligado", RowH, TdUiAction.GameOption, 6, right: "Próxima corrida");
        s.Add(TdUiKind.Button, options.PanelPinned ? "Painel de torres fixo: ligado" : "Painel de torres fixo: desligado", RowH, TdUiAction.GameOption, 7);
        s.Add(TdUiKind.Button, "Sair", RowH, TdUiAction.Exit, tone: TdTone.Bad);
        return Finish("Jogo", s, 0);
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
        s.Add(TdUiKind.Label, won ? $"Fase {m.StageId} concluida. Integridade perdida: {m.IntegrityLost}"
            : m.Options.PersistentRun ? $"Corrida encerrada na fase {m.StageNumber}, onda {m.WaveNumber}/{m.TotalWaves}." : $"O nucleo caiu na onda {m.WaveNumber}/{m.TotalWaves}.", TwoLineH - 8, tone: won ? TdTone.Good : TdTone.Bad);
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
        if (won && m.StageId < _host.StageCount && _host.StageUnlocked(m.StageId + 1))
            s.Add(TdUiKind.Button, "Proxima fase", 38, TdUiAction.NextStage, tone: TdTone.Good);
        s.Add(TdUiKind.Button, won ? "Jogar de novo" : m.Options.PersistentRun ? "Reiniciar" : "Reiniciar fase", 38, TdUiAction.Restart);
        s.Add(TdUiKind.Button, "Mapa", 38, TdUiAction.MapOpen);
        return Finish(title, s, 0);
    }
}
