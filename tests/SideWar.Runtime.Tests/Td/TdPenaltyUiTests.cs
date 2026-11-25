using System.Reflection;
using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Td;
using Xunit;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Runtime.Tests.Td;

/// <summary>BAL-1: avisos de canto da derrota para o chefe (com pausa) e das fusões perdidas no rebaixamento.</summary>
public sealed class TdPenaltyUiTests
{
    private sealed class Rig
    {
        public readonly TdMatchState Match;
        public readonly TdUiControllerTests.FakeHost Host = new();
        public readonly TdUiController Ui;

        public Rig()
        {
            Match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54), null, TdMatchOptions.Default with { InitialReputation = 7000 });
            Match.Apply(new LayoutChanged(TdSanityBot.TypicalLayout()));
            Host.Score = 7000;
            Ui = new TdUiController(() => Match, c => Match.Apply(c), Host, () => 0);
            Ui.Sync();
        }

        public void LoseToBoss()
        {
            typeof(TdMatchState).GetMethod("BeginWave", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Match, [Match.TotalWaves]);
            for (int i = 0; i < 200_000 && Match.BossDefeats == 0; i++) Match.Step();
            Assert.Equal(1, Match.BossDefeats);
            Ui.Sync();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Boss_defeat_keeps_a_corner_card_until_the_player_resumes(int noticeSize)
    {
        var r = new Rig();
        r.Host.SetGameOptions(r.Host.GameOptions with { NoticeSize = noticeSize });
        r.LoseToBoss();
        Assert.True(r.Match.Paused);
        Assert.True(r.Ui.BossDefeatOpen);
        Assert.True(r.Ui.CornerPopover);
        r.Ui.CollapseOutside();
        var card = r.Ui.BuildPopover()!;
        Assert.Equal("O chefe venceu", card.Title);
        Assert.Contains(card.Items, i => i.Text.Contains($"onda {r.Match.TotalWaves - 10}"));
        Assert.Contains(card.Items, i => i.Text.Contains("Reconfigure as torres"));
        r.Ui.Activate(Assert.Single(card.Items, i => i.Action == TdUiAction.BossDefeatDismiss));
        r.Ui.Sync();
        Assert.False(r.Ui.BossDefeatOpen); // fecha o aviso para reconfigurar
        Assert.True(r.Match.Paused);
        var button = Assert.Single(r.Ui.BuildHud().Items, i => i.Action == TdUiAction.PauseToggle);
        Assert.True(button.Selected);
        r.Ui.Activate(button);
        r.Ui.Sync();
        Assert.False(r.Match.Paused);
        Assert.False(r.Ui.BossDefeatOpen);
    }

    [Fact]
    public void Demotion_card_says_which_fusions_were_lost()
    {
        var r = new Rig();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        int bolt = Array.FindIndex(r.Match.Content.Towers, t => t.Id == "bolt");
        ((List<int>)typeof(TdMatchState).GetField("_lastFusionLoss", flags)!.GetValue(r.Match)!).AddRange([bolt, bolt]);
        typeof(TdMatchState).GetField("_fusionLossCount", flags)!.SetValue(r.Match, 1);
        r.Host.Score = 4000;
        r.Ui.Sync();
        var card = r.Ui.BuildPopover()!;
        Assert.Contains(card.Items, i => i.Text.Contains("Fusões perdidas: 2"));
        Assert.Contains(card.Items, i => i.Text == $"{r.Match.Content.Towers[bolt].Name} x2");
        Assert.False(r.Match.Paused);
        r.Ui.Activate(Assert.Single(card.Items, i => i.Action == TdUiAction.DemotionContinue));
        Assert.False(r.Ui.DemotionCardOpen);
    }
}
