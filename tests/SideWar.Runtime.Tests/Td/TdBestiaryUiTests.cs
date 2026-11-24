using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Td;
using Xunit;

namespace SideWar.Runtime.Tests.Td;

public sealed class TdBestiaryUiTests
{
    private sealed class Host : ITdUiHost
    {
        public readonly Dictionary<string, int> Seen = [];
        public int EncounterCount(string id) => Seen.GetValueOrDefault(id);
        public TdGameOptions GameOptions => new();
        public void SetGameOptions(TdGameOptions options) { }
        public bool Paused => false;
        public void TogglePause() { }
        public void RequestExit() { }
        public bool HudMinimized => false;
        public void SetHudMinimized(bool minimized) { }
        public void StartStage(int stageId) { }
        public int StageCount => 6;
        public bool StageUnlocked(int stageId) => true;
        public int StageStars(int stageId) => 0;
        public bool StageCrown(int stageId) => false;
        public int StageMedals(int stageId) => 0;
        public int StageBestScore(int stageId) => 0;
        public TdRankProgress Rank => TdRank.Progress(0);
        public TdRecordOutcome LastOutcome => default;
        public int SealsAvailable => 0;
        public bool NodePurchased(string nodeId) => false;
        public bool PurchaseNode(string nodeId) => false;
    }

    [Fact]
    public void Locked_and_discovered_entries_and_hovered_variations_are_independent()
    {
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54));
        var host = new Host();
        var ui = new TdUiController(() => match, c => match.Apply(c), host);
        ui.Activate(new(default, TdUiKind.Button, "", TdUiAction.ModalTab, (int)TdUiMode.Bestiary));
        var view = ui.BuildPopover()!;
        Assert.Contains(view.Items, i => i.Text == "Aparece a partir da fase 1" && i.SArg == "best-locked");
        Assert.All(view.Items.Where(i => i.Action == TdUiAction.BestiarySelect), i => Assert.Equal("???", i.Text));
        Assert.DoesNotContain(view.Items, i => i.SArg?.StartsWith("best-variant:", StringComparison.Ordinal) == true);
        ui.Activate(new(default, TdUiKind.Button, "", TdUiAction.BestiarySelect, 4));
        Assert.Contains(ui.BuildPopover()!.Items, i => i.Text == "Aparece a partir da fase 5");
        ui.Activate(new(default, TdUiKind.Button, "", TdUiAction.BestiarySelect, 0));
        var tile = view.Items.First(i => i.Action == TdUiAction.BestiarySelect);
        ui.Hover(tile);
        view = ui.BuildPopover()!;
        Assert.Equal(0, view.Items.Single(i => i.SArg == "best-variant:enxame_elite").Fill);
        host.Seen["swarm"] = 3;
        host.Seen["swarm:elite"] = 1;
        view = ui.BuildPopover()!;
        Assert.Contains(view.Items, i => i.Text == "Enxame" && i.SArg == "best-title");
        Assert.Contains(view.Items, i => i.Text == "3 encontrados");
        Assert.Equal(1, view.Items.Single(i => i.SArg == "best-variant:enxame_elite").Fill);
        ui.Hover(null);
        Assert.DoesNotContain(ui.BuildPopover()!.Items, i => i.SArg?.StartsWith("best-variant:", StringComparison.Ordinal) == true);
        host.Seen["chefe_hydra"] = 1;
        ui.Activate(new(default, TdUiKind.Button, "", TdUiAction.BestiarySelect, 13));
        Assert.Contains(ui.BuildPopover()!.Items, i => i.SArg == "best-tower:balancer");
    }
    [Fact]
    public void Construir_offers_economy_and_scrolls_the_full_catalog()
    {
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 6, Rng.Seed(7, 54));
        var ui = new TdUiController(() => match, c => match.Apply(c), new Host());
        ui.Activate(new(default, TdUiKind.Button, "", TdUiAction.ModalTab, (int)TdUiMode.Build));
        var view = ui.BuildPopover()!;
        Assert.Contains(view.Items, i => i.Text == "Todas as torres liberadas");
        Assert.NotNull(view.Scroll);
        Assert.True(ui.ScrollBy(1));
        Assert.True(ui.BuildPopover()!.Scroll!.Value.Offset > 0);
        var economy = ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.TowerCategory && i.SArg == "economy");
        ui.Activate(economy);
        Assert.Contains(ui.BuildPopover()!.Items, i => i.Action == TdUiAction.ChooseTower && i.SArg == "vault");
    }
}
