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
        _fusionLost.Clear();
        UpdateModalPause();
        Changed?.Invoke();
    }

    private TdPopoverView BuildDemotionCard()
    {
        const float width = 250;
        var items = new List<TdUiItem>
        {
            new(new DipRect(12, 30, 226, 26), TdUiKind.Label, $"Rebaixado a {TdRank.DisplayName(_demotionRank)}", Arg: (int)_demotionRank, SArg: "demotion-rank"),
            new(new DipRect(12, 63, 226, 20), TdUiKind.Label, $"Faltam {_demotionMissingXp} XP para recuperar", Tone: TdTone.Dim),
        };
        float y = 87;
        if (_fusionLost.Count > 0)
        {
            var towers = _match().Content.Towers;
            // Uma linha por tipo perdido (no máximo 4; o resto é resumido).
            var lost = _fusionLost.GroupBy(i => i).OrderByDescending(g => g.Count()).Select(g => $"{towers[g.Key].Name} x{g.Count()}").ToArray();
            items.Add(new(new DipRect(12, y, 226, 16), TdUiKind.Label, $"Fusões perdidas: {_fusionLost.Count}", Tone: TdTone.Warn));
            y += 16;
            foreach (var line in lost.Take(4))
            {
                items.Add(new(new DipRect(12, y, 226, 16), TdUiKind.Label, line, Tone: TdTone.Dim));
                y += 16;
            }
            if (lost.Length > 4)
            {
                items.Add(new(new DipRect(12, y, 226, 16), TdUiKind.Label, $"e outros {lost.Length - 4} tipos", Tone: TdTone.Dim));
                y += 16;
            }
        }
        if (BossDefeatOpen)
        {
            items.Add(new(new DipRect(12, y, 226, 16), TdUiKind.Label, "Chefe venceu: ajuste e dê play", Tone: TdTone.Warn));
            y += 16;
        }
        items.Add(new(new DipRect(12, y + 3, 226, 24), TdUiKind.Button, "Continuar", TdUiAction.DemotionContinue, Tone: TdTone.Accent));
        return new TdPopoverView("Rebaixamento", [.. items], width, y + 39, 0f);
    }
}
