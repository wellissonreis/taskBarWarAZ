using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Td;
using Xunit;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using StartWave = SideWar.Core.Td.StartWave;

namespace SideWar.Runtime.Tests.Td;

/// <summary>TD.37d (SDD-37 RF-37.11): aviso discreto das mutações da próxima onda, dica de 1ª vez, tooltip do inimigo e respeito a boss key/pausa/clique-fora.</summary>
public sealed class TdMutationUiTests
{
    private sealed class Host : ITdUiHost
    {
        public bool Paused => false;
        public void TogglePause() { }
        public void OpenSettings() { }
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
        public bool PurchaseNode(string nodeId) => true;
    }

    private sealed class Store : ITdOnboardingStore
    {
        public readonly HashSet<string> Seen = [];
        public bool TutorialDone => true;
        public void SetTutorialDone(bool done) { }
        public bool TipSeen(string id) => Seen.Contains(id);
        public void MarkTipSeen(string id) => Seen.Add(id);
        public void ClearTip(string id) => Seen.Remove(id);
    }

    private static readonly TdContent Real = TdContentLoader.LoadEmbedded();

    /// <summary>Todas as ondas: 10 grunts, mutação sempre ativa (até 30%) com os traços dados; tudo liberado na fase 1.</summary>
    public static TdContent MutContent(params string[] traitIds)
    {
        var traits = Real.Mutations.Traits.Where(t => traitIds.Contains(t.Id)).Select(t => t with { MinStage = 1 }).ToArray();
        var defs = Enumerable.Range(1, 6).Select(i => new TdWaveDef(i, false, 1.0, [new TdWaveGroupDef("grunt", 10, 0.0, 0.5)])).ToArray();
        return Real with
        {
            Mutations = Real.Mutations with { Traits = traits, DoubleGapWaves = 0, Tiers = [new TdMutationTierDef(1, 0, 0.3, -1, 0.5)] },
            Stages = Real.Stages with
            {
                Rules = Real.Stages.Rules with { CoreIntegrity = 1_000_000 },
                Stages = Real.Stages.Stages.Select(s => s with { Waves = 6, StartingCredits = 100000 }).ToArray(),
            },
            Waves = new TdWavesDef(Real.Stages.Stages.Select(s => new TdStageWavesDef(s.Id, defs)).ToArray()),
        };
    }

    private sealed class Rig
    {
        public readonly TdMatchState Match;
        public readonly Store Store = new();
        public readonly TdUiController Ui;
        public double Now;

        public Rig(TdContent content, bool onboarding = false, int stage = 1)
        {
            Match = TdMatchState.Create(content, stage, Rng.Seed(7, 54), null, TdMatchOptions.Default);
            Match.Apply(new LayoutChanged(StripLayout.Create(1000, 954, 88, 4, 920, TowerHost.Band, [new FreeZone(32, 968)])));
            Ui = new TdUiController(() => Match, c => Match.Apply(c), new Host(), () => Now, onboarding ? Store : null);
            Ui.Sync();
        }

        public void Tick(double dt) { Now += dt; Ui.Sync(); }
        public string[] Texts() => Ui.BuildPopover()!.Items.Select(i => i.Text).ToArray();
    }

    [Fact]
    public void Warning_appears_in_the_corner_before_the_wave_with_the_exact_preview()
    {
        var r = new Rig(MutContent("durable", "fast"));
        var preview = r.Match.PreviewNextWaveMutations();
        Assert.NotEmpty(preview);
        Assert.True(r.Ui.MutationWarnOpen);
        Assert.True(r.Ui.CornerPopover);
        var texts = r.Texts();
        Assert.Contains(texts, t => t.StartsWith("Onda 1 traz ", StringComparison.Ordinal));
        foreach (var g in preview)
            Assert.Contains($"{g.Count}x {r.Match.MutatedName(g.EnemyIndex, g.Traits)}", texts);
        Assert.Equal(MatchPhase.Preparo, r.Match.Phase); // não pausou nem iniciou nada
    }

    [Fact]
    public void No_warning_without_mutations()
    {
        var r = new Rig(Real, stage: 1);
        Assert.Empty(r.Match.PreviewNextWaveMutations());
        Assert.False(r.Ui.MutationWarnOpen);
        Assert.False(r.Ui.MutationWarnPending);
    }

    [Fact]
    public void Warning_auto_dismisses_and_the_dismiss_button_works()
    {
        var r = new Rig(MutContent("durable"));
        Assert.True(r.Ui.MutationWarnOpen);
        r.Tick(TdUiController.MutationWarnSeconds + 1);
        Assert.False(r.Ui.MutationWarnOpen);
        var r2 = new Rig(MutContent("durable"));
        r2.Ui.Activate(r2.Ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.MutationWarnDismiss));
        Assert.False(r2.Ui.MutationWarnOpen);
    }

    [Fact]
    public void Warning_freezes_while_paused_and_hides_with_boss_key_and_click_outside()
    {
        var r = new Rig(MutContent("durable"));
        r.Match.Apply(new Pause());
        r.Tick(60);
        Assert.False(r.Ui.MutationWarnOpen); // pausa: some da tela
        r.Match.Apply(new Resume());
        r.Tick(0.1);
        Assert.True(r.Ui.MutationWarnOpen); // o tempo parado não gastou o aviso

        r.Ui.SetBossHidden(true);
        Assert.False(r.Ui.MutationWarnOpen);
        Assert.Null(r.Ui.BuildPopover());
        r.Ui.SetBossHidden(false);

        var o = new Rig(MutContent("durable"));
        Assert.True(o.Ui.MutationWarnOpen);
        o.Ui.CollapseOutside();
        Assert.False(o.Ui.MutationWarnOpen);
        o.Tick(1);
        Assert.False(o.Ui.MutationWarnOpen); // não volta sozinho depois do clique fora
    }

    [Fact]
    public void Warning_waits_for_a_modal_and_never_steals_the_world()
    {
        var r = new Rig(Real, stage: 1);
        r.Ui.OpenStartupModal(); // modal aberta: pausa do jogo e nada de aviso por cima
        var rig = new Rig(MutContent("durable"));
        rig.Ui.OpenStartupModal();
        rig.Tick(1);
        Assert.False(rig.Ui.MutationWarnOpen);
    }

    [Fact]
    public void First_combination_is_detailed_and_following_ones_are_a_short_summary()
    {
        var r = new Rig(MutContent("durable"), onboarding: true);
        var first = r.Texts();
        Assert.Contains(TdUiController.MutationIntroText, first);       // dica de 1ª vez
        Assert.Contains(first, t => t.StartsWith("Onda 1 traz Tank", StringComparison.Ordinal));
        Assert.Contains(TdUiController.MutationTipKey, r.Store.Seen);
        r.Ui.Activate(r.Ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.MutationWarnDismiss));

        Assert.True(r.Match.Apply(new StartWave()).IsOk);   // a prévia passa para a onda 2
        r.Tick(0.1);
        Assert.True(r.Ui.MutationWarnOpen);
        var second = r.Texts();
        Assert.DoesNotContain(TdUiController.MutationIntroText, second);
        Assert.Contains(second, t => t.StartsWith("Onda 2:", StringComparison.Ordinal) && t.Contains("mutante", StringComparison.Ordinal)); // resumo curto
        Assert.DoesNotContain(second, t => t.Contains("traz", StringComparison.Ordinal));
    }

    [Fact]
    public void Intro_is_shown_only_once_across_sessions()
    {
        var store = new Store();
        store.MarkTipSeen(TdUiController.MutationTipKey);
        var match = TdMatchState.Create(MutContent("durable"), 1, Rng.Seed(7, 54), null, TdMatchOptions.Default);
        var ui = new TdUiController(() => match, c => match.Apply(c), new Host(), () => 0, store);
        ui.Sync();
        Assert.True(ui.MutationWarnOpen);
        Assert.DoesNotContain(TdUiController.MutationIntroText, ui.BuildPopover()!.Items.Select(i => i.Text));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Warning_blocks_use_readable_fonts_without_overlap(bool first)
    {
        var r = new Rig(MutContent("durable", "fast", "slow", "shielded"), onboarding: first);
        var p = r.Ui.BuildPopover()!;
        var blocks = p.Items.Where(i => i.Text != "X").ToArray();
        Assert.NotEmpty(blocks);
        foreach (var b in blocks)
        {
            Assert.True(b.Rect.X >= 0 && b.Rect.Y >= 0 && b.Rect.X + b.Rect.W <= p.Width && b.Rect.Y + b.Rect.H <= p.Height, b.Text);
            Assert.True(b.FontSize >= 14f, b.Text);
        }
        AssertNoOverlap(blocks);
    }

    internal static void AssertNoOverlap(IReadOnlyList<TdUiItem> blocks)
    {
        for (int i = 0; i < blocks.Count; i++)
            for (int j = i + 1; j < blocks.Count; j++)
            {
                var a = blocks[i].Rect; var b = blocks[j].Rect;
                Assert.False(a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H, $"{blocks[i].Text} x {blocks[j].Text}");
            }
    }

    private static (Rig R, TdEnemy E) WithMutant(params string[] traits)
    {
        var r = new Rig(MutContent(traits));
        Assert.True(r.Match.Apply(new StartWave()).IsOk);
        for (int i = 0; i < 3000; i++)
        {
            r.Match.Step();
            foreach (var e in r.Match.Enemies) if (e.Mutations != 0) return (r, e);
        }
        throw new InvalidOperationException("sem mutante");
    }

    [Fact]
    public void Enemy_tooltip_shows_mutated_name_and_traits_with_readable_layout()
    {
        var (r, e) = WithMutant("durable");
        Assert.Null(r.Ui.BuildEnemyTip());
        r.Ui.HoverEnemy(e.Id);
        var p = r.Ui.BuildEnemyTip()!;
        var texts = p.Items.Select(i => i.Text).ToArray();
        Assert.Contains(r.Match.MutatedName(e.DefIndex, e.Mutations), texts);
        Assert.Contains("Mutação: Tank", texts);
        Assert.Contains(texts, t => t.StartsWith("Vida ", StringComparison.Ordinal));
        Assert.All(p.Items, i => Assert.True(i.FontSize >= 14f, i.Text));
        AssertNoOverlap(p.Items);
        Assert.All(p.Items, i => Assert.True(i.Rect.Y + i.Rect.H <= p.Height));
        r.Ui.SetBossHidden(true);
        Assert.Null(r.Ui.BuildEnemyTip());
        r.Ui.SetBossHidden(false);
        r.Ui.HoverEnemy(999_999);
        Assert.Null(r.Ui.BuildEnemyTip()); // inimigo que não existe mais
    }

    [Fact]
    public void Help_card_mentions_mutants()
    {
        var r = new Rig(Real);
        r.Ui.OpenHowTo();
        Assert.Contains(TdUiController.MutationHelpText, r.Texts());
    }

    [Fact]
    public void Warning_does_not_touch_the_simulation()
    {
        var a = new Rig(MutContent("durable", "fast"));
        var b = TdMatchState.Create(MutContent("durable", "fast"), 1, Rng.Seed(7, 54), null, TdMatchOptions.Default);
        b.Apply(new LayoutChanged(StripLayout.Create(1000, 954, 88, 4, 920, TowerHost.Band, [new FreeZone(32, 968)])));
        a.Ui.BuildPopover();
        a.Tick(2);
        Assert.Equal(b.ComputeStateHash(), a.Match.ComputeStateHash());
    }
}
