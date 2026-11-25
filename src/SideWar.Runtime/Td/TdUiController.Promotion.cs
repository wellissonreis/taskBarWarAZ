using SideWar.Core.Td;
using SideWar.Runtime.Geometry;

namespace SideWar.Runtime.Td;

public sealed partial class TdUiController
{
    private TdRankLevel _promotionPreviousRank;

    private static TdPopoverView BuildPromotion(TdMatchState match, TdRankLevel previous, TdRankLevel rank)
    {
        const float width = 330;
        var items = new List<TdUiItem>();
        float y = 30;
        void Line(string text, TdTone tone = TdTone.Normal, string? tag = null, int arg = 0)
        {
            items.Add(new(new DipRect(14, y, width - 28, 22), TdUiKind.Label, text, Arg: arg, SArg: tag, Tone: tone));
            y += 22;
        }

        Line($"Promovido a {TdRank.DisplayName(rank)}!", TdTone.Accent, "demotion-rank", (int)rank);
        y += 6;
        if ((int)rank >= match.Content.Fusion.MinRank)
            Line($"Fusão: até {match.FusionLimitAt((int)rank)} torres", TdTone.Good);
        var towers = match.Content.Towers.Where(t => t.MinRank > (int)previous && t.MinRank <= (int)rank).Select(t => t.Name);
        var abilities = match.Content.Abilities.Where(a => a.MinRank is int r && r > (int)previous && r <= (int)rank).Select(a => a.Name);
        foreach (var name in towers) Line($"Torre: {name}");
        foreach (var name in abilities) Line($"Habilidade: {name}");
        if (match.Options.InitialReputation is not null)
            foreach (var stage in match.Content.Stages.Stages.Where(s => s.MinRank > (int)previous && s.MinRank <= (int)rank).GroupBy(s => s.MinRank).Select(g => g.First()))
                Line($"{stage.Title} liberada");
        return new TdPopoverView("PROMOÇÃO!", items.ToArray(), width, y + 10, 0f);
    }
}
