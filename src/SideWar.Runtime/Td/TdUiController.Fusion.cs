using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Td;

/// <summary>
/// TD.37b (SDD-37 RF-37.10): UI da fusão de torres. O inspetor ganha "Fundir"; o fluxo roda num cartão no cantinho (sem pausa, sem foco, recolhe ao
/// clicar fora): escolher o módulo, ver o preview (nome do híbrido, custo exato, efeito e, se bloqueado, o motivo) e confirmar. O Core revalida tudo
/// em <see cref="FuseTower"/>; a UI só consulta <see cref="TdMatchState.CheckFusion"/>. Dica de primeira vez ao liberar a fusão (fase 3).
/// </summary>
public sealed partial class TdUiController
{
    public const string FusionTipKey = "fusion-unlocked";
    public const double FusionTipSeconds = 20.0;
    public const float FuseWidth = 360f, FuseFont = 14f, FuseTitleFont = 15f, FuseButtonH = 32f;
    public const string FusionTipText = "Fusão liberada! Abra uma torre (aba Torres) e toque em Fundir: ela ganha o módulo de outra torre na mesma posição.";

    private int _fuseBase;         // > 0: cartão de fusão aberto sobre esta torre
    private string? _fuseType;     // módulo escolhido (preview); nulo = escolhendo
    private int _fuseAwait;        // torre cuja fusão foi enviada e espera o resultado
    private string? _fuseAwaitName;
    private string? _noticeTitle;

    private bool _ftPending, _ftShown;
    private double _ftLeft, _ftClock = -1.0;

    /// <summary>Cartão de fusão aberto (a torre ainda existe e a partida segue em Preparo/Onda).</summary>
    public bool FuseCardOpen => _fuseBase > 0 && !BossHidden && Mode == TdUiMode.None && FuseStillValid(_match());

    public int FuseBaseId => _fuseBase;
    public string? FuseChosen => _fuseType;
    public bool FusionTipOpen => _ftShown && !BossHidden && !MatchPaused;
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
                _noticeTitle = ok ? "Torre fundida" : "Fusão recusada";
                _noticeText = ok ? (ti >= 0 ? m.TowerDisplayName(m.Towers[ti]) : name ?? "") : "O Core recusou: confira créditos e limites.";
                _noticeUntil = _now() + StageNoticeSeconds;
                Changed?.Invoke();
            }
        }
        SyncFusionTip(m);
    }

    private bool CanShowFusionTip(TdMatchState m) =>
        !BossHidden && !MatchPaused && !ModalOpen && !BuildActive && !RuneWindowOpen && !QuizShowing(m) && !NoticeOpen && !EndCardOpen && !RuneCardOpen
        && !AlertCardOpen && _fuseBase == 0 && (Mode == TdUiMode.None || QuickTowersOpen) && m.Phase is MatchPhase.Preparo or MatchPhase.Onda;

    private void SyncFusionTip(TdMatchState m)
    {
        if (_onb is null) return;
        double now = _now();
        double dt = _ftClock < 0.0 ? 0.0 : Math.Clamp(now - _ftClock, 0.0, QuizMaxDeltaSeconds);
        _ftClock = now;
        if (!_ftShown && !_ftPending && m.FusionUnlocked && !_onb.TipSeen(FusionTipKey)) _ftPending = true;
        if (_ftPending && !_ftShown && CanShowFusionTip(m))
        {
            _ftShown = true;
            _ftPending = false;
            _ftLeft = FusionTipSeconds;
            _onb.MarkTipSeen(FusionTipKey); // contado ao exibir: nunca repete
            Changed?.Invoke();
            return;
        }
        if (!_ftShown) return;
        if (ModalOpen || m.Phase is not (MatchPhase.Preparo or MatchPhase.Onda)) { DismissFusionTip(); return; }
        if (BossHidden || MatchPaused || QuizShowing(m) || NoticeOpen || AlertCardOpen) return;
        _ftLeft -= dt;
        if (_ftLeft <= 0.0) DismissFusionTip();
    }

    private void DismissFusionTip()
    {
        if (!_ftShown) return;
        _ftShown = false;
        Changed?.Invoke();
    }

    private void ResetFusion(bool clearSeen)
    {
        _fuseBase = _fuseAwait = 0;
        _fuseType = null;
        _fuseAwaitName = null;
        _ftShown = _ftPending = false;
        if (clearSeen) _onb?.ClearTip(FusionTipKey);
    }

    private static TdPopoverView BuildFusionTip()
    {
        var s = new Stack(QuizWidth);
        QuizText(s, FusionTipText, QuizOptionFont, icon: TdIcon.Star);
        s.Items.Add(new TdUiItem(new DipRect(Pad, s.Y, s.Width - 2 * Pad, 34f), TdUiKind.Button, "Entendi", TdUiAction.FusionTipDismiss, Tone: TdTone.Accent, FontSize: QuizOptionFont));
        s.Y += 38f;
        var items = new List<TdUiItem>(s.Items.Count + 1)
        {
            new(new DipRect(s.Width - HeaderH - 2, 2, HeaderH, HeaderH - 4), TdUiKind.Button, "X", TdUiAction.FusionTipDismiss),
        };
        items.AddRange(s.Items);
        return new TdPopoverView("Fusão", [.. items], s.Width, s.Y + Pad, 0f);
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
