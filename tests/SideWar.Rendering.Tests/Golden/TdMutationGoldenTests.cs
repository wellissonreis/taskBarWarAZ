using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Rendering.Panel;
using SideWar.Rendering.Text;
using SideWar.Runtime.Td;
using SkiaSharp;
using Xunit;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using StartWave = SideWar.Core.Td.StartWave;

namespace SideWar.Rendering.Tests.Golden;

/// <summary>TD.37d: aviso de mutantes da próxima onda (1ª vez, detalhado, resumo) e tooltip do inimigo mutado a 100/150/200%: blocos sem sobreposição, fonte ≥ 14 DIP.</summary>
public sealed class TdMutationGoldenTests
{
    private static readonly SKColor Backdrop = new(0x1b, 0x22, 0x30);
    private static readonly TdContent Real = TdContentLoader.LoadEmbedded();

    private sealed class Host : ITdUiHost
    {
        public TdGameOptions GameOptions => new(Discreet: true);
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

    /// <summary>Ondas fixas de 10 requisições e 10 pings, mutação sempre ativa (até 30%) com os seis traços liberados na fase 1; só a UI muda com o conteúdo.</summary>
    private static TdContent Content(params string[] traitIds)
    {
        var traits = Real.Mutations.Traits.Where(t => traitIds.Contains(t.Id)).Select(t => t with { MinStage = 1 }).ToArray();
        var defs = Enumerable.Range(1, 6).Select(i => new TdWaveDef(i, false, 1.0, [new TdWaveGroupDef("grunt", 10, 0.0, 0.5), new TdWaveGroupDef("runner", 10, 0.0, 0.5)])).ToArray();
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

    public static readonly string[] Kinds = ["warn-first", "warn-detailed", "warn-summary", "enemy-tip", "help"];

    private static TdPopoverView Build(string kind)
    {
        // resumo = combinação já vista: com um único traço liberado, a onda 2 repete a combinação da onda 1
        var content = kind == "warn-summary" ? Content("fast") : Content("fast", "durable", "flying", "slow", "shielded", "armored");
        var match = TdMatchState.Create(content, 1, Rng.Seed(7, 54), null, TdMatchOptions.Default);
        match.Apply(new LayoutChanged(StripLayout.Create(1000, 954, 88, 4, 920, TowerHost.Band, [new FreeZone(32, 968)])));
        double now = 0;
        var store = new Store();
        if (kind != "warn-first") store.MarkTipSeen(TdUiController.MutationTipKey);
        var ui = new TdUiController(() => match, c => match.Apply(c), new Host(), () => now, store);
        ui.Sync();
        switch (kind)
        {
            case "warn-first":
            case "warn-detailed":
                Assert.True(ui.MutationWarnOpen);
                break;
            case "warn-summary":
                ui.Activate(ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.MutationWarnDismiss));
                Assert.True(match.Apply(new SideWar.Core.Td.BuildTower("bolt", 900)).IsOk);
                for (int guard = 0; guard < 6 && !ui.MutationWarnOpen; guard++) { match.Apply(new StartWave()); now += 0.1; ui.Sync(); }
                Assert.True(ui.MutationWarnOpen);
                break;
            case "enemy-tip":
                Assert.True(match.Apply(new SideWar.Core.Td.BuildTower("bolt", 900)).IsOk);
                Assert.True(match.Apply(new StartWave()).IsOk);
                Assert.True(match.Apply(new SideWar.Core.Td.SellTower(match.Towers[^1].Id)).IsOk);
                for (int i = 0; i < 3000 && ui.HoverEnemyId == 0; i++)
                {
                    match.Step();
                    foreach (var e in match.Enemies)
                        if (e.Mutations != 0 && match.MutationLabels(e.Mutations).Count == 2) { ui.HoverEnemy(e.Id); break; }
                }
                Assert.NotEqual(0, ui.HoverEnemyId);
                return ui.BuildEnemyTip()!;
            case "help":
                ui.OpenHowTo();
                break;
        }
        return ui.BuildPopover()!;
    }

    public static TheoryData<string, int> Cases()
    {
        var d = new TheoryData<string, int>();
        foreach (var kind in Kinds)
            foreach (int dpi in new[] { 96, 144, 192 }) d.Add(kind, dpi);
        return d;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void MutationCard_MatchesGolden(string kind, int dpi)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "texto Skia difere entre SOs; golden com texto só no Linux (RF-17.3)");
        var p = Build(kind);
        using var typo = new Typography();
        using var r = new TdUiRenderer(typo, dpi) { TextEnabled = true };
        var (pw, ph) = r.PhysicalSize(p.Width, p.Height);
        using var bmp = new SKBitmap(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        c.Clear(Backdrop);
        using var layer = new SKBitmap(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var lc = new SKCanvas(layer)) r.DrawPopover(lc, p, -1);
        GoldenImage.Blit(c, layer, 0, 0);
        string? failure = GoldenImage.Verify($"td-mutation-{kind}-{dpi * 100 / 96}", bmp);
        Assert.True(failure is null, failure);
    }

    [Theory]
    [InlineData("warn-first")]
    [InlineData("warn-detailed")]
    [InlineData("warn-summary")]
    [InlineData("enemy-tip")]
    public void MutationCard_blocks_do_not_overlap_and_use_readable_fonts(string kind)
    {
        var p = Build(kind);
        var blocks = p.Items.Where(i => i.Text != "X").ToArray();
        Assert.NotEmpty(blocks);
        foreach (var b in blocks)
        {
            Assert.True(b.Rect.X >= 0 && b.Rect.Y >= 0 && b.Rect.X + b.Rect.W <= p.Width && b.Rect.Y + b.Rect.H <= p.Height, b.Text);
            Assert.True(b.FontSize >= 14f, b.Text);
        }
        for (int i = 0; i < blocks.Length; i++)
            for (int j = i + 1; j < blocks.Length; j++)
            {
                var a = blocks[i].Rect; var b = blocks[j].Rect;
                Assert.False(a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H, $"{blocks[i].Text} x {blocks[j].Text}");
            }
    }

    [Fact]
    public void Warning_variants_differ_in_content()
    {
        var first = Build("warn-first").Items.Select(i => i.Text).ToArray();
        var detailed = Build("warn-detailed").Items.Select(i => i.Text).ToArray();
        var summary = Build("warn-summary").Items.Select(i => i.Text).ToArray();
        Assert.Contains(TdUiController.MutationIntroText, first);
        Assert.DoesNotContain(TdUiController.MutationIntroText, detailed);
        Assert.Contains(detailed, t => t.StartsWith("Onda 1 traz ", StringComparison.Ordinal));
        Assert.Contains(summary, t => t.StartsWith("Onda ", StringComparison.Ordinal) && t.Contains("mutante", StringComparison.Ordinal));
        Assert.DoesNotContain(summary, t => t.Contains("traz", StringComparison.Ordinal));
    }
}
