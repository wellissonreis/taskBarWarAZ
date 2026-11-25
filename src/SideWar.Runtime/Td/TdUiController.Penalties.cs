using SideWar.Core.Td;
using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Td;

/// <summary>Avisos de canto das duas punições: derrota para o chefe (jogo pausado pelo Core) e fusões perdidas no rebaixamento.</summary>
public sealed partial class TdUiController
{
    private int _seenBossDefeats;
    private int _seenFusionLosses;
    private int _bossDefeatWave; // > 0: derrota aguardando retomada manual
    private bool _bossCardDismissed;
    private readonly List<int> _fusionLost = [];
    private TdMatchState? _penaltyMatch;

    /// <summary>O chefe venceu e o jogo está parado esperando o jogador. Aparece mesmo com os avisos desligados.</summary>
    public bool BossDefeatOpen => _bossDefeatWave > 0 && !_bossCardDismissed && !BossHidden;

    private void ResetPenalties(TdMatchState m)
    {
        _penaltyMatch = m;
        _seenBossDefeats = m.BossDefeats;
        _seenFusionLosses = m.FusionLossCount;
        _bossDefeatWave = 0;
        _fusionLost.Clear();
    }

    private void SyncPenalties(TdMatchState m)
    {
        if (!ReferenceEquals(_penaltyMatch, m)) ResetPenalties(m);
        if (m.BossDefeats > _seenBossDefeats)
        {
            _seenBossDefeats = m.BossDefeats;
            _bossDefeatWave = Math.Max(1, m.TotalWaves - 10);
            _bossCardDismissed = false;
            _userPaused = true; // o botão pausa/iniciar mostra "retomar"
            _pendingBossRewindWave = 0;
            Changed?.Invoke();
        }
        if (m.FusionLossCount > _seenFusionLosses)
        {
            _seenFusionLosses = m.FusionLossCount;
            _fusionLost.AddRange(m.LastFusionLoss);
            Changed?.Invoke();
        }
    }

    private TdPopoverView BuildBossDefeatCard(TdMatchState m)
    {
        const float width = 270;
        var items = new List<TdUiItem>
        {
            new(new DipRect(10, 28, 250, 18), TdUiKind.Label, $"Voltou para a onda {_bossDefeatWave}", Tone: TdTone.Warn),
            new(new DipRect(10, 48, 250, 18), TdUiKind.Label, "Reconfigure as torres e dê play", Tone: TdTone.Dim),
        };
        float y = 68;
        if (m.FinalBossImmune)
        {
            items.Add(new(new DipRect(10, 68, 250, 18), TdUiKind.Label, $"Chefe imune: teia {m.AwakenedCount}/{m.RuneWeb.Count}", Tone: TdTone.Warn));
            y = 88;
        }
        items.Add(new(new DipRect(10, y + 2, 250, 24), TdUiKind.Button, "Ver torres", TdUiAction.BossDefeatDismiss, Tone: TdTone.Accent));
        return new TdPopoverView("O chefe venceu", [.. items], width, y + 34, 0f);
    }
}
