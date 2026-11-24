using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Core.Tests;

public sealed class TdRankUnlockTests
{
    [Fact]
    public void Tower_fusion_and_ability_follow_current_rank_and_survive_demotion()
    {
        var c = TdContentLoader.LoadEmbedded();
        var m = TdMatchState.Create(c, 6, Rng.Seed(1, 54), null, TdMatchOptions.Classic with { InitialReputation = 0 });
        m.Apply(new LayoutChanged(TdSanityBot.TypicalLayout()));
        typeof(TdMatchState).GetProperty("Credits")!.SetValue(m, 100000L);
        Assert.Equal(CommandReason.TowerLocked, m.Apply(new BuildTower("balancer", 300)).Reason);
        Assert.False(m.FusionUnlocked);
        Assert.False(m.AbilityStatuses.Single(a => a.Id == "firewall").Unlocked);
        typeof(TdMatchState).GetProperty("GateReputation")!.SetValue(m, TdRank.Threshold(TdRankLevel.Pleno));
        Assert.True(m.Apply(new BuildTower("balancer", 300)).IsOk);
        Assert.True(m.Apply(new BuildTower("bolt", 600)).IsOk);
        int baseId = m.Towers[0].Id;
        Assert.True(m.FusionUnlocked);
        Assert.True(m.AbilityStatuses.Single(a => a.Id == "firewall").Unlocked);
        typeof(TdMatchState).GetProperty("GateReputation")!.SetValue(m, 0);
        Assert.Equal(2, m.Towers.Count);
        Assert.Equal(CommandReason.TowerLocked, m.Apply(new BuildTower("balancer", 900)).Reason);
        Assert.Equal(CommandReason.FusionLocked, m.Apply(new FuseTower(baseId, "bolt")).Reason);
        Assert.False(m.AbilityStatuses.Single(a => a.Id == "firewall").Unlocked);
        typeof(TdMatchState).GetProperty("GateReputation")!.SetValue(m, TdRank.Threshold(TdRankLevel.Junior));
        Assert.Equal(CommandReason.TowerLocked, m.Apply(new FuseTower(baseId, "scaleset")).Reason);
    }
    [Fact]
    public void Content_validator_rejects_unknown_rank_ids()
    {
        var c = TdContentLoader.LoadEmbedded();
        var towers = (TdTowerDef[])c.Towers.Clone();
        towers[0] = towers[0] with { MinRank = 6 };
        var abilities = (TdAbilityDef[])c.Abilities.Clone();
        abilities[0] = abilities[0] with { MinRank = -1 };
        var errors = TdContentValidator.Validate(c with { Towers = towers, Abilities = abilities, Fusion = c.Fusion with { MinRank = 6 } });
        Assert.Contains(errors, e => e.Contains("td-towers") && e.Contains("minRank"));
        Assert.Contains(errors, e => e.Contains("td-abilities") && e.Contains("minRank"));
        Assert.Contains(errors, e => e.Contains("td-fusion.minRank"));
    }

}
