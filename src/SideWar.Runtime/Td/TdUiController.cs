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
    private bool _confirmWipe;
    public bool QuickTowersOpen { get; private set; }
    private bool _endCorner;
    private bool _restartArmed, _returnToEnd;
    private double _restartRemaining, _restartLast;
    private const double RestartSeconds = 8.0;
    /// <summary>Cartão automático de fim de fase no cantinho (sem modal, sem foco); clicar no chip abre a modal central.</summary>
    public bool EndCardOpen => Mode == TdUiMode.End && _endCorner;
    /// <summary>TD.28b: aviso discreto "Fase N ✓" ao concluir uma fase da corrida; some sozinho em <see cref="StageNoticeSeconds"/> s, sem foco, sem pausa.</summary>
    public const double StageNoticeSeconds = 2.5;
    /// <summary>Aviso de promoção dura um pouco mais (3,5 s); entrada/saída suaves por opacidade (<see cref="NoticeFadeSeconds"/>).</summary>
    public const double PromoNoticeSeconds = 3.5, NoticeFadeSeconds = 0.25;
    public const float ToastH = 22f, ToastFont = 11f;
    private string? _noticeText;
    private double _noticeUntil;
    private double _noticeStarted;
    private int _seenStages = -1;
    private TdRankLevel _seenRank;
    private int _seenRankScore;
    private TdRankLevel? _noticeRank;
    /// <summary>REPUTACAO: "Base sendo atacada" aparece no máximo 1 vez a cada tantos segundos.</summary>
    public const double AttackNoticeCooldownSeconds = 8.0;
    private const string AttackTitle = "Base";
    private TdMatchState? _attackMatch;
    private long _seenLost;
    private double _attackNextAt = double.NegativeInfinity;
    private double _criticalNoticeUntil;
    private TdMatchState? _rewindMatch;
    private int _seenBossRewinds;
    private int _pendingBossRewindWave;
    private bool _pinnedOpen;
    private bool _outsideCollapsed;
    /// <summary>TD.28b: atalho global "minimizar tudo" ativo: nenhum cartão, painel ou modal é aberto e a pergunta-bônus espera.</summary>
    public bool BossHidden { get; private set; }
    /// <summary>Velocidades do seletor: 1x/2x/3x e, com "Velocidades extras" ligadas, as escolhidas pelo usuário.</summary>
    private int[] AllowedSpeeds => TdSpeed.Allowed(_host.GameOptions.ExtraSpeeds, _host.GameOptions.SpeedMask);

    /// <summary>Liga/desliga o modo sombra (botão, Configurações e atalho). Entra/sai entre ondas: em Onda o pedido fica pendente.</summary>
    public void ToggleShadow()
    {
        var m = _match();
        if (m.Phase is not (MatchPhase.Preparo or MatchPhase.Onda)) return;
        _send(new SetShadow(!(m.ShadowPending ?? m.Shadow)));
        Changed?.Invoke();
    }

    /// <summary>Atalho de teclado: cicla só as velocidades habilitadas.</summary>
    public void CycleSpeed()
    {
        var o = _host.GameOptions;
        var allowed = AllowedSpeeds;
        _host.SetGameOptions(o with { GameSpeed = TdSpeed.Next(TdSpeed.Resolve(o.GameSpeed, allowed), allowed) });
        Changed?.Invoke();
    }

    private bool PanelPinned => _host.GameOptions.PanelPinned;
    private bool PinnedIdle => QuickTowersOpen && PanelPinned && !BuildActive;
    public bool NoticeOpen => _host.GameOptions.NoticeSize != 0 && _noticeText is not null && !BossHidden && _now() < _noticeUntil;
    public string? NoticeText => NoticeOpen ? _noticeText : null;
    public void DismissNotice() { if (_noticeText is null) return; _noticeText = null; Changed?.Invoke(); }
    /// <summary>Qualquer popover pequeno ancorado ao chip: painel rápido, pergunta-bônus, aviso de fase ou cartão de fim. Nunca ativa janela.</summary>
    public bool CornerPopover => DemotionCardOpen || QuickTowersOpen || RuneWindowOpen || EndCardOpen || NoticeOpen || RuneCardOpen || AlertCardOpen || FuseCardOpen || FuseDragCardOpen || FusionTipOpen || _fusionChoosingModule || FusionTargetActive || MutationWarnOpen || ((Mode == TdUiMode.None || QuickTowersOpen) && QuizActive)
        || (_tutorial is { Active: true } && (Mode == TdUiMode.None || BuildActive));
    private int _chosenX = -1;       // X lógico já escolhido (passo "escolher a torre"); -1 = ainda no passo do ponto
    private int _moveTowerId;        // > 0: reposicionando uma torre SEM POSIÇÃO (sem escolha de tipo)
    private int _dropTowerId;       // > 0: o fantasma está sobre esta torre (preview de fusão)
    private TdFusionCheck _dropCheck;
    private int _fxTowerId;
    private double _fxStart;
    public const float FuseFxSeconds = 0.6f;
    private int _ghostX = -1;
    private bool _ghostValid;
    private int _hoverDef = -1;
    private string? _hoverEnemy;
    private int _selectedEnemy;
    private int _selectedTowerId;
    private string? _notice;
    private string? _placementType;
    private int _modalCount;
    public int ModalCount => _modalCount;
    public bool ModalOpen => !QuickTowersOpen && !BuildActive && Mode != TdUiMode.None && !EndCardOpen;
    private bool _mapSeals, _mapConfirm;
    private int _mapSel;
    /// <summary>TD.29b: pausa pedida pelo botão Pausar do HUD (Pause/Resume reais do Core); zera a cada partida nova.</summary>
    private bool _userPaused;
    private readonly Queue<string> _runePath = new();
    private string? _runePathPending;
    private double _runePathNext;
    private string? _runePathNotice;
    private double _runePathNoticeUntil;
    private string? _selectedRune;
    private string? _hoverRune;
    /// <summary>TD.30c: janela de runas do HUD (popover no cantinho, ancorado no botão Runas; nunca abre sozinha, não pausa e não toma foco).</summary>
    public bool RuneWindowOpen { get; private set; }
    /// <summary>Id do nó sob o mouse (balão e destaque); nulo quando o mouse sai.</summary>
    public string? HoverRune => _hoverRune;
    private string? _lastRune;
    private string? _hoverBranch;
    private bool _legendHint;
    private int _runeCount;
    private readonly HashSet<string> _runeKnown = [];
    private double _lastActionAt;
    private float _anchorX;

    public TdUiController(Func<TdMatchState> match, Action<IGameCommand> send, ITdUiHost host, Func<double>? now = null,
        ITdOnboardingStore? onboarding = null, TdQuestionPicker? quiz = null, Func<double>? quizDraw = null)
    {
        if (quizDraw is not null) _quizDraw = quizDraw;
        _onb = onboarding;
        _tutorial = onboarding is null ? null : new TdTutorial(onboarding);
        _quiz = quiz;
        _match = match ?? throw new ArgumentNullException(nameof(match));
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _seenRank = _host.Rank.Current;
        _seenRankScore = _host.Rank.Total;
        _now = now ?? (() => 0.0);
    }

    public TdUiMode Mode { get; private set; }

    /// <summary>
    /// Modo posicionar ativo (TD.16b; passo do ponto ou da escolha): é o ÚNICO momento em que a faixa aceita clique (fora dele ela é
    /// click-through e o mouse vai para o app de baixo); o ESC fica registrado.
    /// </summary>
    public bool BuildActive => Mode == TdUiMode.Build && (_placementType is not null || _moveTowerId > 0);

    /// <summary>Algum popover aberto (o ESC fecha). O painel de torres fixo (TD.28b) em repouso não conta: não rouba o ESC dos outros apps.</summary>
    public bool PopoverOpen => !PinnedIdle && (QuickTowersOpen || FuseCardOpen || _fusionChoosingModule || FusionTargetActive || RuneWindowOpen || Mode != TdUiMode.None || QuizActive);

    /// <summary>Passo do ponto: o fantasma segue o mouse e o clique escolhe o ponto.</summary>
    public bool ChoosingPoint => BuildActive && _chosenX < 0;

    public int SelectedTowerId => _selectedTowerId;
    public string? Notice => _notice;

    /// <summary>Notifica a apresentação (HUD/popover/faixa) de que algo mudou.</summary>
    public event Action? Changed;

    /// <summary>Define o volume pela posição no controle da aba Jogo, em passos de 5%.</summary>
    public void SetVolumeAt(TdUiItem item, float x)
    {
        if (item.Action != TdUiAction.GameOption || item.Arg != 3 || item.Rect.W <= 100) return;
        float left = item.Rect.X + 8, right = item.Rect.X + item.Rect.W - 8;
        int value = (int)Math.Round(Math.Clamp((x - left) / (right - left), 0f, 1f) * 20f, MidpointRounding.AwayFromZero) * 5;
        _host.SetGameOptions(_host.GameOptions with { Volume = value });
        Changed?.Invoke();
    }

    // ================================================================ entradas

    /// <summary>Visão da faixa: seleção, fantasma e alcance da torre candidata.</summary>
    public TdView View => new(_selectedTowerId, 0)
    {
        GhostX = BuildActive || (_dragMoved && !_dragSell) ? _ghostX : -1,
        SellRefund = DragSellRefund(),
        GhostValid = _ghostValid,
        GhostDef = _dragTowerId > 0 ? DragDef() : _hoverDef >= 0 ? _hoverDef : -1,
        ChosenX = BuildActive ? _chosenX : -1,
        PlacingMode = BuildActive,
        BandHint = Step == TdTutorialStep.Point,
        HintX = HintX(),
        FuseTargetId = BuildActive || _dragMoved ? _dropTowerId : 0,
        FuseTargetOk = _dropCheck.IsOk,
        FuseModuleId = _dropTowerId > 0 ? (_dragTowerId > 0 ? DragModuleId() : BuildActive ? _placementType : null) : null,
        FuseFxId = _fxTowerId,
        FuseFxAge = _fxTowerId > 0 ? (float)(_now() - _fxStart) : -1f,
    };

    /// <summary>Move o mouse sobre a faixa (X em DIP): só importa no passo do ponto.</summary>
    public void PointerMoved(float xDip, float yDip = 0f)
    {
        if (_dragTowerId > 0) { TowerDragMoved(xDip, yDip); return; }
        if (!ChoosingPoint) return;
        UpdateGhost(xDip);
        Changed?.Invoke();
    }

    /// <summary>
    /// Botão esquerdo na faixa. TD.16b: só existe no modo posicionar (a janela é click-through fora dele); fora do modo devolve falso e não
    /// faz nada (não há inspetor por clique, nem popover ao clicar num ponto livre).
    /// </summary>
    public bool SceneLeftClick(float xDip, float yDip, HitTarget target)
    {
        if (FusionTargetActive)
        {
            var hit = TdHitTester.Test(_match(), xDip, yDip);
            if (hit.Kind == HitKind.TdTower)
            {
                string module = _fusionTargetModule!;
                _fusionTargetModule = null;
                OpenFuse(hit.TowerId);
                _fuseType = module;
                Changed?.Invoke();
            }
            return true;
        }
        if (!BuildActive)
        {
            // Botão pressionado sobre uma torre colocada: começa o arrasto (soltar sobre outra funde; sem mover abre o inspetor).
            return TdHitTester.Test(_match(), xDip, yDip) is { Kind: HitKind.TdTower } h && BeginTowerDrag(h.TowerId, xDip);
        }
        Touch();
        // Clique no passo do ponto (ou novo ponto durante a escolha): valida e avança.
        UpdateGhost(xDip);
        if (_dropTowerId > 0)
        {
            // Soltar sobre uma torre: fusão direta (mesmo custo/regras do cartão); par inválido não faz nada.
            if (!_dropCheck.IsOk) { Changed?.Invoke(); return true; }
            DropFuse(_dropTowerId, _placementType!, _dropCheck);
            return true;
        }
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

    /// <summary>Botão esquerdo solto na cena: termina um arrasto de torre, se houver.</summary>
    public void SceneLeftUp(float xDip, float yDip = 0f) => TowerDragEnd(xDip, yDip);

    public void SceneRightClick()
    {
        if (PopoverOpen) Close();
    }

    /// <summary>ESC / clique direito / ocultação da cena / 30 s: cancela sem tocar no jogo (RF-28.20 passo 5).</summary>
    public void Cancel()
    {
        if (_captureKey is not null) { _captureKey = null; _hotkeyError = null; Changed?.Invoke(); return; }
        if (_dragTowerId > 0) { CancelTowerDrag(); return; }
        if (!PopoverOpen && _selectedTowerId == 0) return;
        Close();
    }

    public void Hover(TdUiItem? item)
    {
        string? enemy = item is { Action: TdUiAction.BestiarySelect, SArg: { } eid } ? eid : null;
        if (enemy != _hoverEnemy) { _hoverEnemy = enemy; Changed?.Invoke(); }
        string? rune = item is { Action: TdUiAction.SelectRune, SArg: { } rid } ? rid : null;
        if (rune != _hoverRune) { _hoverRune = rune; Changed?.Invoke(); }
        string? branch = item is { Action: TdUiAction.RuneBranch, SArg: { } bid } ? bid : null;
        if (branch != _hoverBranch) { _hoverBranch = branch; if (branch is not null) _legendHint = false; Changed?.Invoke(); }
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
        _outsideCollapsed = false;
        if (_restartArmed && item.Action != TdUiAction.Restart && (EndCardOpen || item.Action is TdUiAction.Menu or TdUiAction.Settings)) _restartArmed = false;
        Touch();
        NoteAction(item.Action);
        if (item.Action >= TdUiAction.SettingsOpen && ActivateSetting(item)) return;
        switch (item.Action)
        {
            case TdUiAction.TutorialSkip: SkipTutorial(); break;
            case TdUiAction.HowTo: Toggle(TdUiMode.Help); break;
            case TdUiAction.RedoTutorial: RedoTutorial(); break;
            case TdUiAction.TutorialNext: break;
            case TdUiAction.TutorialPrevious: break;
            case TdUiAction.QuizTrue: AnswerQuiz(true); break;
            case TdUiAction.QuizFalse: AnswerQuiz(false); break;
            case TdUiAction.QuizClose: CloseQuiz(); break;
            case TdUiAction.Close: Close(); break;
            case TdUiAction.ViewRunes: _returnToEnd = true; SetMode(TdUiMode.Runes); break;
            case TdUiAction.RuneWindow:
                if (RuneWindowOpen) Close();
                else if (!ModalOpen && _match().Phase is not (MatchPhase.Vitoria)) OpenRuneWindow();
                break;
            case TdUiAction.RecenterRunes: RecenterWeb(); break;
            case TdUiAction.RuneTutorialOpen:
                DismissRuneCard();
                if (!ModalOpen && !RuneWindowOpen && _match().Phase is not (MatchPhase.Vitoria)) OpenRuneWindow(fromCard: true);
                Changed?.Invoke();
                break;
            case TdUiAction.DemotionContinue: ContinueAfterDemotion(); break;
            case TdUiAction.AlertSave or TdUiAction.AlertFall or TdUiAction.AlertDismiss or TdUiAction.AlertMute: ActivateAlert(item.Action); break;
            case TdUiAction.RuneTutorialDismiss: DismissRuneCard(); Changed?.Invoke(); break;
            case TdUiAction.FuseOpen or TdUiAction.FusePick or TdUiAction.FuseConfirm or TdUiAction.FuseBack or TdUiAction.FuseClose: ActivateFuse(item); break;
            case TdUiAction.FusionTipDismiss: DismissFusionTip(); break;
            case TdUiAction.FusionTry: TryFusion(); break;
            case TdUiAction.FusionModule: OpenFusionModuleChooser(); break;
            case TdUiAction.FusionSelectModule when item.SArg is not null:
                _fusionChoosingModule = false;
                _fusionTargetModule = item.SArg;
                _selectedTowerId = _match().Towers.Where(t => CanReceiveModule(_match(), t)).Select(t => t.Id).FirstOrDefault();
                Changed?.Invoke(); break;
            case TdUiAction.MutationWarnDismiss: DismissMutationWarn(); break;
            case TdUiAction.QuickTowers:
                if (QuickTowersOpen) Close();
                else if (!ModalOpen && _match().Phase is not (MatchPhase.Vitoria)) OpenQuickTowers();
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
            case TdUiAction.HudExpand:
            case TdUiAction.HudRestore:
                if (_host.HudMinimized) { _host.SetHudMinimized(false); Changed?.Invoke(); }
                else SetMode(_match().Phase is MatchPhase.Vitoria ? TdUiMode.End : TdUiMode.Build);
                break;
            case TdUiAction.ModalTab: SetMode((TdUiMode)item.Arg); break;
            case TdUiAction.BestiarySelect: _selectedEnemy = item.Arg; Changed?.Invoke(); break;
            case TdUiAction.Targeting when Enum.TryParse<TdTargeting>(item.SArg, out var mode):
                _send(new SetTargeting(item.Arg, mode));
                Changed?.Invoke();
                break;
            case TdUiAction.Sell:
                _money.NoteRefund(); // o reembolso não é ganho (sem "+N" nem renda)
                _send(new SellTower(item.Arg));
                if (Mode == TdUiMode.Towers) Changed?.Invoke(); else Close();
                break;
            case TdUiAction.TowersOpen: SetMode(TdUiMode.Towers); break;
            case TdUiAction.PauseToggle when !ModalOpen && !_demotionPending:
                if (_match().Phase is MatchPhase.Preparo or MatchPhase.Onda)
                {
                    _userPaused = !_userPaused;
                    _send(_userPaused ? new Pause() : new Resume());
                }
                Changed?.Invoke(); break;
            case TdUiAction.SetSpeed when !ModalOpen || Mode == TdUiMode.Menu:
                _host.SetGameOptions(_host.GameOptions with { GameSpeed = TdSpeed.Resolve(item.Arg, AllowedSpeeds) });
                Changed?.Invoke(); break;
            case TdUiAction.RuneBranch when item.SArg is not null:
                StartRunePath(_match(), item.SArg);
                break;
            case TdUiAction.SelectRune when item.SArg is not null:
            {
                // TD.30c: clicar num nó disponível gasta 1 fragmento (o Core desperta exatamente esse nó); bloqueado/despertado só seleciona.
                _runePath.Clear(); _runePathPending = null;
                _selectedRune = item.SArg;
                var rm = _match();
                if (rm.Phase is MatchPhase.Preparo or MatchPhase.Onda && rm.Fragments >= rm.RuneCost(item.SArg) && !rm.IsAwakened(item.SArg) && rm.Frontier.Contains(item.SArg)) _send(new AwakenRune(item.SArg));
                Changed?.Invoke(); break;
            }
            case TdUiAction.AwakenRune:
                if (_match().Phase is MatchPhase.Preparo or MatchPhase.Onda && _selectedRune is not null && _match().Fragments >= _match().RuneCost(_selectedRune) && _match().Frontier.Contains(_selectedRune)) _send(new AwakenRune(_selectedRune));
                Changed?.Invoke(); break;
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
                    8 => o with { NoticeSize = (Math.Clamp(o.NoticeSize, 0, 3) + 1) % 4 },
                    _ => o,
                };
                _host.SetGameOptions(next);
                Changed?.Invoke(); break;
            }
            case TdUiAction.ShadowToggle: ToggleShadow(); break;
            case TdUiAction.AutoStartToggle:
                _send(new SetAutoStart(_match().AutoStart == AutoStartMode.Off ? AutoStartMode.Countdown : AutoStartMode.Off, 15));
                Changed?.Invoke(); break;
            case TdUiAction.TowerCategory: _towerCategory = item.SArg ?? "attack"; _buildPage = 0; _buildScroll = 0; Changed?.Invoke(); break;
            case TdUiAction.BuildCatalogPage: _buildPage = Math.Max(0, _buildPage + item.Arg); Changed?.Invoke(); break;
            case TdUiAction.TowerPage: ScrollBy(item.Arg * ModalTowerRows); break;
            case TdUiAction.OpenTower: OpenInspector(item.Arg); break;
            case TdUiAction.Reposition:
                EnterBuild();
                _moveTowerId = item.Arg;
                UpdateModalPause();
                Changed?.Invoke();
                break;
            case TdUiAction.Preview: Toggle(TdUiMode.Preview); break;
            case TdUiAction.MapOpen: SetMode(TdUiMode.Map); break;
            case TdUiAction.MapTab: _mapSeals = item.Arg == 1; _mapConfirm = false; Changed?.Invoke(); break;
            case TdUiAction.SelectStage: _mapSel = item.Arg; _mapConfirm = false; Changed?.Invoke(); break;
            case TdUiAction.MapPlay: _mapConfirm = true; Changed?.Invoke(); break;
            case TdUiAction.MapPlayCancel: _mapConfirm = false; Changed?.Invoke(); break;
            case TdUiAction.MapPlayConfirm: _mapConfirm = false; Close(); _host.StartStage(item.Arg); break;
            case TdUiAction.BuyNode when item.SArg is not null: _host.PurchaseNode(item.SArg); Changed?.Invoke(); break;
            case TdUiAction.Pause: _host.TogglePause(); Close(); break;
            case TdUiAction.Settings: OpenSettingsView(); break;
            case TdUiAction.Exit: _host.RequestExit(); break;
            case TdUiAction.RestartPrompt: _confirmRestart = true; Changed?.Invoke(); break;
            case TdUiAction.RestartCancel: _confirmRestart = false; Changed?.Invoke(); break;
            case TdUiAction.WipePrompt: _confirmWipe = true; Changed?.Invoke(); break;
            case TdUiAction.WipeCancel: _confirmWipe = false; Changed?.Invoke(); break;
            case TdUiAction.WipeConfirm: _confirmWipe = false; if (_onb is not null) RedoTutorial(); Close(); _host.ResetProfile(); break;
            case TdUiAction.Restart: { var match = _match(); int stage = match.Options.PersistentRun ? (_host.RunStartStage) : match.StageId; _restartArmed = false; Close(); _host.StartStage(stage); break; }
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
            _restartArmed = false;
            _awayActive = false;
            _restartRemaining = 0;
            _returnToEnd = false;
            _noticeText = null;
            _userPaused = false;
            _demotionPending = false; // cartão de rebaixamento é da partida antiga
            _runePath.Clear(); _runePathPending = null; _runePathNotice = null;
            _selectedRune = _lastRune = _hoverRune = _hoverBranch = null;
            RuneWindowOpen = false;
            ResetRuneCard(clearSeen: false);
            ResetFusion(clearSeen: false);
            ResetMutations(clearSeen: false);
            _runeCount = m.AwakenedCount;
            _runeKnown.Clear();
            foreach (var rn in m.RuneWeb) if (m.IsAwakened(rn.Id)) _runeKnown.Add(rn.Id);
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
        SyncRunes(m);
        SyncRunePath(m);
        SyncNotice(m);
        SyncOnboarding(m);
        SyncQuiz(m);
        SyncRuneCard(m);
        SyncAlertCard();
        SyncFuse(m);
        SyncMutationWarn(m);
        SyncShadowAway(m);
        UpdateModalPause();
        if (m.Phase is MatchPhase.Vitoria)
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
            if (_restartArmed)
            {
                double now = _now();
                double elapsed = Math.Max(0, now - _restartLast);
                _restartLast = now;
                if (!BossHidden && !ModalOpen && !m.Paused && _host.SimulationRunning) _restartRemaining -= elapsed;
                if (_restartRemaining <= 0) { _restartArmed = false; Close(); _host.StartStage(1); }
                Changed?.Invoke();
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

    /// <summary>TD.29b: acompanha a runa recém-despertada (destaque na teia e detalhe selecionado, como no design).</summary>
    private void SyncRunes(TdMatchState m)
    {
        int count = m.AwakenedCount;
        if (count == _runeCount) return;
        if (count > _runeCount)
        {
            foreach (var n in m.RuneWeb)
                if (m.IsAwakened(n.Id) && !_runeKnown.Contains(n.Id)) { _lastRune = n.Id; _selectedRune = n.Id; }
        }
        _runeCount = count;
        _runeKnown.Clear();
        foreach (var n in m.RuneWeb) if (m.IsAwakened(n.Id)) _runeKnown.Add(n.Id);
        Changed?.Invoke();
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
        _dropTowerId = 0;
        _dragTowerId = 0;
        _hoverDef = -1;
        _notice = null;
    }

    /// <summary>Recolhe expansões após interação fora do jogo, sem cancelar pausa do usuário nem o auto-reinício.</summary>
    public void CollapseOutside()
    {
        _outsideCollapsed = true;
        if (!AlertCritical) _acHidden = true; // a pausa crítica exige escolha explícita: não recolhe
        _returnToEnd = false;
        _rcPending = false;
        _ftShown = false;
        _mwShown = false;
        _tipText = null;
        Close();
    }

    private void Close()
    {
        if (_returnToEnd && Mode == TdUiMode.Runes && !BossHidden)
        {
            _returnToEnd = false;
            SetMode(TdUiMode.End);
            _endCorner = true;
            return;
        }
        ResetBuild();
        QuickTowersOpen = false;
        RuneWindowOpen = false;
        _runePath.Clear(); _runePathPending = null; _runePathNotice = null;
        _hoverRune = _hoverBranch = null;
        _legendHint = false;
        _rcShown = false; // um cartão ainda pendente continua esperando a vez
        _fuseBase = 0;
        _fuseType = null;
        _fusionTargetModule = null;
        _fusionChoosingModule = false;
        _pinnedOpen = false;
        _noticeText = null;
        _endCorner = false;
        _confirmRestart = false;
        _confirmWipe = false;
        Mode = TdUiMode.None;
        if (QuizActive) CloseQuiz();
        UpdateModalPause();
        _selectedTowerId = 0;
        MaintainPinned();
        Changed?.Invoke();
    }

    /// <summary>TD.30c: abre a janela de runas do HUD (fecha o painel de torres, um por vez; o jogo segue rodando).</summary>
    private void OpenRuneWindow(bool fromCard = false)
    {
        ResetBuild();
        QuickTowersOpen = false;
        _pinnedOpen = false;
        _hoverRune = _hoverBranch = null;
        _legendHint = fromCard;
        _rcShown = _rcPending = false; // o jogador foi às runas: o aviso cumpriu o papel
        Mode = TdUiMode.None;
        RuneWindowOpen = true;
        RecenterWeb();
        UpdateModalPause();
        Touch();
        Changed?.Invoke();
    }

    private void OpenQuickTowers()
    {
        RuneWindowOpen = false; _hoverRune = null;
        QuickTowersOpen = true; Mode = TdUiMode.Build; ResetBuild(); UpdateModalPause(); Changed?.Invoke();
    }

    /// <summary>TD.28b: com o painel fixo ligado, reabre o painel rápido sempre que nada mais está aberto; ao desligar fecha o que o fixo abriu.</summary>
    private void MaintainPinned()
    {
        var m = _match();
        bool want = PanelPinned && !_outsideCollapsed && !BossHidden && m.Phase is not (MatchPhase.Vitoria);
        if (want && !QuickTowersOpen && !RuneWindowOpen && Mode == TdUiMode.None) { OpenQuickTowers(); _pinnedOpen = true; }
        else if (!want && _pinnedOpen && QuickTowersOpen && !BuildActive) Close();
    }

    /// <summary>
    /// TD.28b (atalho global de minimizar tudo): esconde/mostra tudo. Ao esconder fecha modal, painel, cartões e pergunta (a partida segue);
    /// ao mostrar reabre o painel fixo e, se a corrida já acabou, o cartão de fim.
    /// </summary>
    public void SetBossHidden(bool hidden)
    {
        if (BossHidden == hidden) return;
        _restartLast = _now();
        BossHidden = hidden;
        if (hidden) { Close(); UpdateModalPause(); return; }
        UpdateModalPause();
        _moneyReset = true; // volta no valor real, sem animar a diferença acumulada
        _endShown = false;
        MaintainPinned();
        Changed?.Invoke();
    }

    private void SyncNotice(TdMatchState m)
    {
        if (!ReferenceEquals(_rewindMatch, m)) { _rewindMatch = m; _seenBossRewinds = m.BossRewinds; _pendingBossRewindWave = 0; }
        bool rewound = m.BossRewinds > _seenBossRewinds;
        _seenBossRewinds = m.BossRewinds;
        if (rewound) _pendingBossRewindWave = Math.Max(1, m.TotalWaves - 10);
        var progress = _host.Rank;
        var rank = progress.Current;
        var previousRank = _seenRank;
        bool promoted = rank > _seenRank;
        bool demoted = rank < _seenRank;
        _seenRank = rank;
        if (!ReferenceEquals(_attackMatch, m)) { _attackMatch = m; _seenLost = m.ReputationLostTotal; }
        bool attacked = m.ReputationLostTotal > _seenLost;
        _seenLost = m.ReputationLostTotal;
        if (attacked && !BossHidden) _criticalNoticeUntil = _now() + StageNoticeSeconds;
        if (progress.Total != _seenRankScore)
        {
            _seenRankScore = progress.Total;
            Changed?.Invoke();
        }
        int n = m.StageResults.Count;
        if (n > _seenStages)
        {
            _seenStages = n;
            if (BossHidden) return;
            var r = m.StageResults[^1];
            var o = m.Shadow ? default : _host.LastOutcome; // sombra: sem recorde/selos de fase
            _noticeTitle = null;
            _noticeRank = null;
            _noticeText = $"Fase {r.StageNumber}" + (r.Crown ? " +coroa" : "") + (o.NewRecord ? " +recorde" : "");
            if (o.BonusSeals > 0) _noticeText += $" · +{o.BonusSeals} selo{(o.BonusSeals > 1 ? "s" : "")}"; // o cartão de fim aponta a aba
            if (m.Options.InitialReputation is not null && m.StageNumber != r.StageNumber + 1)
                _noticeText = $"Preso {TdStageGate.RegionWithArticle(m.Content.Stages.Stages[(m.StageNumber - 1) % m.Content.Stages.Stages.Length].Region)} por ser {TdRank.DisplayName(TdRank.For(m.GateReputation))}";
            _noticeUntil = _now() + StageNoticeSeconds;
            Changed?.Invoke();
        }
        if (promoted && !BossHidden)
        {
            _promotionPreviousRank = previousRank;
            _noticeTitle = "Promoção";
            _noticeText = $"Promovido a {TdRank.DisplayName(rank)}!";
            if (m.Options.InitialReputation is not null)
            {
                var unlocked = m.Content.Stages.Stages.FirstOrDefault(s => s.MinRank == (int)rank);
                if (unlocked is not null) _noticeText = $"{unlocked.Title} liberada";
            }
            _noticeRank = rank;
            _noticeStarted = _now();
            _noticeUntil = _now() + PromoNoticeSeconds;
            Changed?.Invoke();
        }
        else if (demoted)
        {
            StartDemotion(rank, progress);
            _noticeTitle = "Rebaixamento";
            _noticeText = $"Rebaixado a {TdRank.DisplayName(rank)}";
            _noticeRank = rank;
            _noticeStarted = _now();
            _noticeUntil = _now() + StageNoticeSeconds;
            Changed?.Invoke();
        }
        else if (_pendingBossRewindWave > 0 && !BossHidden && !NoticeOpen)
        {
            _noticeTitle = null;
            _noticeRank = null;
            _noticeText = $"Chefe: voltou para a onda {_pendingBossRewindWave}";
            _pendingBossRewindWave = 0;
            _noticeUntil = _now() + StageNoticeSeconds;
            Changed?.Invoke();
        }
        else if (attacked && !BossHidden && _now() >= _attackNextAt && !NoticeOpen && !_rcPending && !_rcShown && !(_onb is not null && !_onb.TipSeen(RuneTipKey)))
        {
            // Aviso discreto e único: sem som, sem foco, no máximo 1 a cada AttackNoticeCooldownSeconds.
            _attackNextAt = _now() + AttackNoticeCooldownSeconds;
            _noticeTitle = AttackTitle;
            _noticeText = "Base sendo atacada";
            _noticeRank = null;
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
        if (_noticeTitle == "Promoção" && _noticeRank is { } promotion)
            return BuildPromotion(_match(), _promotionPreviousRank, promotion);
        // Toast discreto: uma linha, tamanho configurável, sem animação e somente um por vez.
        var tone = _noticeRank is not null ? TdTone.Accent : _noticeTitle == AttackTitle ? TdTone.Warn : _noticeTitle == "Fusão recusada" ? TdTone.Warn : TdTone.Good;
        float scale = _host.GameOptions.NoticeSize switch { 2 => 1.25f, 3 => 1.5f, _ => 1f };
        float h = ToastH * scale;
        float w = Math.Clamp(26f + _noticeText!.Length * 6.6f + (_noticeRank is not null ? 22f : 0f), 110f, 280f) * scale;
        var item = new TdUiItem(new DipRect(0, 0, w, h), TdUiKind.Label, _noticeText!, Arg: _noticeRank is { } r ? (int)r : -1, SArg: "toast", Tone: tone, Icon: tone == TdTone.Warn ? TdIcon.Alert : TdIcon.Check,
            Fill: 1f, FontSize: ToastFont * scale);
        return new TdPopoverView(_noticeTitle ?? "Fase concluida", [item], w, h, 0f);
    }

    private void SetMode(TdUiMode mode)
    {
        ResetBuild();
        QuickTowersOpen = false;
        RuneWindowOpen = false;
        _hoverRune = null;
        _webCentered = false; // cada abertura da aba Runas recentraliza na última runa
        _endCorner = false;
        _confirmRestart = false;
        _confirmWipe = false;
        _mapConfirm = false;
        _mapSel = 0;
        _captureKey = null; _hotkeyError = null;
        Mode = mode;
        UpdateModalPause();
        if (mode != TdUiMode.Inspector) _selectedTowerId = 0;
        if (mode == TdUiMode.None) MaintainPinned();
        Touch();
        Changed?.Invoke();
    }

    private void UpdateModalPause() => _modalCount = ModalOpen || (_demotionPending && !BossHidden) ? 1 : 0;

    /// <summary>Abre a tela inicial sem interromper a partida.</summary>
    public void OpenStartupModal() { if (_tutorial is { Active: true }) Close(); else SetMode(TdUiMode.Menu); }

    private void Toggle(TdUiMode mode) => SetMode(Mode == mode ? TdUiMode.None : mode);

    private void OpenInspector(int towerId)
    {
        SetMode(TdUiMode.Inspector);
        _selectedTowerId = towerId;
        Changed?.Invoke();
    }

    private int DragDef()
    {
        var m = _match();
        int ti = FindTower(m, _dragTowerId);
        return ti >= 0 ? m.Towers[ti].DefIndex : -1;
    }

    private string? DragModuleId()
    {
        var m = _match();
        var (b, mo, r) = m.CheckFusionTowers(_dropTowerId, _dragTowerId);
        if (r != CommandReason.None) return m.Content.Towers[DragDef()].Id;
        int mi = FindTower(m, mo);
        return mi >= 0 ? m.Content.Towers[m.Towers[mi].DefIndex].Id : null;
    }

    private void UpdateGhost(float xDip)
    {
        var m = _match();
        int w = TdBand.WidthDip(m);
        _dropTowerId = 0;
        // Fusão por soltar: com um tipo escolhido, o fantasma sobre uma torre posicionada vira preview de fusão (o Core revalida em FuseTower).
        if (_moveTowerId == 0 && _placementType is not null
            && TdHitTester.Test(m, Math.Clamp(xDip, 0f, w - 1f), TdBand.HeightDip / 2f) is { Kind: HitKind.TdTower } hit
            && FindTower(m, hit.TowerId) is var ti and >= 0 && m.Towers[ti].Placed)
        {
            _dropTowerId = hit.TowerId;
            _dropCheck = m.CheckFusion(hit.TowerId, _placementType);
            _ghostX = m.Towers[ti].X;
            _ghostValid = _dropCheck.IsOk;
            _notice = _dropCheck.IsOk ? $"Soltar funde ({_dropCheck.Count}/{_dropCheck.Limit}): {_dropCheck.Name} ${_dropCheck.Cost}" : "Fusão: " + DropReasonText(m, _dropCheck);
            return;
        }
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

    public TdHudView BuildHud() => BuildHudCore() with { Shadow = _match().Shadow };

    private TdHudView BuildHudCore()
    {
        var m = _match();
        var items = new List<TdUiItem>(14);
        var repRank = _host.Rank;
        string repText = XpText(repRank.Total) + StreakSuffix(m);
        int shown = m.Phase == MatchPhase.Onda || m.NextWaveNumber == 0 ? m.WaveNumber : m.NextWaveNumber;
        shown = Math.Clamp(shown, 1, Math.Max(1, m.TotalWaves));
        bool run = m.Options.PersistentRun;
        string stageText = run ? StageLabel(m.StageNumber, m.Content.Stages.Stages.Length) + "  " : "";
        string coreTip = $"Reputação {repRank.Total} XP ({TdRank.DisplayName(repRank.Current)}): vencer ondas dá XP; inimigo que chega à base tira XP e pode rebaixar sua patente. Não há derrota."
            + (run ? " O jogo é infinito: depois da fase 6 entra o Ciclo 2 (e 3, 4…), as mesmas regiões mais fortes." : "")
            + $" Esconder tudo: {_host.BossHotkey}.";

        bool needed = _onb is not null && (HudNeeded(m) || ComputeStep(m) == TdTutorialStep.HudOpen);
        bool live = m.Phase is MatchPhase.Preparo or MatchPhase.Onda;
        bool showStart = live && (!m.Options.ContinuousWaves || m.Phase == MatchPhase.Preparo);
        UpdateMoney(m);
        // Keep the expanded window's size fixed while income floats appear and disappear.
        // The minimized HUD does not draw floats, so it must never inherit their top padding.
        float y0 = _host.HudMinimized ? 0f : TdHudView.FloatPad;
        const float startWidth = 88f;
        float offset = showStart ? startWidth + 4f : 0f;
        if (showStart)
        {
            var (_, _, enabled, tone, tip) = StartButton(m);
            items.Add(new TdUiItem(new DipRect(0, y0 + 6, startWidth, 28), TdUiKind.Button,
                m.Phase == MatchPhase.Onda ? "Próxima" : "Iniciar", TdUiAction.StartWave,
                Enabled: enabled && !StartBlockedFor(m) && !ModalOpen, Tone: tone,
                Tip: StartBlockedFor(m) ? "Coloque uma torre para começar" : tip,
                Icon: m.Phase == MatchPhase.Onda ? TdIcon.FastForward : TdIcon.Play));
        }
        void Add(float x, float y, float w, float h, TdUiKind kind, string text, TdUiAction action = TdUiAction.None,
            int arg = 0, string? tag = null, bool enabled = true, bool selected = false, TdIcon icon = TdIcon.None, string? tip = null, bool highlight = false, float fill = 0f, string? sub = null)
            => items.Add(new TdUiItem(new DipRect(offset + x, y0 + y, w, h), kind, text, action, arg, SArg: tag,
                Enabled: enabled, Selected: selected, Icon: icon, Tip: tip, Highlight: highlight, Fill: fill, Sub: sub));
        // Reserve space for large numbers before laying out later groups.
        // JetBrains Mono at the largest HUD font (20 DIP) advances 12 DIP per glyph.
        // Reserve the common large values so streak changes do not resize the window.
        float coreExtra = Math.Max(0, 20f + Math.Max(repText.Length, 10) * 12f - 73f);
        float waveExtra = Math.Max(0, Math.Max($"{shown}/{m.TotalWaves}".Length, 5) * 12f + 8f - 52f);
        if (_host.HudMinimized)
        {
            // O chip permanece no canto e nunca abre automaticamente ao haver uma ação pendente.
            offset = 0;
            items.Clear();
            bool actionReady = (showStart && StartButton(m).Enabled && !StartBlockedFor(m) && !ModalOpen)
                || m.CanAwakenAny;
            Add(12, 6, 73 + coreExtra, 28, TdUiKind.Button, repText, TdUiAction.HudRestore,
                tag: "core", icon: TdIcon.Medal, fill: (float)repRank.Fraction, tip: coreTip + $" {stageText.Trim()} Onda {shown}/{m.TotalWaves}. Clique para expandir o HUD.", highlight: needed && PulseOn, sub: _host.GameOptions.NoticeSize == 0 && _now() < _criticalNoticeUntil ? "critical-notice" : null);
            Add(96 + coreExtra, 11, 1, 18, TdUiKind.Separator, "");
            Add(109 + coreExtra, 6, 167 + waveExtra, 28, TdUiKind.Label, $"{shown}/{m.TotalWaves}", tag: "wave", action: TdUiAction.Menu, tip: $"{stageText}Onda {shown}/{m.TotalWaves}. Clique para abrir o menu.");
            Add(282 + coreExtra + waveExtra, 6, 28, 28, TdUiKind.Button, "", TdUiAction.HudExpand,
                icon: TdIcon.Expand, tip: actionReady ? "Expandir HUD: há uma ação disponível" : "Expandir HUD", fill: actionReady ? 1f : 0f);
            return CompactHud(AddRankToHud(new TdHudView(HighlightHud(items), 316 + coreExtra + waveExtra, 40, Minimized: true, Wave: shown, TotalWaves: m.TotalWaves)), 0.55f);
        }
        float moneyExtra = Math.Max(0, Math.Max(_money.Real, _money.Display).ToString(System.Globalization.CultureInfo.InvariantCulture).Length - 3) * 8f;
        float grow = coreExtra + waveExtra + moneyExtra;
        Add(12, 6, 73 + coreExtra, 28, TdUiKind.Button, repText, TdUiAction.HudRestore, tag: "core", icon: TdIcon.Medal, fill: (float)repRank.Fraction, tip: coreTip + $" {stageText.Trim()} Onda {shown}/{m.TotalWaves}. Créditos ¢{m.Credits}. Clique para abrir o menu.", highlight: needed && PulseOn, sub: _host.GameOptions.NoticeSize == 0 && _now() < _criticalNoticeUntil ? "critical-notice" : null);
        Add(96 + coreExtra, 11, 1, 18, TdUiKind.Separator, "");
        Add(109 + coreExtra, 6, 167 + waveExtra, 28, TdUiKind.Label, $"{shown}/{m.TotalWaves}", tag: "wave", action: TdUiAction.Menu, tip: $"{stageText}Onda {shown}/{m.TotalWaves}. Clique para abrir o menu.");
        Add(288 + coreExtra + waveExtra, 11, 1, 18, TdUiKind.Separator, "");
        Add(301 + coreExtra + waveExtra, 6, 52 + moneyExtra, 28, TdUiKind.Money, _money.Display.ToString(System.Globalization.CultureInfo.InvariantCulture), TdUiAction.TowersOpen, tip: $"Créditos ¢{m.Credits}: ganhos de abates, bônus e perguntas; servem para comprar torres. Clique para abrir as torres." + (_money.IncomeText is { } inc ? $" Renda recente {inc}." : ""), fill: _money.MilestoneGlow, sub: _money.IncomeText);
        bool runeReady = m.CanAwakenAny;
        Add(359 + grow, 6, 28, 28, TdUiKind.Button, "", TdUiAction.RuneWindow, enabled: !ModalOpen, selected: RuneWindowOpen, icon: TdIcon.Star, tip: $"Runas {m.AwakenedCount}/{m.RuneWeb.Count}" + (runeReady ? $": {m.Fragments} fragmento(s), há runa para despertar. " : $": {m.Fragments} fragmento(s). ") + "Clique para abrir ou fechar a janela de runas.", fill: runeReady ? 1f : 0f);
        Add(399 + grow, 6, 28, 28, TdUiKind.Button, "", TdUiAction.QuickTowers, enabled: !ModalOpen, selected: QuickTowersOpen, icon: TdIcon.Castle, tip: PanelPinned ? "Torres (painel fixo: Jogo ou Configurações)" : "Torres: escolha e posicione sem pausar", fill: _money.BuyGlow);
        Add(439 + grow, 11, 1, 18, TdUiKind.Separator, "");
        bool paused = _userPaused && live;
        Add(452 + grow, 6, 28, 28, TdUiKind.Button, "", TdUiAction.PauseToggle, enabled: !ModalOpen && live, selected: paused, icon: paused ? TdIcon.Play : TdIcon.Pause, tip: paused ? "Retomar o jogo" : "Pausar o jogo");
        var allowed = AllowedSpeeds;
        int speed = TdSpeed.Resolve(_host.GameOptions.GameSpeed, allowed);
        double effective = _host.EffectiveSpeed;
        bool slow = speed > 3 && (speed >= TdSpeed.Max || effective < speed - 0.5);
        const float speedExtra = 0f; // extras não alargam a HUD: um botão que cicla ocupa o espaço dos 3 botões
        Add(492 + grow, 6, 4 + 3 * 32, 28, TdUiKind.Label, "", tag: "speeds", tip: "Velocidade do jogo");
        if (_host.GameOptions.ExtraSpeeds)
        {
            string effTxt = slow ? $"≈{effective.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}x" : TdSpeed.Label(speed);
            string cyc = $"Velocidade {TdSpeed.Label(speed)}: clique para a próxima" + (slow ? $" (efetiva ≈{effective.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}x)" : "");
            Add(495 + grow, 9, 58, 22, TdUiKind.Button, TdSpeed.Label(speed), TdUiAction.SetSpeed, TdSpeed.Next(speed, allowed), enabled: !ModalOpen, selected: true, tip: cyc);
            if (slow) Add(555 + grow, 9, 40, 22, TdUiKind.Label, effTxt, tag: "speedeff", tip: "Velocidade efetiva real (abaixo da pedida: o PC não acompanha)");
        }
        else
        for (int i = 0; i < allowed.Length; i++)
        {
            int v = allowed[i];
            Add(495 + grow + i * 32, 9, 30, 22, TdUiKind.Button, TdSpeed.Label(v), TdUiAction.SetSpeed, v, enabled: !ModalOpen, selected: speed == v, tip: $"Velocidade {v}x");
        }
        // O TD não tem caixa coletável desde TD.30a; a ação permanece desabilitada.
        Add(604 + grow + speedExtra, 6, 28, 28, TdUiKind.Button, "", TdUiAction.HudMinimize, icon: TdIcon.Collapse, tip: "Recolher HUD");
        return CompactHud(AddRankToHud(new TdHudView(HighlightHud(items), offset + 638 + grow + speedExtra, 40 + y0, Minimized: false, Wave: shown, TotalWaves: m.TotalWaves, Floats: _money.Floats, TopPad: y0)), 0.7f);
    }

    /// <summary>XP compacto para o HUD (cabe em até 5 caracteres).</summary>
    internal static string XpText(int xp) => xp < 10_000 ? xp.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : (xp / 1000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "k";

    /// <summary>Medidor monotônico de dano à base: só desce quando a base é atacada (integridade no clássico, XP perdido na corrida).</summary>
    internal static long HurtMeter(TdMatchState m) => -m.ReputationLostTotal;

    private TdHudView AddRankToHud(TdHudView view)
    {
        const float space = 34f;
        var rank = _host.Rank.Current;
        var crown = new TdUiItem(new DipRect(4, view.TopPad + 4, 32, 32), TdUiKind.Label, "", Arg: (int)rank,
            Action: TdUiAction.Menu, SArg: "rank-crown", Tip: $"Patente: {TdRank.DisplayName(rank)}. Clique para abrir o menu.");
        return view with { Width = view.Width + space, Items = [crown, .. view.Items.Select(i => i with
            { Rect = new DipRect(i.Rect.X + space, i.Rect.Y, i.Rect.W, i.Rect.H) })] };
    }

    private static TdHudView CompactHud(TdHudView view, float scale) => view with
    {
        Width = view.Width * scale, Height = view.Height * scale, TopPad = view.TopPad * scale, LayoutScale = scale,
        Items = view.Items.Select(i => i with { Rect = new DipRect(i.Rect.X * scale, i.Rect.Y * scale, i.Rect.W * scale, i.Rect.H * scale) }).ToArray(),
    };

    // ================================================================ TD.33d: dinheiro visível (só apresentação)

    private readonly TdMoneyModel _money = new();
    private TdMatchState? _moneyMatch;
    private bool _moneyReset;

    /// <summary>Modelo de apresentação do dinheiro (valor exibido, "+N", renda, brilhos). Nada disto vai ao Core, ao log nem ao hash.</summary>
    public TdMoneyModel Money => _money;

    /// <summary>Há animação de dinheiro agendável agora (falso com boss key, pausa, modal ou tudo parado).</summary>
    public bool MoneyAnimating => !BossHidden && _money.Animating;

    private void UpdateMoney(TdMatchState m)
    {
        if (BossHidden) return; // oculto: nada anima nem agenda; ao voltar, Reset no valor real
        var o = _host.GameOptions;
        _money.Discreet = o.Discreet;
        _money.ShowIncome = o.ShowIncome;
        long threshold = MoneyThreshold(m);
        double now = _now();
        if (!ReferenceEquals(m, _moneyMatch) || _moneyReset)
        {
            _moneyMatch = m; _moneyReset = false;
            _money.Reset(m.Credits, m.Frame, threshold, now);
            return;
        }
        _money.Update(m.Credits, m.Frame, now, _userPaused || ModalOpen, threshold);
    }

    /// <summary>Custo da torre escolhida; sem escolha, o da mais barata desbloqueada (preço atual da curva). 0 = nenhuma.</summary>
    private long MoneyThreshold(TdMatchState m)
    {
        var towers = m.Content.Towers;
        if (_placementType is not null)
        {
            int i = Array.FindIndex(towers, t => t.Id == _placementType);
            if (i >= 0) return m.NextTowerCost(i);
        }
        long min = 0;
        for (int i = 0; i < towers.Length; i++)
        {
            if (m.CurrentRankId < towers[i].MinRank) continue;
            long cost = m.NextTowerCost(i);
            if (min == 0 || cost < min) min = cost;
        }
        return min;
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
            default: return ("", null, false, TdTone.Dim, "A partida terminou.");
        }
    }

    // ================================================================ popover

    /// <summary>Modelo do popover atual (null = fechado). A janela posiciona acima da faixa em <see cref="TdPopoverView.AnchorX"/>.</summary>
    public TdPopoverView? BuildPopover()
    {
        var m = _match();
        if (BossHidden) return null;
        if (DemotionCardOpen) return BuildDemotionCard();
        if (AlertCritical) return BuildAlertCard();
        if (FuseDragCardOpen) return BuildFuseDragCard(m);
        if (_outsideCollapsed) return null;
        if (AlertWarningOpen) return BuildAlertCard();
        if (!BuildActive && (Mode == TdUiMode.None || QuickTowersOpen) && _quizQ is not null && QuizPhaseOk(m)) return BuildQuiz(m);
        if (!BuildActive && NoticeOpen && (Mode == TdUiMode.None || QuickTowersOpen)) return BuildNotice();
        if (!BuildActive && FuseCardOpen) return BuildFuseCard(m);
        if (_fusionChoosingModule || FusionTargetActive) return BuildFusionGesture(m);
        if (!BuildActive && FusionTipOpen && (Mode == TdUiMode.None || QuickTowersOpen)) return BuildFusionTip();
        if (!BuildActive && MutationWarnOpen && (Mode == TdUiMode.None || QuickTowersOpen)) return BuildMutationWarn(m);
        if (!BuildActive && RuneCardOpen && (Mode == TdUiMode.None || QuickTowersOpen)) return BuildRuneCard();
        if (QuickTowersOpen) return BuildQuickTowers(m);
        if (RuneWindowOpen && Mode == TdUiMode.None) return BuildRuneWindow(m);
        if (BuildActive) return _tutorial is { Active: true, Index: 0 } ? BuildTourCallout() : null;
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
            TdUiMode.Runes => Finish("Teia de runas", Placeholder(), 0),
            TdUiMode.Bestiary => Finish("Inimigos · bestiário", Placeholder(), 0),
            TdUiMode.Settings => Finish("Configurações", Placeholder(), 0),
            _ => null,
        };
        return view is null ? Step == TdTutorialStep.HudOpen ? null : DecoratePopover(m, null)
            : EndCardOpen ? DecoratePopover(m, view) : Modalize(DecoratePopover(m, view));
    }

    /// <summary>Descrição curta (design Partida.dc.html; demais torres com texto equivalente do conteúdo) mostrada no painel rápido.</summary>
    public static string TowerBlurb(TdTowerDef d) => d.Id switch
    {
        "bolt" => "Longo alcance (150). Fraco contra enxames e blindados.",
        "frost" => "Controla e desacelera. Quase não causa dano.",
        "cannon" => "Alcance 110. Não acerta voadores nem rápidos.",
        "balancer" => "Segura inimigos de solo. Não segura voadores.",
        "scaleset" => "Reforça torres vizinhas. Não ataca.",
        _ => d.Role,
    };

    public const float QuickWidth = 304f, QuickHeader = 6f, QuickCardH = 62f;
    private string _towerCategory = "attack";
    private int _buildPage;
    private int _buildScroll, _towerScroll;
    public const int ModalBuildRows = 5, ModalTowerRows = 6;

    private static string NextTowerHint(TdMatchState m)
    {
        var next = m.Content.Towers.Where(t => t.MinRank > m.CurrentRankId).OrderBy(t => t.MinRank).FirstOrDefault();
        return next is null ? "Todas as torres liberadas" : $"Próxima: {next.Name} · patente {TdRank.DisplayName((TdRankLevel)next.MinRank)}";
    }
    private static readonly (string Id, string Name)[] TowerCategories = [("attack", "Ataque"), ("control", "Controle"), ("support", "Suporte"), ("economy", "Economia")];

    private void EnsureTowerCategory(TdMatchState m)
    {
        if (m.Content.Towers.Any(t => t.Category == _towerCategory && t.MinRank <= m.CurrentRankId)) return;
        _towerCategory = TowerCategories.FirstOrDefault(c => m.Content.Towers.Any(t => t.Category == c.Id && t.MinRank <= m.CurrentRankId)).Id ?? "attack";
    }

    /// <summary>Painel rápido (Partida.dc.html): cartão por torre com sprite, nome, descrição curta e preço âmbar; clicar escolhe sem pausar.</summary>
    private TdPopoverView BuildQuickTowers(TdMatchState m)
    {
        var items = new List<TdUiItem>();
        EnsureTowerCategory(m);
        float w = QuickWidth;
        bool tut = _tutorial is { Active: true, Index: 0 } && !m.HasPlacedTower;
        bool marked = false;
        int tabs = 0;
        foreach (var (id, name) in TowerCategories)
        {
            if (!m.Content.Towers.Any(t => t.Category == id && t.MinRank <= m.CurrentRankId)) continue;
            items.Add(new TdUiItem(new DipRect(8 + tabs++ * 72, QuickHeader, 68, 22), TdUiKind.Button, name, TdUiAction.TowerCategory, SArg: id, Selected: _towerCategory == id));
        }
        int count = m.Content.Towers.Count(t => t.Category == _towerCategory && t.MinRank <= m.CurrentRankId);
        const int pageSize = 6;
        _buildPage = Math.Clamp(_buildPage, 0, Math.Max(0, (count - 1) / pageSize));
        int row = 0, seen = 0;
        for (int i = 0; i < m.Content.Towers.Length; i++)
        {
            var d = m.Content.Towers[i];
            if (d.Category != _towerCategory || m.CurrentRankId < d.MinRank) continue;
            if (seen++ < _buildPage * pageSize || row >= pageSize) continue;
            long price = m.NextTowerCost(i);
            bool locked = m.CurrentRankId < d.MinRank;
            bool enabled = !locked && m.Credits >= price;
            bool hl = tut && !marked && enabled && PulseOn;
            if (hl) marked = true;
            items.Add(new TdUiItem(new DipRect(8 + row++ * 48, QuickHeader + 26, 44, QuickCardH), TdUiKind.Button, d.Name,
                TdUiAction.ChooseTower, Arg: Array.FindIndex(TowerCategories, c => c.Id == d.Category), SArg: d.Id, Right: $"{price}", Tip: $"{d.Name}: {TowerBlurb(d)} Forte: {string.Join(", ", d.Strong)}. Fraqueza: {d.WeaknessText}", Enabled: enabled,
                Selected: _placementType == d.Id, Highlight: hl));
        }
        float statusY = QuickHeader + 26 + QuickCardH + 4;
        items.Add(new TdUiItem(new DipRect(8, statusY, 22, 20), TdUiKind.Button, "<", TdUiAction.BuildCatalogPage, -1, Enabled: _buildPage > 0));
        items.Add(new TdUiItem(new DipRect(30, statusY, w - 60, 20), TdUiKind.Label, (tut && BuildActive ? _notice ?? "Clique em um ponto livre da faixa" : NextTowerHint(m)), Tone: TdTone.Dim));
        items.Add(new TdUiItem(new DipRect(w - 30, statusY, 22, 20), TdUiKind.Button, ">", TdUiAction.BuildCatalogPage, 1, Enabled: (_buildPage + 1) * pageSize < count));
        float h = statusY + 24;
        return new TdPopoverView(BuildActive && _dropTowerId > 0 ? (_dropCheck.IsOk ? $"FUNDIR {_dropCheck.Count}/{_dropCheck.Limit}: {_dropCheck.Name}  ${_dropCheck.Cost}" : "FUSÃO: " + DropReasonText(m, _dropCheck)) : BuildActive ? "TORRES   Toque na faixa" : "TORRES   Escolha uma torre", [.. items], w, h, 0, Quick: true);
    }

    private static Stack Placeholder()
    {
        var s = new Stack(PopoverWidth);
        s.Add(TdUiKind.Label, "Teia de runas", RowH);
        return s;
    }

    // ================================================================ teia de runas (Main.dc.html, aba Runas)

    /// <summary>Estado de um nó da teia no item (campo <c>Fill</c>): 1 despertada, 0,5 disponível, 0 bloqueada.</summary>
    public const float RuneOn = 1f, RuneAvailable = 0.5f, RuneLocked = 0f;

    /// <summary>Codifica tipo (índice em <c>Runes.Types</c>), anel e setor no <c>Arg</c> do item de nó (o renderer decodifica).</summary>
    public static int PackRune(int typeIndex, int ring, int sector) => (typeIndex << 8) | (ring << 4) | sector;
    public static (int Type, int Ring, int Sector) UnpackRune(int arg) => (arg >> 8, (arg >> 4) & 15, arg & 15);

    /// <summary>Tamanhos (DIP) do nó despertado, disponível e bloqueado: modal e janela do HUD. TD.30e: o bloqueado cresce para o glifo do ramo ficar legível.</summary>
    private readonly record struct RuneSizes(float On, float Available, float Locked, float MinHit);
    private static readonly RuneSizes ModalRune = new(30f, 26f, 22f, 22f), HudRune = new(20f, 19f, 17f, 16f);

    public const float RuneWindowWidth = 288f, RuneWebWidth = 268f, RuneWebHeight = 168f;

    /// <summary>
    /// Escala do mundo da teia: DIP por unidade normalizada (x/y do conteúdo). A teia de 128 nós é grande demais para caber legível: a janela do HUD
    /// mostra só a região ao redor da última runa despertada (zoom fixo <see cref="HudWorld"/>) e a aba Runas da modal rola (arrasto/setas) e dá
    /// zoom (roda) sobre <see cref="ModalWorld"/> × zoom.
    /// </summary>
    public const float HudWorld = 820f, ModalWorld = 1400f, MinWebZoom = 0.4f, MaxWebZoom = 2.2f;

    /// <summary>Níveis de detalhe da teia por zoom: 0 = completo (glifos, brilhos, arcos), 1 = nós simples sem glifo, 2 = pontos e fios finos (zoom afastado).</summary>
    public const float LodFullZoom = 0.75f, LodMidZoom = 0.55f;

    public static int RuneLod(float zoom) => zoom >= LodFullZoom ? 0 : zoom >= LodMidZoom ? 1 : 2;

    /// <summary>Fator de tamanho dos nós: encolhe junto com o zoom afastado para não se sobreporem (vizinhos ficam ≥ 0,029 × <see cref="ModalWorld"/> × zoom).</summary>
    public static float RuneNodeScale(float zoom) => Math.Clamp(zoom, 0.4f, 1f);
    private float _webZoom = 1f, _webPanX = 0.5f, _webPanY = 0.5f;
    private bool _webCentered;

    /// <summary>Zoom atual da teia na aba Runas.</summary>
    public float WebZoom => _webZoom;

    /// <summary>Centro (coordenadas normalizadas) da região visível da teia na aba Runas.</summary>
    public (float X, float Y) WebCenter => (_webPanX, _webPanY);

    /// <summary>Roda do mouse na aba Runas: <paramref name="steps"/> &gt; 0 aproxima. Devolve true se a aba é a de runas.</summary>
    public bool ZoomWeb(int steps, float? anchorX = null, float? anchorY = null)
    {
        if (Mode != TdUiMode.Runes && !RuneWindowOpen) return false;
        float z = Math.Clamp(_webZoom * MathF.Pow(1.18f, steps), MinWebZoom, MaxWebZoom);
        if (z != _webZoom)
        {
            var area = WebArea();
            float world = RuneWindowOpen ? HudWorld : ModalWorld;
            float x = anchorX ?? area.X + area.W / 2, y = anchorY ?? area.Y + area.H / 2;
            _webPanX = Math.Clamp(_webPanX + (x - area.X - area.W / 2) / world * (1 / _webZoom - 1 / z), 0f, 1f);
            _webPanY = Math.Clamp(_webPanY + (y - area.Y - area.H / 2) / world * (1 / _webZoom - 1 / z), 0f, 1f);
            _webZoom = z; Changed?.Invoke();
        }
        return true;
    }

    /// <summary>Arrasto/rolagem da teia na aba Runas: deslocamento em DIP da modal (o conteúdo acompanha o ponteiro). Devolve true se a aba é a de runas.</summary>
    public bool PanWeb(float dxDip, float dyDip)
    {
        if (Mode != TdUiMode.Runes && !RuneWindowOpen) return false;
        float k = (RuneWindowOpen ? HudWorld : ModalWorld) * _webZoom;
        _webPanX = Math.Clamp(_webPanX - dxDip / k, 0f, 1f);
        _webPanY = Math.Clamp(_webPanY - dyDip / k, 0f, 1f);
        Changed?.Invoke();
        return true;
    }

    public DipRect WebArea() => RuneWindowOpen ? new DipRect(10, 40, RuneWebWidth, RuneWebHeight) : new DipRect(16, 102, 688, ModalWebHeight);
    public bool WebContains(float x, float y)
    {
        var a = WebArea();
        return (RuneWindowOpen || Mode == TdUiMode.Runes) && x >= a.X && x < a.X + a.W && y >= a.Y && y < a.Y + a.H;
    }

    /// <summary>Volta o zoom e centraliza na última runa despertada (ou no núcleo).</summary>
    public void RecenterWeb()
    {
        _webZoom = 1f;
        var (x, y) = RuneFocus(_match());
        _webPanX = x; _webPanY = y;
        Changed?.Invoke();
    }

    /// <summary>Ponto de foco da teia (normalizado): a última runa despertada que o jogador viu; o núcleo se nenhuma.</summary>
    private (float X, float Y) RuneFocus(TdMatchState m)
    {
        if (_lastRune is not null)
            foreach (var n in m.RuneWeb) if (n.Id == _lastRune) return ((float)n.X, (float)n.Y);
        var available = m.RuneWeb.Where(n => m.Frontier.Contains(n.Id))
            .OrderBy(n => (n.X - 0.5) * (n.X - 0.5) + (n.Y - 0.5) * (n.Y - 0.5)).FirstOrDefault();
        if (available is not null) return ((float)available.X, (float)available.Y);
        return (0.5f, 0.5f);
    }
    public const float ModalWebHeight = 318f, ModalLegendHeight = 32f, HudLegendHeight = 30f;

    /// <summary>Etiqueta de ramo para o nome acessível do nó.</summary>
    private static string RuneStateWord(TdRuneState st) => st switch { TdRuneState.Awake => "despertada", TdRuneState.Available => "disponível", _ => "bloqueada" };

    /// <summary>
    /// Nós da teia REAL do Core (32 nós, 4 anéis × 8 setores; posição normalizada X/Y do conteúdo mapeada na área). TD.30e: o RAMO de todo nó é
    /// visível em qualquer estado (o <c>Arg</c> carrega o índice do tipo; o renderer desenha o glifo): despertada cheia, disponível tracejada,
    /// bloqueada esmaecida. <c>Selected</c> marca os nós do ramo sob o mouse na legenda. O <c>Rect</c> é a área de acerto (mínimo
    /// <see cref="RuneSizes.MinHit"/>) e o diâmetro visual vai em <c>FontSize</c>. Devolve despertadas e o balão do nó sob o mouse (sempre nome do ramo + efeito).
    /// </summary>
    private (int Total, TdRuneTip? Tip) AddRuneNodes(List<TdUiItem> items, TdMatchState m, DipRect area, RuneSizes sz, float world, (float X, float Y) center)
    {
        var types = m.Content.Runes.Types;
        var frontier = new HashSet<string>(m.Frontier);
        long frag = m.Fragments;
        int total = 0;
        TdRuneTip? tip = null;
        float ns = RuneNodeScale(world / (sz == ModalRune ? ModalWorld : HudWorld));
        foreach (var n in m.RuneWeb)
        {
            bool on = m.IsAwakened(n.Id), av = !on && frontier.Contains(n.Id);
            int ti = Math.Max(0, Array.FindIndex(types, t => t.Id == n.Type));
            var st = on ? TdRuneState.Awake : av ? TdRuneState.Available : TdRuneState.Locked;
            if (on) total++;
            float vis = (on ? sz.On : av ? sz.Available : sz.Locked) * ns, hit = Math.Max(vis, sz.MinHit * ns);
            float cx = area.X + area.W / 2f + ((float)n.X - center.X) * world, cy = area.Y + area.H / 2f + ((float)n.Y - center.Y) * world;
            if (cx - hit / 2 < area.X || cy - hit / 2 < area.Y || cx + hit / 2 > area.X + area.W || cy + hit / 2 > area.Y + area.H) continue; // fora da região visível
            items.Add(new(new DipRect(cx - hit / 2, cy - hit / 2, hit, hit), TdUiKind.Button, $"{types[ti].Name}, {RuneStateWord(st)}", TdUiAction.SelectRune,
                PackRune(ti, n.Ring, n.Sector), SArg: n.Id, Right: n.Id == _lastRune ? "last" : null,
                Fill: on ? RuneOn : av ? RuneAvailable : RuneLocked, Selected: _hoverBranch is not null && _hoverBranch == n.Type, FontSize: vis));
            if (n.Id != _hoverRune) continue;
            string name = "Runa de " + types[ti].Name;
            int cost = m.RuneCost(n.Id);
            string effect = types[ti].Text + (n.Power > 1 ? $" (valor ×{n.Power})" : "");
            tip = st switch
            {
                TdRuneState.Awake => new TdRuneTip(n.Id, st, ti, name, "DESPERTADA", effect, "Efeito ativo", TdTone.Good, cx, cy, vis),
                TdRuneState.Available => new TdRuneTip(n.Id, st, ti, name, "DISPONÍVEL", effect,
                    frag >= cost ? $"Clique para despertar · {cost} fragmento{(cost == 1 ? "" : "s")}" : $"Custo {cost} fragmento{(cost == 1 ? "" : "s")} · faltam {cost - frag}", frag >= cost ? TdTone.Accent : TdTone.Bad, cx, cy, vis),
                _ => new TdRuneTip(n.Id, st, ti, name, "BLOQUEADA", effect, $"Desperte uma vizinha antes · custo {cost}", TdTone.Dim, cx, cy, vis),
            };
        }
        return (total, tip);
    }

    /// <summary>
    /// Legenda de ramos (TD.30e): um chip por tipo com glifo, nome e n/total despertadas (<c>Right</c>). Passar o mouse destaca os nós do ramo na teia
    /// (<see cref="HoverBranch"/>); <c>Highlight</c> acende todos enquanto o tutorial do 1º dano está ensinando. Mesma linha na modal e no HUD.
    /// </summary>
    private void AddRuneLegend(List<TdUiItem> items, TdMatchState m, float x, float y, float w, float h)
    {
        var types = m.Content.Runes.Types;
        const float gap = 4f;
        float cw = (w - (types.Length - 1) * gap) / types.Length;
        for (int i = 0; i < types.Length; i++)
        {
            int total = 0;
            foreach (var n in m.RuneWeb) if (n.Type == types[i].Id) total++;
            items.Add(new(new DipRect(x + i * (cw + gap), y, cw, h), TdUiKind.Button, types[i].Name, TdUiAction.RuneBranch, i, SArg: types[i].Id,
                Right: $"{m.AwakenedOfType(types[i].Id)}/{total}", Highlight: LegendHighlight, Selected: _hoverBranch == types[i].Id));
        }
    }

    /// <summary>Ramo da legenda sob o mouse (destaque dos nós); nulo fora da legenda.</summary>
    public string? HoverBranch => _hoverBranch;

    /// <summary>A legenda de ramos está acesa porque a janela de runas foi aberta pelo cartão do tutorial (some ao passar o mouse num chip ou fechar).</summary>
    public bool LegendHighlight => _legendHint && RuneWindowOpen;

    /// <summary>
    /// Aba Runas da modal (Main.dc.html): teia de 688×318 com FRAGMENTOS no canto esquerdo, contador no direito, dica embaixo, a legenda de ramos
    /// abaixo da teia e o balão do nó sob o mouse (desenhado pelo renderer, dentro da janela). Lê só o <see cref="TdMatchState"/>; o despertar envia <see cref="AwakenRune"/>.
    /// </summary>
    /// <summary>Item invisível que marca onde fica o centro da teia (núcleo) na tela: o renderer desenha o fundo e as curvas em torno dele. <c>FontSize</c> = DIP por unidade normalizada.</summary>
    private static void AddWebCenter(List<TdUiItem> items, DipRect area, (float X, float Y) center, float world) =>
        items.Add(new(new DipRect(area.X + area.W / 2f + (0.5f - center.X) * world, area.Y + area.H / 2f + (0.5f - center.Y) * world, 0f, 0f), TdUiKind.Label, "", SArg: "webctr", FontSize: world));

    private TdRuneTip? AddRuneWeb(List<TdUiItem> items, TdMatchState m)
    {
        var area = new DipRect(16, 102, 688, ModalWebHeight);
        if (!_webCentered) { _webCentered = true; var f = RuneFocus(m); _webPanX = f.X; _webPanY = f.Y; _webZoom = 1f; }
        items.Add(new(area, TdUiKind.Label, "", SArg: "webbg"));
        float world = ModalWorld * _webZoom;
        AddWebCenter(items, area, (_webPanX, _webPanY), world);
        var (total, tip) = AddRuneNodes(items, m, area, ModalRune, world, (_webPanX, _webPanY));
        bool done = m.Frontier.Count == 0;
        long frag = m.Fragments;
        items.Add(new(new DipRect(area.X + 14, area.Y + 12, 200, 24), TdUiKind.Label, frag.ToString(System.Globalization.CultureInfo.InvariantCulture), SArg: "rfrag"));
        items.Add(new(new DipRect(area.X + area.W - 214, area.Y + 12, 200, 34), TdUiKind.Label, $"{total}/{m.RuneWeb.Count} runas", SArg: "rtot"));
        items.Add(new(new DipRect(area.X + 14, area.Y + area.H - 24, area.W - 28, 16), TdUiKind.Label,
            RunePathNotice ?? (done ? "Teia completa." : m.CanAwakenAny ? "Clique numa runa tracejada para despertar · arraste/setas: mover · roda: zoom" : "Faltam fragmentos para a fronteira · arraste/setas: mover · roda: zoom"),
            SArg: "rhint"));
        AddRuneLegend(items, m, area.X, area.Y + area.H + 6f, area.W, ModalLegendHeight);
        return tip;
    }

    /// <summary>
    /// Janela de runas do HUD (HUDRunas.dc.html): popover de 288 DIP no cantinho, seta apontando para o botão Runas, cabeçalho (título,
    /// n/total, fragmentos, fechar), mini-teia 268×168, dica e o link "Teia completa" (abre a modal na aba Runas). Não pausa o jogo.
    /// </summary>
    private TdPopoverView BuildRuneWindow(TdMatchState m)
    {
        var items = new List<TdUiItem>(48)
        {
            new(new DipRect(RuneWindowWidth - 10 - 24, 8, 24, 24), TdUiKind.Button, "Fechar runas", TdUiAction.Close),
        };
        var area = new DipRect(10, 40, RuneWebWidth, RuneWebHeight);
        items.Add(new(area, TdUiKind.Label, "", SArg: "webbg"));
        var focus = (_webPanX, _webPanY);
        AddWebCenter(items, area, focus, HudWorld * _webZoom);
        var (total, tip) = AddRuneNodes(items, m, area, HudRune, HudWorld * _webZoom, focus);
        items.Add(new(new DipRect(10, 8, 150, 24), TdUiKind.Label, $"{total}/{m.RuneWeb.Count}", SArg: "rcount"));
        items.Add(new(new DipRect(RuneWindowWidth - 10 - 24 - 6 - 54, 9, 54, 22), TdUiKind.Label, m.Fragments.ToString(System.Globalization.CultureInfo.InvariantCulture), SArg: "rpill"));
        // TD.30e: a legenda de ramos cabe (8 chips compactos em 268 DIP) e a janela cresce só HudLegendHeight + 4.
        AddRuneLegend(items, m, area.X, area.Y + area.H + 4f, area.W, HudLegendHeight);
        float fy = area.Y + area.H + 4f + HudLegendHeight + 6f;
        items.Add(new(new DipRect(10, fy, 168, 30), TdUiKind.Label, RunePathNotice ?? "Arraste para mover · roda para zoom", SArg: "rhintw"));
        items.Add(new(new DipRect(RuneWindowWidth - 10 - 100, fy, 100, 30), TdUiKind.Button, "Teia completa", TdUiAction.ModalTab, (int)TdUiMode.Runes, SArg: "rlink"));
        items.Add(new(new DipRect(160, 9, 24, 22), TdUiKind.Button, "Centralizar", TdUiAction.RecenterRunes, SArg: "rcenter", Tip: "Centralizar teia"));
        float boxH = fy + 30 + 10;
        // Seta: aponta para o centro do botão Runas (a janela e o HUD ancoram na mesma borda direita).
        return new TdPopoverView("Runas", [.. items], RuneWindowWidth, boxH + TdPopoverView.ArrowH, 0, ArrowX: RuneWindowWidth / 2f, Tip: tip, RuneWindow: true);
    }

    /// <summary>X da seta de um popover de <paramref name="width"/> DIP ancorado na borda direita do HUD, apontando para o centro do botão Runas.</summary>
    private float RuneButtonArrowX(float width)
    {
        var hud = BuildHud();
        float arrowX = width - 12;
        foreach (var it in hud.Items)
            if (it.Action == TdUiAction.RuneWindow) { arrowX = width - (hud.Width - (it.Rect.X + it.Rect.W / 2f)); break; }
        return arrowX;
    }

    /// <summary>Composição do Main.dc.html para Runas, Torres, Jogo e Como jogar (modal 720×480, conteúdo a partir de y=100).</summary>
    private TdRuneTip? RelayoutTab(TdPopoverView view, List<TdUiItem> items)
    {
        TdRuneTip? runeTip = null;
        TdUiItem Put(TdUiItem it, float x, float y, float w, float h) => it with { Rect = new DipRect(x, y, w, h) };
        var src = view.Items;
        foreach (var it in src) if (it.Action == TdUiAction.Close) items.Add(Put(it, 674, 4, 36, 36));
        switch (Mode)
        {
            case TdUiMode.Runes:
                runeTip = AddRuneWeb(items, _match());
                break;
            case TdUiMode.Help:
            {
                int row = 0, col = 0;
                foreach (var it in src)
                {
                    if (it.Action == TdUiAction.Close) continue;
                    if (it.Action == TdUiAction.RedoTutorial) { items.Add(Put(it, 16, _tutorial is { Active: true } ? 360 : 400, 180, 40)); continue; }
                    if (it.SArg == "help-section")
                    {
                        if (col != 0) { row++; col = 0; }
                        int visible = row++ - _helpScroll;
                        if (visible >= 0 && visible < HelpVisibleRows) items.Add(Put(it, 16, 108 + visible * 36, 688, 30));
                        continue;
                    }
                    int display = row - _helpScroll;
                    if (display >= 0 && display < HelpVisibleRows)
                    {
                        var card = it;
                        if (it.Text.Length > 36)
                        {
                            int split = it.Text.LastIndexOf(' ', 36);
                            if (split > 0) card = it with { Text = it.Text[..split], Sub = it.Text[(split + 1)..] };
                        }
                        items.Add(Put(card, 16 + col * 352, 108 + display * 36, 336, 30));
                    }
                    if (++col == 2) { col = 0; row++; }
                }
                break;
            }
            case TdUiMode.Towers:
            {
                var m = _match();
                string fusion = m.FusionUnlocked ? $"Fusão: até {m.FusionMaxComponents} torres" : $"Fusão: patente {TdRank.DisplayName((TdRankLevel)m.Content.Fusion.MinRank)}";
                items.Add(new(new DipRect(16, 104, 470, 18), TdUiKind.Label, $"Torres em campo: {m.Towers.Count}  |  {fusion}", Tone: TdTone.Dim, SArg: "hdr"));
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
                }
                break;
            }
            case TdUiMode.Menu:
                AddJogoTab(src, items);
                break;
        }
        return runeTip;
    }

    /// <summary>Lista rolável da aba atual da modal (Construir: catálogo; Torres: torres em campo). Nulo se cabe inteira.</summary>
    private TdScrollInfo? ScrollInfo(TdMatchState m)
    {
        if (Mode == TdUiMode.Help)
        {
            int rows = HelpRows(BuildHelp(m).Items);
            return rows > HelpVisibleRows ? new(rows, HelpVisibleRows, Math.Clamp(_helpScroll, 0, rows - HelpVisibleRows), 108f, HelpVisibleRows * 36f - 6f) : null;
        }
        if (Mode == TdUiMode.Settings) return SettingsScrollInfo();
        if (Mode == TdUiMode.Towers)
            return m.Towers.Count > ModalTowerRows ? new(m.Towers.Count, ModalTowerRows, _towerScroll, 132f, ModalTowerRows * 52f - 6f) : null;
        if (Mode == TdUiMode.Build && !BuildActive)
        {
            int count = m.Content.Towers.Count(t => t.Category == _towerCategory && t.MinRank <= m.CurrentRankId);
            return count > ModalBuildRows ? new(count, ModalBuildRows, _buildScroll, 158f, ModalBuildRows * 51f - 3f) : null;
        }
        return null;
    }

    private static int HelpRows(TdUiItem[] items)
    {
        int row = 0, col = 0;
        foreach (var it in items)
        {
            if (it.Action is TdUiAction.Close or TdUiAction.RedoTutorial) continue;
            if (it.SArg == "help-section") { if (col != 0) { row++; col = 0; } row++; }
            else if (++col == 2) { row++; col = 0; }
        }
        return row + (col == 0 ? 0 : 1);
    }

    /// <summary>Rolagem da lista da aba atual (roda do mouse: 1 linha por clique; teclado: página). Negativo sobe. Devolve true se a aba tem lista rolável.</summary>
    public bool ScrollBy(int rows)
    {
        var m = _match();
        if (ScrollInfo(m) is not { } info) return false;
        int max = Math.Max(0, info.Total - info.Visible);
        int next = Math.Clamp(info.Offset + rows, 0, max);
        if (next != info.Offset)
        {
            if (Mode == TdUiMode.Settings) _settingsScroll = next;
            else if (Mode == TdUiMode.Towers) _towerScroll = next;
            else if (Mode == TdUiMode.Help) _helpScroll = next;
            else _buildScroll = next;
            Changed?.Invoke();
        }
        return true;
    }

    /// <summary>Rolagem pelo teclado: -1/+1 = uma página (ou uma linha com <paramref name="line"/>).</summary>
    public bool ScrollKey(int dir, bool line = false) =>
        ScrollInfo(_match()) is { } i && ScrollBy(dir * (line ? 1 : i.Visible));

    /// <summary>Ordem das abas da modal: a primeira (Jogo: patente, XP e progresso) é também a aberta ao iniciar.</summary>
    public static readonly TdUiMode[] ModalTabs = [TdUiMode.Menu, TdUiMode.Build, TdUiMode.Runes, TdUiMode.Towers, TdUiMode.Bestiary, TdUiMode.Map, TdUiMode.Help];

    private TdPopoverView? Modalize(TdPopoverView? view)
    {
        if (view is null) return null;
        var items = new List<TdUiItem>();
        var modes = ModalTabs;
        string[] labels = ["Jogo", "Construir", "Runas", "Torres", "Inimigos", "Fase / Selo", "Como jogar"];
        for (int i = 0; i < modes.Length; i++)
            items.Add(new(new DipRect(16 + i * 98, 52, 96, 36), TdUiKind.Button, labels[i],
                TdUiAction.ModalTab, (int)modes[i], Selected: Mode == modes[i]));
        if (Mode == TdUiMode.End && view.Width == 720f && view.Height == 480f)
        {
            // TD.41: cartão de fim de corrida já vem em coordenadas da modal (blocos medidos, sem reescala).
            items.AddRange(view.Items);
            return new TdPopoverView($"{view.Title}   ¢{_match().Credits}", [.. items], 720, 480, 0);
        }
        var currentRank = _host.Rank.Current;
        items.Add(new(new DipRect(16, 10, 150, 24), TdUiKind.Label, TdRank.DisplayName(currentRank), Arg: (int)currentRank, SArg: "rank-chip"));
        items.Add(new(new DipRect(634, 4, 36, 36), TdUiKind.Button, "Configurações", TdUiAction.SettingsOpen, Icon: TdIcon.Gear, Selected: Mode == TdUiMode.Settings, Tip: "Configurações", SArg: "cfg-gear"));
        if (Mode == TdUiMode.Settings)
        {
            items.Add(new(new DipRect(674, 4, 36, 36), TdUiKind.Button, "X", TdUiAction.Close));
            AddSettings(items);
            return new TdPopoverView("Configurações", [.. items], 720, 480, 0, Scroll: SettingsScrollInfo());
        }
        if (Mode == TdUiMode.Bestiary)
        {
            items.Add(new(new DipRect(674, 4, 36, 36), TdUiKind.Button, "X", TdUiAction.Close));
            AddBestiary(items);
            AddModalTutorial(items);
            return new TdPopoverView($"{view.Title}   ¢{_match().Credits}", [.. items], 720, 480, 0);
        }
        if (Mode == TdUiMode.Map)
        {
            items.Add(new(new DipRect(674, 4, 36, 36), TdUiKind.Button, "X", TdUiAction.Close));
            AddMapModal(items, _match());
            AddModalTutorial(items);
            return new TdPopoverView($"{view.Title}   ¢{_match().Credits}", [.. items], 720, 480, 0);
        }
        float factor = Math.Min(1f, 402f / Math.Max(1, view.Height));
        int towerRow = 0, categoryColumn = 0;
        if (Mode is TdUiMode.Menu or TdUiMode.Towers or TdUiMode.Help or TdUiMode.Runes)
        {
            var tip = RelayoutTab(view, items);
            AddModalTutorial(items);
            return new TdPopoverView($"{view.Title}   ¢{_match().Credits}", [.. items], 720, 480, 0, Tip: tip, Scroll: ScrollInfo(_match()));
        }
        foreach (var item in view.Items)
        {
            var r = item.Rect;
            if (item.Action == TdUiAction.Close)
            {
                items.Add(item with { Rect = new DipRect(674, 4, 36, 36) });
                continue;
            }
            if (Mode == TdUiMode.Build && item.Kind == TdUiKind.Label && item.SArg != "catalog-hint" && towerRow == 0)
            {
                items.Add(item with { Rect = new DipRect(16, 103, 688, 18) });
                items.Add(new(new DipRect(504, 103, 200, 18), TdUiKind.Label, "+25% por torre igual", Tone: TdTone.Dim, SArg: "hdrR"));
                continue;
            }
            if (item.Action == TdUiAction.ChooseTower)
            {
                items.Add(item with { Rect = new DipRect(16, 158 + towerRow++ * 51, 688, 48) });
                continue;
            }
            if (item.Kind == TdUiKind.Label && item.SArg == "catalog-hint")
            {
                items.Add(item with { Rect = new DipRect(16, 430, 672, 20) });
                continue;
            }
            if (item.Action == TdUiAction.TowerCategory)
            {
                items.Add(item with { Rect = new DipRect(16 + categoryColumn++ * 172, 126, 164, 26) });
                continue;
            }
            items.Add(item with { Rect = new DipRect(r.X * 2.2f + 20, 96 + r.Y * factor * .92f, r.W * 2.2f, r.H * factor * .92f) });
        }
        AddModalTutorial(items);
        return new TdPopoverView($"{view.Title}   ¢{_match().Credits}", [.. items], 720, 480, 0, Scroll: ScrollInfo(_match()));
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
        string right = r.Next is { } n ? $"{r.Total}/{r.NextThreshold} XP > {TdRank.DisplayName(n)}" : $"{r.Total} XP";
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

    private static TdPopoverView Finish(string title, Stack s, float anchorX, string? iconTower = null, string? iconModule = null)
    {
        var items = new List<TdUiItem>(s.Items.Count + 1)
        {
            new(new DipRect(s.Width - HeaderH - 2, 2, HeaderH, HeaderH - 4), TdUiKind.Button, "X", TdUiAction.Close),
        };
        items.AddRange(s.Items);
        return new TdPopoverView(title, [.. items], s.Width, s.Y + Pad, anchorX, iconTower, IconModule: iconModule);
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
        EnsureTowerCategory(m);
        string title = _moveTowerId > 0 ? "Reposicionar torre" : _chosenX < 0 ? "Construir: escolha o ponto" : "Construir: escolha a torre";
        if (BuildActive)
        {
            s.Add(TdUiKind.Label, _moveTowerId > 0 ? "Clique num ponto livre da faixa." : "Clique num ponto livre da faixa para construir.", RowH, tone: TdTone.Normal);
            s.Add(TdUiKind.Label, "ESC ou botao direito cancela.", RowH - 4, tone: TdTone.Dim);
            if (_notice is not null) s.Add(TdUiKind.Label, _notice, RowH - 4, tone: TdTone.Warn);
            return Finish(title, s, _anchorX);
        }
        s.Add(TdUiKind.Label, "Escolha uma torre e toque na faixa para posicionar.", RowH - 4, tone: TdTone.Dim);
        foreach (var (id, name) in TowerCategories)
            if (m.Content.Towers.Any(t => t.Category == id && t.MinRank <= m.CurrentRankId))
                s.Add(TdUiKind.Button, name, 24, TdUiAction.TowerCategory, sarg: id, selected: _towerCategory == id);
        var towers = m.Content.Towers;
        int count = towers.Count(t => t.Category == _towerCategory && t.MinRank <= m.CurrentRankId);
        _buildScroll = Math.Clamp(_buildScroll, 0, Math.Max(0, count - ModalBuildRows));
        int seen = 0;
        for (int i = 0; i < towers.Length; i++)
        {
            var d = towers[i];
            if (d.Category != _towerCategory || d.MinRank > m.CurrentRankId) continue;
            if (seen++ < _buildScroll || seen > _buildScroll + ModalBuildRows) continue;
            bool locked = m.CurrentRankId < d.MinRank;
            int owned = m.TowerCount(i);
            long price = m.NextTowerCost(i); // custo crescente por tipo (SDD-29 §5.1)
            bool poor = m.Credits < price;
            string right = locked ? $"patente {TdRank.DisplayName((TdRankLevel)d.MinRank)}" : $"${price}";
            string sub = locked ? $"Desbloqueia na patente {TdRank.DisplayName((TdRankLevel)d.MinRank)}" : $"alc {d.Range:0}  |  Fraqueza: {d.WeaknessText}";
            s.Add(TdUiKind.Button, owned > 0 ? $"{d.Name}  x{owned}" : d.Name, 44f, TdUiAction.ChooseTower, sarg: d.Id, right: right, sub: sub, enabled: !locked && !poor,
                tone: locked ? TdTone.Dim : poor ? TdTone.Bad : TdTone.Normal);
        }
        s.Add(TdUiKind.Label, NextTowerHint(m), 20, sarg: "catalog-hint", tone: TdTone.Dim);
        return Finish("Construir: escolha a torre", s, _anchorX);
    }

    private TdPopoverView BuildInspector(TdMatchState m)
    {
        int ti = FindTower(m, _selectedTowerId);
        var s = new Stack(PopoverWidth);
        if (ti < 0) return Finish("Torre", s, _anchorX);
        var t = m.Towers[ti];
        var d = m.Content.Towers[t.DefIndex];
        string title = t.HasModule ? m.TowerDisplayName(t) : $"{d.Name} x{m.TowerCount(t.DefIndex)}";
        string? moduleId = t.HasModule ? m.Content.Towers[t.ModuleDefIndex].Id : null;
        if (t.HasModule)
        {
            var names = t.Components().Select(i => m.Content.Towers[i].Name).ToArray();
            s.Add(TdUiKind.Label, $"Fundida: {string.Join(" + ", names.Take(3))}", RowH - 4, tone: TdTone.Accent);
            if (names.Length > 3) s.Add(TdUiKind.Label, $"+ {string.Join(" + ", names.Skip(3))}", RowH - 4, tone: TdTone.Accent);
            if (m.RecipeBonus(t) is { } bonus) QuizText(s, "Receita: " + bonus.Text + ".", FuseFont, TdTone.Good, TdIcon.Star);
        }
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
        if (CanReceiveModule(m, t))
        {
            bool unlocked = m.FusionUnlocked;
            s.Add(TdUiKind.Button, unlocked ? "Fundir com outra torre" : $"Fundir (patente {TdRank.DisplayName((TdRankLevel)m.Content.Fusion.MinRank)})", RowH, TdUiAction.FuseOpen, arg: t.Id, enabled: unlocked, tone: unlocked ? TdTone.Accent : TdTone.Dim);
        }
        long refund = m.SellValue(t.Id); // fungivel: taxa x custo(tipo, n-1) (SDD-29 §5.2)
        s.Add(TdUiKind.Button, $"Vender ({Math.Round(m.Content.Stages.Rules.SellRefund * 100):0}%)", RowH, TdUiAction.Sell, arg: t.Id, right: $"+${refund}", tone: TdTone.Bad);
        return Finish(title, s, t.Placed ? m.TowerDip(t) : _anchorX, d.Id, moduleId);
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
        s.Add(TdUiKind.Label, m.FusionUnlocked ? $"Fusão: até {m.FusionMaxComponents} torres" : $"Fusão: patente {TdRank.DisplayName((TdRankLevel)m.Content.Fusion.MinRank)}", RowH, tone: TdTone.Dim);
        if (m.Towers.Count == 0) s.Add(TdUiKind.Label, "Nenhuma torre. Use Construir.", RowH, tone: TdTone.Dim);
        float sellW = 98f;
        _towerScroll = Math.Clamp(_towerScroll, 0, Math.Max(0, m.Towers.Count - ModalTowerRows));
        for (int i = _towerScroll; i < Math.Min(m.Towers.Count, _towerScroll + ModalTowerRows); i++)
        {
            var t = m.Towers[i];
            var d = m.Content.Towers[t.DefIndex];
            long refund = m.SellValue(t.Id);
            s.Items.Add(new TdUiItem(new DipRect(Pad, s.Y, PopoverWidth - 3 * Pad - sellW, RowH + 2), TdUiKind.Button,
                t.HasModule ? m.TowerDisplayName(t) : $"{d.Name} x{m.TowerCount(t.DefIndex)}", TdUiAction.OpenTower, t.Id, SArg: d.Id,
                Sub: t.HasModule ? m.Content.Towers[t.ModuleDefIndex].Id : null, Tone: t.Placed ? TdTone.Normal : TdTone.Warn));
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
        foreach (var ability in m.AbilityStatuses)
        {
            bool ready = m.Phase == MatchPhase.Onda && ability.Unlocked && ability.CooldownTicks == 0 && ability.UsesLeft != 0;
            var def = m.Content.Abilities.First(a => a.Id == ability.Id);
            string reason = !ability.Unlocked && def.MinRank is int rank ? $"Requer patente {TdRank.DisplayName((TdRankLevel)rank)}" : "Pausado";
            s.Add(TdUiKind.Button, AbilityShort(ability.Id), RowH, TdUiAction.ResumeAbility, sarg: ability.Id,
                enabled: ready, right: ready ? "Usar e fechar" : reason);
        }
        if (_confirmRestart)
        {
            s.Add(TdUiKind.Label, "Reiniciar? Torres ficam", RowH, tone: TdTone.Warn);
            s.Add(TdUiKind.Button, "Sim", RowH, TdUiAction.Restart, tone: TdTone.Bad);
            s.Add(TdUiKind.Button, "Nao", RowH, TdUiAction.RestartCancel);
        }
        else s.Add(TdUiKind.Button, m.Options.PersistentRun ? "Reiniciar corrida" : "Reiniciar fase", RowH, TdUiAction.RestartPrompt);
        s.Add(TdUiKind.Button, "Como jogar", RowH, TdUiAction.HowTo, tone: TdTone.Accent);
        s.Add(TdUiKind.Button, "Vender torres", RowH, TdUiAction.TowersOpen);
        s.Add(TdUiKind.Button, "Próxima onda", RowH, TdUiAction.Preview);

        var options = _host.GameOptions;
        s.Add(TdUiKind.Button, $"Faixa: {options.BandHeight} DIP", RowH, TdUiAction.GameOption, 0);
        s.Add(TdUiKind.Button, options.AutoStart ? "Auto-início: ligado" : "Auto-início: desligado", RowH, TdUiAction.GameOption, 1);
        s.Add(TdUiKind.Button, options.Sound ? "Som: ligado" : "Som: desligado", RowH, TdUiAction.GameOption, 2);
        s.Add(TdUiKind.Button, $"Volume: {options.Volume}%", RowH, TdUiAction.GameOption, 3);
        s.Items[^1] = s.Items[^1] with { Fill = Math.Clamp(options.Volume, 0, 100) / 100f };
        s.Add(TdUiKind.Button, options.Questions ? "Perguntas V/F: ligado" : "Perguntas V/F: desligado", RowH, TdUiAction.GameOption, 4);
        s.Add(TdUiKind.Button, options.Fade ? "Esmaecer: ligado" : "Esmaecer: desligado", RowH, TdUiAction.GameOption, 5);
        s.Add(TdUiKind.Button, options.Continuous ? "Ondas contínuas: ligado" : "Ondas contínuas: desligado", RowH, TdUiAction.GameOption, 6, right: "Próxima corrida");
        s.Add(TdUiKind.Button, options.PanelPinned ? "Painel de torres fixo: ligado" : "Painel de torres fixo: desligado", RowH, TdUiAction.GameOption, 7);
        s.Add(TdUiKind.Button, $"Avisos: {options.NoticeSize switch { 0 => "Desligado", 2 => "Médio", 3 => "Grande", _ => "Pequeno" }}", RowH, TdUiAction.GameOption, 8);
        foreach (int v in AllowedSpeeds)
            s.Add(TdUiKind.Button, TdSpeed.Label(v), RowH, TdUiAction.SetSpeed, v, selected: TdSpeed.Resolve(options.GameSpeed, AllowedSpeeds) == v);
        if (_confirmWipe)
        {
            s.Add(TdUiKind.Label, "Apagar todo o progresso?", RowH, tone: TdTone.Warn);
            s.Add(TdUiKind.Button, "Sim, apagar", RowH, TdUiAction.WipeConfirm, tone: TdTone.Bad);
            s.Add(TdUiKind.Button, "Nao", RowH, TdUiAction.WipeCancel);
        }
        else
        {
            s.Add(TdUiKind.Button, "Sair da partida", RowH, TdUiAction.Exit, tone: TdTone.Bad);
            s.Add(TdUiKind.Button, "Recomeçar do zero", RowH, TdUiAction.WipePrompt, tone: TdTone.Bad);
        }
        return Finish("Jogo", s, 0);
    }

    /// <summary>Posições (DIP, relativas ao mapa) das 6 regiões da rota no artboard "Mapa de fases + Selos".</summary>
    private static readonly (float X, float Y)[] MapPoints = [(136, 250), (92, 116), (214, 96), (340, 122), (196, 186), (334, 262)];

    /// <summary>
    /// Mapa de fases + Selos (artboard Fases.dc.html, 720×480): subabas e resumo no topo; Fases = mapa com a rota das regiões e painel de detalhe da fase
    /// escolhida (jogar pede confirmação aqui mesmo); Selos = banner e árvore de melhorias em 3 ramos, compra em ordem. Cada elemento é um item
    /// com SArg "map*" que o renderer desenha; os botões reais (subabas, regiões, jogar, comprar) trazem a ação.
    /// </summary>
    private void AddMapModal(List<TdUiItem> items, TdMatchState m)
    {
        const float x0 = 16f, w = 688f, mt = 138f, mh = 315f; // conteúdo embutido do artboard: 688×357 a partir de y=96
        var stages = m.Content.Stages.Stages;
        items.Add(new(new DipRect(x0 + 3, 99, 72, 28), TdUiKind.Button, "Fases", TdUiAction.MapTab, 0, Selected: !_mapSeals, SArg: "mapsubtab"));
        items.Add(new(new DipRect(x0 + 78, 99, 72, 28), TdUiKind.Button, "Selos", TdUiAction.MapTab, 1, Selected: _mapSeals, SArg: "mapsubtab"));
        int stars = 0, crowns = 0;
        foreach (var st in stages) { stars += _host.StageStars(st.Id); if (_host.StageCrown(st.Id)) crowns++; }
        items.Add(new(new DipRect(704 - 150, 104, 44, 18), TdUiKind.Label, stars.ToString(System.Globalization.CultureInfo.InvariantCulture), Arg: 0, Right: $"/{stages.Length * 3}", SArg: "mapsum"));
        items.Add(new(new DipRect(704 - 100, 104, 44, 18), TdUiKind.Label, crowns.ToString(System.Globalization.CultureInfo.InvariantCulture), Arg: 1, Right: $"/{stages.Length}", SArg: "mapsum"));
        items.Add(new(new DipRect(704 - 50, 104, 50, 18), TdUiKind.Label, _host.SealsAvailable.ToString(System.Globalization.CultureInfo.InvariantCulture), Arg: 2, SArg: "mapsum"));
        if (!_mapSeals) { AddMapStages(items, m, x0, mt, mh); return; }

        int total = 0, bought = 0;
        foreach (var b in m.Content.Meta.Branches) foreach (var n in b.Nodes) { total++; if (_host.NodePurchased(n.Id)) bought++; }
        items.Add(new(new DipRect(x0, mt, w, 46), TdUiKind.Label, "Melhorias compradas valem a partir da próxima partida.", Arg: _host.SealsAvailable,
            Right: $"{bought}/{total} compradas", SArg: "mapbanner"));
        float colW = (w - 2 * 12f) / 3f;
        int col = 0;
        foreach (var b in m.Content.Meta.Branches)
        {
            float cx = x0 + col * (colW + 12f);
            int got = b.Nodes.Count(n => _host.NodePurchased(n.Id));
            items.Add(new(new DipRect(cx, mt + 54, colW, 26), TdUiKind.Label, b.Name, Arg: col, Right: $"{got}/{b.Nodes.Length}", SArg: "mapbranch"));
            float y = mt + 84f;
            for (int i = 0; i < b.Nodes.Length; i++)
            {
                var n = b.Nodes[i];
                bool have = _host.NodePurchased(n.Id), prevOk = i == 0 || _host.NodePurchased(b.Nodes[i - 1].Id);
                bool avail = !have && prevOk, afford = avail && _host.SealsAvailable >= n.Cost;
                items.Add(new(new DipRect(cx, y, colW, 48), TdUiKind.Button, n.Name, TdUiAction.BuyNode, col * 16 + i, SArg: n.Id,
                    Right: have ? "COMPRADO" : $"{n.Cost} selo{(n.Cost > 1 ? "s" : "")}", Fill: have ? 1f : avail ? 0.5f : 0f, Enabled: afford, Sub: "mapnode"));
                if (i < b.Nodes.Length - 1) items.Add(new(new DipRect(cx + colW / 2f - 1f, y + 48, 2, 8), TdUiKind.Label, "", Arg: col, Fill: have ? 1f : 0f, Highlight: false, SArg: "maplink"));
                y += 56f;
            }
            col++;
        }
    }

    private void AddMapStages(List<TdUiItem> items, TdMatchState m, float x0, float mt, float mh)
    {
        var stages = m.Content.Stages.Stages;
        int sel = _mapSel;
        if (sel == 0 || !stages.Any(s => s.Id == sel)) sel = _host.StageUnlocked(m.StageId) ? m.StageId : stages[0].Id;
        _mapSel = sel;
        items.Add(new(new DipRect(x0, mt, 404, mh), TdUiKind.Label, "ROTA DE REGIÕES", SArg: "mapbg"));
        for (int i = 0; i < stages.Length && i < MapPoints.Length; i++)
        {
            var st = stages[i];
            bool open = _host.StageUnlocked(st.Id) && TdStageGate.Allowed(st, _host.Rank.Current), on = st.Id == sel;
            float size = on ? 38f : 32f;
            var (px, py) = MapPoints[i];
            items.Add(new(new DipRect(x0 + px - size / 2f, mt + py - size / 2f, size, size), TdUiKind.Button, st.Title, TdUiAction.SelectStage, st.Id, SArg: "mapnode",
                Fill: !open ? 0f : _host.StageStars(st.Id) > 0 ? 1f : 0.5f, Selected: on, Sub: _host.StageStars(st.Id).ToString(System.Globalization.CultureInfo.InvariantCulture), Right: (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Tip: !TdStageGate.Allowed(st, _host.Rank.Current) ? TdRank.DisplayName((TdRankLevel)st.MinRank) : null));
        }
        var cur = stages.First(s => s.Id == sel);
        bool rankLocked = !TdStageGate.Allowed(cur, _host.Rank.Current);
        bool locked = !_host.StageUnlocked(cur.Id) || rankLocked;
        int cs = _host.StageStars(cur.Id), best = _host.StageBestScore(cur.Id);
        string status = locked ? "BLOQUEADA" : cs == 3 ? "CONCLUÍDA" : cs > 0 ? "EM ANDAMENTO" : "DISPONÍVEL";
        float dx = x0 + 404 + 12, dw = 704 - dx, cardH = mh - 40 - 8 - 14 - 8;
        items.Add(new(new DipRect(dx, mt, dw, cardH), TdUiKind.Label, cur.Title, Arg: cs | (_host.StageCrown(cur.Id) ? 4 : 0) | (_host.StageMedals(cur.Id) << 3) | (cur.Id << 8) | (cur.MinRank << 16),
            Sub: status, Right: best > 0 ? best.ToString(System.Globalization.CultureInfo.InvariantCulture) : "—", Enabled: !locked, SArg: "mapdetail", Tip: rankLocked ? "rank" : null));
        float by = mt + cardH + 8;
        if (_mapConfirm && !locked)
        {
            float half = (dw - 6) / 2f;
            items.Add(new(new DipRect(dx, by, half, 40), TdUiKind.Button, "Sim, jogar", TdUiAction.MapPlayConfirm, cur.Id, SArg: "mapplay"));
            items.Add(new(new DipRect(dx + half + 6, by, half, 40), TdUiKind.Button, "Cancelar", TdUiAction.MapPlayCancel, SArg: "mapcancel"));
            items.Add(new(new DipRect(dx, by + 48, dw, 14), TdUiKind.Label, $"Reiniciar a partida atual e jogar a fase {cur.Id}?", SArg: "mapwarn"));
            return;
        }
        items.Add(new(new DipRect(dx, by, dw, 40), TdUiKind.Button,
            rankLocked ? $"Requer {TdRank.DisplayName((TdRankLevel)cur.MinRank)}" : locked ? $"Bloqueada · vença a fase {Math.Max(1, cur.Id - 1)}" : cs > 0 ? "Jogar de novo" : "Jogar esta fase", TdUiAction.MapPlay, cur.Id, Enabled: !locked, SArg: "mapplay"));
        items.Add(new(new DipRect(dx, by + 48, dw, 14), TdUiKind.Label, "Jogar uma fase reinicia a partida atual.", SArg: "mapwarn"));
    }

    /// <summary>Aba Fases: o conteúdo (mapa + selos) vem de <see cref="AddMapModal"/> no Modalize; aqui só o título.</summary>
    private static TdPopoverView BuildMap(TdMatchState m) => Finish("Fases e selos", new Stack(PopoverWidth), 0);

    private TdPopoverView BuildEnd(TdMatchState m)
    {
        var s = new Stack(260f);
        bool won = m.Phase == MatchPhase.Vitoria;
        var r = m.Result;
        string title = won ? $"VITORIA  {new string('*', m.Stars)}{new string('-', 3 - m.Stars)}" : "CORRIDA ENCERRADA";
        s.Add(TdUiKind.Label, won ? $"Fase {m.StageId} concluida. Impacto dos vazamentos: {m.IntegrityLost}"
            : $"Corrida encerrada na fase {m.StageNumber}, onda {m.WaveNumber}/{m.TotalWaves}.", TwoLineH - 8, tone: won ? TdTone.Good : TdTone.Bad);
        if (won)
        {
            var o = _host.LastOutcome;
            AddBadges(s, r.Crown, (int)r.Medals, r.Crown ? "COROA" : null);
            var earned = MedalNames(r.Medals);
            s.Add(TdUiKind.Label, earned.Length > 0 ? $"Medalhas: {earned}" : "Sem medalhas desta vez.", TwoLineH - 8, tone: TdTone.Dim);
            s.Add(TdUiKind.Label, $"Pontuacao: {r.Score}", RowH - 4, right: o.NewRecord ? "NOVO RECORDE" : $"Recorde {_host.StageBestScore(m.StageId)}",
                tone: o.NewRecord ? TdTone.Accent : TdTone.Normal);
            if (o.BonusSeals > 0) s.Add(TdUiKind.Label, $"Selos bonus: +{o.BonusSeals} · veja a aba Fase / Selo", RowH - 4, tone: TdTone.Good);
            AddRankBar(s, _host.Rank);
            if (_winGuide)
            {
                s.Add(TdUiKind.Label, "Coroa: vença sem vazar nada.", 14f, tone: TdTone.Accent, icon: TdIcon.Crown);
                s.Add(TdUiKind.Label, "Medalhas: desafios extras.", 14f, tone: TdTone.Accent, icon: TdIcon.Medal);
                s.Add(TdUiKind.Label, "Patente: sobe com XP, desce se vazar.", 14f, tone: TdTone.Accent, icon: TdIcon.Target);
            }
        }
        if (won && m.StageId < _host.StageCount && _host.StageUnlocked(m.StageId + 1))
            s.Add(TdUiKind.Button, "Proxima fase", 38, TdUiAction.NextStage, tone: TdTone.Good);
        s.Add(TdUiKind.Button, won ? "Jogar de novo" : m.Options.PersistentRun ? "Reiniciar" : "Reiniciar fase", 38, TdUiAction.Restart);
        s.Add(TdUiKind.Button, "Mapa", 38, TdUiAction.MapOpen);
        return Finish(title, s, 0);
    }

}
