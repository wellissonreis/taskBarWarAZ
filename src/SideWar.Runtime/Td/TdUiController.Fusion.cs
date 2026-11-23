using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Input;

namespace SideWar.Runtime.Td;

/// <summary>
/// TD.37b (SDD-37 RF-37.10): UI da fusão de torres. O inspetor ganha "Fundir"; o fluxo roda num cartão no cantinho (sem pausa, sem foco, recolhe ao
/// clicar fora): escolher o módulo, ver o preview (nome do híbrido, custo exato, efeito e, se bloqueado, o motivo) e confirmar. O Core revalida tudo
/// em <see cref="FuseTower"/>; a UI só consulta <see cref="TdMatchState.CheckFusion"/>. O convite persiste até a primeira fusão ou dispensa.
/// </summary>
public sealed partial class TdUiController
{
    public const string FusionTipKey = "fusion-discovery-done";
    public const float FuseWidth = 360f, FuseFont = 14f, FuseTitleFont = 15f, FuseButtonH = 32f;
    public const string FusionTipText = "Fusão liberada! Escolha uma torre na barra e solte sobre outra: ela funde.";

    private int _fuseBase;         // > 0: cartão de fusão aberto sobre esta torre
    private string? _fuseType;     // módulo escolhido (preview); nulo = escolhendo
    private int _fuseAwait;        // torre cuja fusão foi enviada e espera o resultado
    private string? _fuseAwaitName;
    private string? _noticeTitle;
    private bool _fusionChoosingModule;
    private string? _fusionTargetModule;
    public bool FusionTargetActive => _fusionTargetModule is not null && !BossHidden;

    private bool _ftPending, _ftShown;

    /// <summary>Cartão de fusão aberto (a torre ainda existe e a partida segue em Preparo/Onda).</summary>
    public bool FuseCardOpen => _fuseBase > 0 && !BossHidden && Mode == TdUiMode.None && FuseStillValid(_match());

    public int FuseBaseId => _fuseBase;
    public string? FuseChosen => _fuseType;
    public bool FusionTipOpen => _ftShown && !BossHidden && !MatchPaused && Mode == TdUiMode.None && !QuickTowersOpen && !RuneWindowOpen && _fuseBase == 0;
    public bool FusionTipPending => _ftPending;

    private bool FuseStillValid(TdMatchState m) => m.Phase is MatchPhase.Preparo or MatchPhase.Onda && FindTower(m, _fuseBase) >= 0;

    /// <summary>Texto legível do motivo de a fusão estar bloqueada (null = elegível).</summary>
    public static string? FusionReasonText(TdMatchState m, in TdFusionCheck c) => c.Reason switch
    {
        CommandReason.None => null,
        CommandReason.FusionLocked => $"Fusão libera na fase {m.Content.Fusion.MinStage}.",
        CommandReason.InvalidPhase => "Só dá para fundir em Preparo ou durante a Onda.",
        CommandReason.NotPlaced => "Torre sem posição: reposicione antes de fundir.",
        CommandReason.AlreadyFused => "Esta torre já tem módulo (máximo de 2 componentes).",
        CommandReason.TowerLocked => "Esse módulo ainda não está desbloqueado.",
        CommandReason.TowerTypeLimit => "Limite desse tipo atingido (conta também os módulos).",
        CommandReason.InsufficientCredits => $"Faltam ${c.Missing} créditos.",
        CommandReason.TowerNotFound => "A torre não existe mais.",
        _ => "Fusão indisponível agora.",
    };

    /// <summary>Descrição curta do efeito do módulo (par nomeado ou regra genérica por papel). Mostra só o módulo, nunca promete a segunda torre inteira.</summary>
    public static string FusionEffectText(TdMatchState m, in TdFusionCheck c, string moduleName)
    {
        string e = c.Effect;
        if (e.StartsWith("generic:", StringComparison.Ordinal))
        {
            return e[8..] switch
            {
                "attack" => $"Módulo {moduleName}: a cada 3 s causa 35% do dano dele em quem estiver no alcance dele.",
                "control" => $"Módulo {moduleName}: a cada 3 s aplica lentidão de 15% por 2 s.",
                "support" => $"Módulo {moduleName}: reforça outras torres no alcance (+5% de dano; nunca a própria).",
                "economy" => $"Módulo {moduleName}: adiciona metade da renda dele.",
                _ => $"Módulo {moduleName}.",
            };
        }
        return e switch
        {
            "hitDamage" => "Cada tiro causa dano elétrico extra (35%) no alvo principal.",
            "hitSlow" => "Cada raio também desacelera o alvo.",
            "hitSplash" => "Cada raio explode em área (alcance do Canhão), só no solo.",
            "hitChain" => "Cada raio salta para mais 1 inimigo com 40% do dano.",
            "hitCorrode" => "Cada tiro corrói 25% da armadura do alvo por 3 s (não acumula).",
            "nodeSlow" => "Os inimigos retidos pelos nós ficam desacelerados.",
            "periodicDamage" => "A cada 3 s causa 35% de dano elétrico a quem estiver no alcance da base.",
            "periodicSlow" => "A cada 3 s desacelera inimigos no alcance do Gelo.",
            _ => $"Módulo {moduleName}.",
        };
    }

    /// <summary>Motivo curto (cabe no título do painel) de a fusão por soltar estar bloqueada.</summary>
    public static string DropReasonText(TdMatchState m, in TdFusionCheck c) => c.Reason switch
    {
        CommandReason.FusionLocked => $"só na fase {m.Content.Fusion.MinStage}",
        CommandReason.AlreadyFused => "torre já fundida",
        CommandReason.TowerLocked => "módulo bloqueado",
        CommandReason.TowerTypeLimit => "limite do tipo",
        CommandReason.InsufficientCredits => $"faltam ${c.Missing}",
        CommandReason.InvalidPhase => "fora de Preparo/Onda",
        CommandReason.NotPlaced => "torre sem posição",
        _ => "par inválido",
    };

    /// <summary>Soltar na torre: envia <see cref="FuseTower"/> (o Core revalida e cobra), dispara o efeito curto e sai do modo posicionar como após construir.</summary>
    private void DropFuse(int towerId, string moduleId, in TdFusionCheck check)
    {
        _fuseAwait = towerId;
        _fuseAwaitName = check.Name;
        _fxTowerId = towerId;
        _fxStart = _now();
        _send(new FuseTower(towerId, moduleId));
        if (QuickTowersOpen) { ResetBuild(); Changed?.Invoke(); }
        else SetMode(TdUiMode.Build);
        _dropTowerId = 0;
    }

    // ---- fusão por arrasto (Parte E): arrastar uma torre já colocada e soltar sobre outra ---------------------------------

    private int _dragTowerId;
    private bool _dragMoved;
    private float _dragStartX;
    public const float DragThresholdDip = 5f;

    /// <summary>Arrasto de uma torre colocada em curso (a faixa só mostra o fantasma; a torre fica no lugar até soltar).</summary>
    public bool DraggingTower => _dragTowerId > 0;

    /// <summary>
    /// A plataforma pergunta se, neste ponto (DIP do espaço de desenho, Y já sem a folga), há uma torre COLOCADA que pode ser arrastada agora.
    /// Só então a faixa deixa de ser click-through; chip sem posição, espaço livre, modo posicionar, chefe oculto e fases fora de jogo = não.
    /// </summary>
    public bool CanGrabTowerAt(float xDip, float yDip)
    {
        var m = _match();
        if (BuildActive || BossHidden || m.Phase is not (MatchPhase.Preparo or MatchPhase.Onda)) return false;
        if (TdHitTester.Test(m, xDip, yDip) is not { Kind: HitKind.TdTower } hit) return false;
        int ti = FindTower(m, hit.TowerId);
        return ti >= 0 && m.Towers[ti].Placed;
    }

    /// <summary>Botão pressionado sobre uma torre posicionada: começa o arrasto (sem mover = clique, que abre o inspetor ao soltar).</summary>
    public bool BeginTowerDrag(int towerId, float xDip)
    {
        var m = _match();
        int ti = FindTower(m, towerId);
        if (ti < 0 || !m.Towers[ti].Placed || BuildActive || BossHidden || m.Phase is not (MatchPhase.Preparo or MatchPhase.Onda)) return false;
        _dragTowerId = towerId;
        _dragMoved = false;
        _dragStartX = xDip;
        return true;
    }

    /// <summary>Mouse movendo durante o arrasto: atualiza o fantasma e o preview da fusão sobre a torre-alvo (igual ao da barra).</summary>
    public void TowerDragMoved(float xDip)
    {
        if (_dragTowerId == 0) return;
        var m = _match();
        if (FindTower(m, _dragTowerId) < 0) { CancelTowerDrag(); return; }
        if (!_dragMoved && MathF.Abs(xDip - _dragStartX) < DragThresholdDip) return;
        _dragMoved = true;
        int w = TdBand.WidthDip(m);
        _ghostX = TdPlacement.ToLogical((int)MathF.Round(Math.Clamp(xDip, 0f, w)), w);
        _dropTowerId = 0;
        _ghostValid = false;
        if (TdHitTester.Test(m, Math.Clamp(xDip, 0f, w - 1f), TdBand.HeightDip / 2f) is { Kind: HitKind.TdTower } hit && hit.TowerId != _dragTowerId)
        {
            var (_, _, reason) = m.CheckFusionTowers(hit.TowerId, _dragTowerId);
            _dropTowerId = hit.TowerId;
            _dropCheck = new TdFusionCheck(reason, 0, 0, m.FusionNameOfTowers(hit.TowerId, _dragTowerId), false, "");
            _ghostValid = reason == CommandReason.None;
            int ti = FindTower(m, hit.TowerId);
            if (ti >= 0) _ghostX = m.Towers[ti].X;
        }
        Changed?.Invoke();
    }

    /// <summary>Soltou: sobre um par válido funde (a mais forte vira base, sem custo); fora, ou par inválido, a torre fica onde estava; sem mover é clique (inspetor).</summary>
    public void TowerDragEnd(float xDip)
    {
        if (_dragTowerId == 0) return;
        TowerDragMoved(xDip);
        int dragged = _dragTowerId, target = _dropTowerId;
        bool moved = _dragMoved;
        var check = _dropCheck;
        CancelTowerDrag();
        if (!moved) { OpenInspector(dragged); return; }
        if (target == 0 || !check.IsOk) { Changed?.Invoke(); return; }
        var m = _match();
        var (baseId, _, _) = m.CheckFusionTowers(target, dragged);
        _fuseAwait = baseId;
        _fuseAwaitName = check.Name;
        _fxTowerId = baseId;
        _fxStart = _now();
        _send(new FuseTowers(target, dragged));
    }

    /// <summary>Cancela o arrasto sem mexer na partida (Esc, partida acabou).</summary>
    public void CancelTowerDrag()
    {
        if (_dragTowerId == 0) return;
        _dragTowerId = 0;
        _dragMoved = false;
        _dropTowerId = 0;
        _ghostValid = false;
        _ghostX = -1;
        Changed?.Invoke();
    }

    private void OpenFuse(int towerId)
    {
        var m = _match();
        if (FindTower(m, towerId) < 0) return;
        SetMode(TdUiMode.None);
        _fuseBase = towerId;
        _fuseType = null;
        _selectedTowerId = towerId; // mantém o contorno da base na faixa
        Changed?.Invoke();
    }

    private void CloseFuse(bool keepSelection = false)
    {
        if (_fuseBase == 0) return;
        _fuseBase = 0;
        _fuseType = null;
        if (!keepSelection && Mode == TdUiMode.None) _selectedTowerId = 0;
        Changed?.Invoke();
    }

    private void ActivateFuse(in TdUiItem item)
    {
        var m = _match();
        switch (item.Action)
        {
            case TdUiAction.FuseOpen: OpenFuse(item.Arg); break;
            case TdUiAction.FusePick when item.SArg is not null: _fuseType = item.SArg; Changed?.Invoke(); break;
            case TdUiAction.FuseBack: _fuseType = null; Changed?.Invoke(); break;
            case TdUiAction.FuseClose: CloseFuse(); break;
            case TdUiAction.FuseConfirm when _fuseType is not null:
            {
                var check = m.CheckFusion(_fuseBase, _fuseType);
                int ti = FindTower(m, _fuseBase);
                if (!check.IsOk || ti < 0) { Changed?.Invoke(); break; } // preview já mostra o motivo; nada é enviado
                _fuseAwait = _fuseBase;
                _fuseAwaitName = check.Name;
                _send(new FuseTower(_fuseBase, _fuseType));
                CloseFuse();
                break;
            }
        }
    }

    /// <summary>Depois do comando: avisa o resultado no cantinho (fundida ou recusada pelo Core).</summary>
    private void SyncFuse(TdMatchState m)
    {
        if (_fuseBase > 0 && Mode != TdUiMode.None) CloseFuse(keepSelection: true);
        else if (_fuseBase > 0 && !FuseStillValid(m)) CloseFuse();
        if (_fuseAwait > 0)
        {
            int ti = FindTower(m, _fuseAwait);
            string? name = _fuseAwaitName;
            _fuseAwait = 0;
            _fuseAwaitName = null;
            if (!BossHidden)
            {
                bool ok = ti >= 0 && m.Towers[ti].HasModule;
                if (ok) { _ack |= Ack.Fusion; _onb?.MarkTipSeen(FusionTipKey); _ftShown = _ftPending = false; }
                _noticeTitle = ok ? "Torre fundida" : "Fusão recusada";
                _noticeText = ok ? (ti >= 0 ? m.TowerDisplayName(m.Towers[ti]) : name ?? "") : "O Core recusou: confira créditos e limites.";
                _noticeUntil = _now() + StageNoticeSeconds;
                Changed?.Invoke();
            }
        }
        SyncFusionTip(m);
    }

    private bool CanShowFusionTip(TdMatchState m) =>
        !BossHidden && !MatchPaused && !ModalOpen && !BuildActive && !_outsideCollapsed && !_fusionChoosingModule && !FusionTargetActive && !RuneWindowOpen && !QuizShowing(m) && !NoticeOpen && !EndCardOpen && !RuneCardOpen
        && !AlertCardOpen && _fuseBase == 0 && (Mode == TdUiMode.None || QuickTowersOpen) && m.Phase is MatchPhase.Preparo or MatchPhase.Onda;

    private void SyncFusionTip(TdMatchState m)
    {
        if (_onb is null) return;
        if (!_ftShown && !_ftPending && m.FusionUnlocked && !_onb.TipSeen(FusionTipKey)) _ftPending = true;
        if (_ftPending && !_ftShown && CanShowFusionTip(m))
        {
            _ftShown = true;
            _ftPending = false;
            Changed?.Invoke();
            return;
        }
        if (!_ftShown) return;
        if (ModalOpen || m.Phase is not (MatchPhase.Preparo or MatchPhase.Onda)) { _ftShown = false; _ftPending = true; }
    }

    private void DismissFusionTip()
    {
        _onb?.MarkTipSeen(FusionTipKey);
        _ftShown = _ftPending = false;
        Changed?.Invoke();
    }

    private void ResetFusion(bool clearSeen)
    {
        _fuseBase = _fuseAwait = _fxTowerId = _dropTowerId = _dragTowerId = 0;
        _fuseType = null;
        _fuseAwaitName = null;
        _ftShown = _ftPending = false;
        _fusionChoosingModule = false;
        _fusionTargetModule = null;
        if (clearSeen) _onb?.ClearTip(FusionTipKey);
    }

    private static TdPopoverView BuildFusionTip()
    {
        var s = new Stack(QuizWidth);
        QuizText(s, FusionTipText, QuizOptionFont, icon: TdIcon.Star);
        s.Items.Add(new TdUiItem(new DipRect(Pad, s.Y, s.Width - 2 * Pad, 34f), TdUiKind.Button, "Experimentar pelo inspetor", TdUiAction.FusionTry, Tone: TdTone.Accent, FontSize: QuizOptionFont));
        s.Y += 38f;
        s.Items.Add(new TdUiItem(new DipRect(Pad, s.Y, s.Width - 2 * Pad, 34f), TdUiKind.Button, "Escolher módulo e base", TdUiAction.FusionModule, FontSize: QuizOptionFont));
        s.Y += 38f;
        var items = new List<TdUiItem>(s.Items.Count + 1)
        {
            new(new DipRect(s.Width - HeaderH - 2, 2, HeaderH, HeaderH - 4), TdUiKind.Button, "X", TdUiAction.FusionTipDismiss),
        };
        items.AddRange(s.Items);
        return new TdPopoverView("Fusão", [.. items], s.Width, s.Y + Pad, 0f);
    }

    private void TryFusion()
    {
        var m = _match();
        int towerId = m.Towers.Where(t => t.Placed && !t.HasModule).Select(t => t.Id).FirstOrDefault();
        _ftShown = false;
        _ftPending = true;
        if (towerId > 0) OpenFuse(towerId);
        else { _noticeTitle = "Fusão"; _noticeText = "Construa uma torre simples para usar como base."; _noticeUntil = _now() + StageNoticeSeconds; Changed?.Invoke(); }
    }

    private void OpenFusionModuleChooser()
    {
        _ftShown = false; _ftPending = true;
        _fusionChoosingModule = true;
        Changed?.Invoke();
    }

    private TdPopoverView BuildFusionGesture(TdMatchState m)
    {
        var s = new Stack(FuseWidth);
        if (FusionTargetActive)
            QuizText(s, "Clique sobre uma torre na faixa para ver o preview. Esc cancela.", FuseFont);
        else
        {
            QuizText(s, "Escolha o módulo: ele será comprado, não junta torres existentes.", FuseFont);
            float half = (s.Width - 2 * Pad - 4f) / 2f;
            int col = 0;
            float rowY = s.Y;
            foreach (var d in m.Content.Towers.Where(t => t.UnlockStage <= m.UnlockedTowerStage))
            {
                s.Items.Add(new TdUiItem(new DipRect(Pad + col * (half + 4f), rowY, half, FuseButtonH), TdUiKind.Button,
                    d.Name, TdUiAction.FusionSelectModule, SArg: d.Id, FontSize: FuseFont));
                if (++col == 2) { col = 0; rowY += FuseButtonH + 4f; }
            }
            s.Y = rowY + (col > 0 ? FuseButtonH + 4f : 0f);
        }
        s.Add(TdUiKind.Button, "Cancelar", FuseButtonH, TdUiAction.Close);
        return Finish("Experimentar fusão", s, 0);
    }

    private TdPopoverView BuildFuseCard(TdMatchState m)
    {
        int ti = FindTower(m, _fuseBase);
        var t = m.Towers[ti];
        var baseDef = m.Content.Towers[t.DefIndex];
        var s = new Stack(FuseWidth);
        string? iconModule = null;
        string title = "Fundir";
        if (_fuseType is null)
        {
            QuizText(s, $"{baseDef.Name}: escolha o módulo (a base mantém alcance e mira).", FuseFont, TdTone.Dim);
            float half = (s.Width - 2 * Pad - 4f) / 2f;
            int col = 0;
            var towers = m.Content.Towers;
            float rowY = s.Y;
            for (int i = 0; i < towers.Length; i++)
            {
                var d = towers[i];
                if (d.UnlockStage > m.UnlockedTowerStage) continue;
                var c = m.CheckFusion(_fuseBase, d.Id);
                bool poor = c.Reason is CommandReason.InsufficientCredits;
                s.Items.Add(new TdUiItem(new DipRect(Pad + col * (half + 4f), rowY, half, FuseButtonH), TdUiKind.Button, $"{d.Name} ${c.Cost}", TdUiAction.FusePick, SArg: d.Id,
                    Tone: c.IsOk ? TdTone.Normal : poor ? TdTone.Bad : TdTone.Dim, FontSize: FuseFont));
                if (++col == 2) { col = 0; rowY += FuseButtonH + 4f; }
            }
            s.Y = rowY + (col > 0 ? FuseButtonH + 4f : 0f);
            if (!m.FusionUnlocked) QuizText(s, FusionReasonText(m, m.CheckFusion(_fuseBase, baseDef.Id))!, FuseFont, TdTone.Warn);
        }
        else
        {
            var c = m.CheckFusion(_fuseBase, _fuseType);
            var md = m.Content.Towers[Array.FindIndex(m.Content.Towers, x => x.Id == _fuseType)];
            iconModule = md.Id;
            title = "Fundir: preview";
            QuizText(s, c.Name, FuseTitleFont, c.IsOk ? TdTone.Accent : TdTone.Normal);
            QuizText(s, $"{baseDef.Name} + {md.Name}" + (c.Named ? "" : " (combinação genérica)"), FuseFont, TdTone.Dim);
            QuizText(s, FusionEffectText(m, c, md.Name), FuseFont);
            QuizText(s, $"Custo: ${c.Cost}  |  venda atual +${m.SellValue(_fuseBase)}", FuseFont, c.Reason == CommandReason.InsufficientCredits ? TdTone.Bad : TdTone.Good, TdIcon.Coin);
            if (FusionReasonText(m, c) is { } why) QuizText(s, why, FuseFont, TdTone.Bad, TdIcon.Alert);
            float half = (s.Width - 2 * Pad - 4f) / 2f;
            s.Items.Add(new TdUiItem(new DipRect(Pad, s.Y, half, FuseButtonH + 2f), TdUiKind.Button, $"Fundir ${c.Cost}", TdUiAction.FuseConfirm, Tone: TdTone.Good, Enabled: c.IsOk, FontSize: FuseFont));
            s.Items.Add(new TdUiItem(new DipRect(Pad + half + 4f, s.Y, half, FuseButtonH + 2f), TdUiKind.Button, "Voltar", TdUiAction.FuseBack, FontSize: FuseFont));
            s.Y += FuseButtonH + 6f;
        }
        var items = new List<TdUiItem>(s.Items.Count + 1)
        {
            new(new DipRect(s.Width - HeaderH - 2, 2, HeaderH, HeaderH - 4), TdUiKind.Button, "X", TdUiAction.FuseClose),
        };
        items.AddRange(s.Items);
        return new TdPopoverView(title, [.. items], s.Width, s.Y + Pad, 0f, IconTower: baseDef.Id, IconModule: iconModule);
    }
}
