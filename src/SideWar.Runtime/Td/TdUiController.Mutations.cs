using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Td;

/// <summary>
/// TD.37d (SDD-37 RF-37.11): UI das mutações de inimigos. Aviso discreto no cantinho (sem foco, sem pausa, sem som, sem modal) com o que a PRÓXIMA onda
/// traz, lido de <see cref="TdMatchState.PreviewNextWaveMutations"/> (idêntica à agenda efetiva): detalhado quando há combinação nova ("Onda traz Tank Rápido"),
/// resumo curto nas seguintes. O aviso nasce assim que a prévia existe (no início da onda anterior ou do Preparo), bem antes do primeiro spawn mutado.
/// Respeita boss key, pausa, clique-fora e as demais UIs do cantinho. Dica de primeira vez embutida no 1º aviso. Também expõe o tooltip do inimigo
/// (nome mutado + traços) para o hover da faixa.
/// </summary>
public sealed partial class TdUiController
{
    public const string MutationTipKey = "mutations-intro";
    public const double MutationWarnSeconds = 10.0, MutationWarnSummarySeconds = 5.0, MutationWarnFirstSeconds = 16.0;
    public const float MutWidth = 340f, MutFont = 14f, MutTitleFont = 15f;
    public const string MutationIntroText = "Inimigos podem misturar traços: Tank Rápido, Voador Tank Lento...";
    public const string MutationHelpText = "Mutantes misturam traços (Tank Rápido).";

    private int _mwKey = int.MinValue;
    private readonly List<TdMutationPreview> _mwCurrent = [];
    private int _mwWave;
    private bool _mwPending, _mwShown, _mwDetailed, _mwFirst;
    private double _mwLeft, _mwClock = -1.0;
    private readonly HashSet<ushort> _mwSeen = [];
    private int _hoverEnemyId;

    /// <summary>Aviso de mutantes aberto no cantinho.</summary>
    public bool MutationWarnOpen => _mwShown && !BossHidden && !MatchPaused;

    public bool MutationWarnPending => _mwPending;

    private bool CanShowMutationWarn(TdMatchState m) =>
        !BossHidden && !MatchPaused && !ModalOpen && !BuildActive && !RuneWindowOpen && !QuizShowing(m) && !NoticeOpen && !EndCardOpen && !RuneCardOpen
        && !AlertCardOpen && !FuseCardOpen && !FusionTipOpen && !_outsideCollapsed && (Mode == TdUiMode.None || QuickTowersOpen)
        && m.Phase is MatchPhase.Preparo or MatchPhase.Onda;

    private void SyncMutationWarn(TdMatchState m)
    {
        double now = _now();
        double dt = _mwClock < 0.0 ? 0.0 : Math.Clamp(now - _mwClock, 0.0, QuizMaxDeltaSeconds);
        _mwClock = now;
        int key = HashCode.Combine(m.RunWavesStarted, m.StageId, m.NextWaveNumber);
        if (key != _mwKey)
        {
            _mwKey = key;
            _mwCurrent.Clear();
            var prev = m.PreviewNextWaveMutations();
            _mwCurrent.AddRange(prev);
            _mwWave = m.NextWaveNumber > 0 ? m.NextWaveNumber : 1;
            _mwPending = _mwCurrent.Count > 0;
            if (_mwShown) { _mwShown = false; Changed?.Invoke(); } // a prévia mudou (a onda começou): o aviso antigo sai
        }
        if (_mwPending && !_mwShown && CanShowMutationWarn(m))
        {
            _mwShown = true;
            _mwPending = false;
            _mwDetailed = false;
            foreach (var g in _mwCurrent) if (_mwSeen.Add(g.Traits)) _mwDetailed = true;
            _mwFirst = _onb is not null && !_onb.TipSeen(MutationTipKey);
            if (_mwFirst) _onb!.MarkTipSeen(MutationTipKey); // contado ao exibir: nunca repete
            _mwLeft = _mwFirst ? MutationWarnFirstSeconds : _mwDetailed ? MutationWarnSeconds : MutationWarnSummarySeconds;
            Changed?.Invoke();
            return;
        }
        if (!_mwShown) return;
        if (ModalOpen || m.Phase is not (MatchPhase.Preparo or MatchPhase.Onda)) { DismissMutationWarn(); return; }
        if (BossHidden || MatchPaused || QuizShowing(m) || NoticeOpen || AlertCardOpen) return; // o tempo congela
        _mwLeft -= dt;
        if (_mwLeft <= 0.0) DismissMutationWarn();
    }

    private void DismissMutationWarn()
    {
        if (!_mwShown) return;
        _mwShown = false;
        Changed?.Invoke();
    }

    private void ResetMutations(bool clearSeen)
    {
        _mwKey = int.MinValue;
        _mwCurrent.Clear();
        _mwPending = _mwShown = false;
        _mwSeen.Clear();
        _hoverEnemyId = 0;
        if (clearSeen) _onb?.ClearTip(MutationTipKey);
    }

    /// <summary>Rótulos das combinações distintas ("Tank Rápido, Voador Tank Lento"), no máximo <paramref name="max"/>; o resto vira "+N".</summary>
    private string MutationLabelsText(TdMatchState m, int max)
    {
        var seen = new List<ushort>(4);
        foreach (var g in _mwCurrent) if (!seen.Contains(g.Traits)) seen.Add(g.Traits);
        var parts = new List<string>(max + 1);
        for (int i = 0; i < seen.Count && i < max; i++) parts.Add(string.Join(" ", m.MutationLabels(seen[i])));
        if (seen.Count > max) parts.Add($"+{seen.Count - max}");
        return string.Join(", ", parts);
    }

    private TdPopoverView BuildMutationWarn(TdMatchState m)
    {
        var s = new Stack(MutWidth);
        int total = 0;
        foreach (var g in _mwCurrent) total += g.Count;
        if (_mwFirst) QuizText(s, MutationIntroText, MutFont, TdTone.Dim, TdIcon.Star);
        if (_mwDetailed || _mwFirst)
        {
            QuizText(s, $"Onda {_mwWave} traz {MutationLabelsText(m, 3)}", MutTitleFont, TdTone.Warn, TdIcon.Alert);
            for (int i = 0; i < _mwCurrent.Count && i < 4; i++)
                QuizText(s, $"{_mwCurrent[i].Count}x {m.MutatedName(_mwCurrent[i].EnemyIndex, _mwCurrent[i].Traits)}", MutFont, TdTone.Normal);
            if (_mwCurrent.Count > 4) QuizText(s, $"+{_mwCurrent.Count - 4} grupos", MutFont, TdTone.Dim);
        }
        else QuizText(s, $"Onda {_mwWave}: {total} mutante{(total == 1 ? "" : "s")} ({MutationLabelsText(m, 2)})", MutFont, TdTone.Dim, TdIcon.Alert);
        var items = new List<TdUiItem>(s.Items.Count + 1)
        {
            new(new DipRect(s.Width - HeaderH - 2, 2, HeaderH, HeaderH - 4), TdUiKind.Button, "X", TdUiAction.MutationWarnDismiss),
        };
        items.AddRange(s.Items);
        return new TdPopoverView("Mutantes", [.. items], s.Width, s.Y + Pad, 0f);
    }

    // ------------------------------------------------------------------ tooltip do inimigo (hover da faixa)

    private static string TraitName(string id) => id switch
    {
        "fast" => "Rápido", "durable" => "Tank", "flying" => "Voador", "slow" => "Lento", "shielded" => "Escudo", "armored" => "Blindado",
        "regen" => "Regenera", "drain" => "Drena", "replicator" => "Replica", "stealth" => "Furtivo", "swarm" => "Enxame", "boss" => "Chefe", "basic" => "Básico",
        _ => id,
    };

    /// <summary>Inimigo sob o mouse (0 = nenhum). O host da janela chama a cada movimento na faixa; só estado, sem efeito na partida.</summary>
    public void HoverEnemy(int enemyId)
    {
        if (_hoverEnemyId == enemyId) return;
        _hoverEnemyId = enemyId;
        Changed?.Invoke();
    }

    public int HoverEnemyId => _hoverEnemyId;

    /// <summary>Tooltip do inimigo sob o mouse (null = nada). Nome mutado, traços adicionados e a união com os nativos, vida/escudo; fonte ≥ 14 DIP, layout medido.</summary>
    public TdPopoverView? BuildEnemyTip()
    {
        if (_hoverEnemyId == 0 || BossHidden) return null;
        var m = _match();
        int idx = -1;
        for (int i = 0; i < m.Enemies.Count; i++) if (m.Enemies[i].Id == _hoverEnemyId) { idx = i; break; }
        if (idx < 0) return null;
        var e = m.Enemies[idx];
        var s = new Stack(MutWidth);
        QuizText(s, m.MutatedName(e.DefIndex, e.Mutations), MutTitleFont, e.Mutations != 0 ? TdTone.Warn : TdTone.Accent);
        if (e.Mutations != 0) QuizText(s, "Mutação: " + string.Join(" + ", m.MutationLabels(e.Mutations)), MutFont, TdTone.Normal, TdIcon.Alert);
        var union = m.EnemyTraitUnion(e).Where(t => t is not ("basic" or "boss")).Select(TraitName).ToArray();
        if (union.Length > 0) QuizText(s, "Traços: " + string.Join(", ", union), MutFont, TdTone.Dim);
        string life = $"Vida {Math.Ceiling(e.Hp):0}/{Math.Ceiling(e.MaxHp):0}" + (e.ShieldHp > 0 ? $"  Escudo {Math.Ceiling(e.ShieldHp):0}" : "");
        QuizText(s, life, MutFont);
        return new TdPopoverView("Inimigo", [.. s.Items], s.Width, s.Y + Pad, 0f);
    }
}
