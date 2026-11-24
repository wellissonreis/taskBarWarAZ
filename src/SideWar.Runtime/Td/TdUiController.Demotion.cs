using SideWar.Core.Td;
using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Td;

public sealed partial class TdUiController
{
    private bool _demotionPending;
    private TdRankLevel _demotionRank;
    private int _demotionMissingXp;

    public bool DemotionCardOpen => _demotionPending && !BossHidden;

    private void StartDemotion(TdRankLevel rank, TdRankProgress progress)
    {
        _demotionPending = true;
        _demotionRank = rank;
        _demotionMissingXp = Math.Max(0, progress.NextThreshold - progress.Total);
        UpdateModalPause();
        Changed?.Invoke();
    }

    private void ContinueAfterDemotion()
    {
        if (!_demotionPending) return;
        _demotionPending = false;
        UpdateModalPause();
        Changed?.Invoke();
    }

    private TdPopoverView BuildDemotionCard()
    {
        const float width = 250;
        var items = new List<TdUiItem>
        {
            new(new DipRect(12, 12, 226, 26), TdUiKind.Label, $"Rebaixado a {TdRank.DisplayName(_demotionRank)}", Arg: (int)_demotionRank, SArg: "demotion-rank"),
            new(new DipRect(12, 45, 226, 20), TdUiKind.Label, $"Faltam {_demotionMissingXp} XP para recuperar", Tone: TdTone.Dim),
            new(new DipRect(12, 72, 226, 24), TdUiKind.Button, "Continuar", TdUiAction.DemotionContinue, Tone: TdTone.Accent),
        };
        return new TdPopoverView("Rebaixamento", [.. items], width, 108, 0f);
    }
}
