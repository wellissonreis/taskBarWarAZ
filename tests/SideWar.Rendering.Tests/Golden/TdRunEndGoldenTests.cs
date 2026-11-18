using System.Reflection;
using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Rendering.Panel;
using SideWar.Rendering.Scene;
using SideWar.Rendering.Text;
using SideWar.Runtime.Td;
using SkiaSharp;
using Xunit;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using StartWave = SideWar.Core.Td.StartWave;

namespace SideWar.Rendering.Tests.Golden;

/// <summary>TD.41: cartão de fim de corrida (modal 720×480 e cantinho) em blocos que não se sobrepõem, com conteúdo curto e longo a 100/150/200%.</summary>
public sealed class TdRunEndGoldenTests
{
    private const int Width = 1000;
    private static readonly SKColor Backdrop = new(0x1b, 0x22, 0x30);

    private sealed class Host(bool longText) : ITdUiHost
    {
        public TdGameOptions GameOptions => new(AutoRestart: longText);
        public bool Paused => false;
        public void TogglePause() { }
        public void OpenSettings() { }
        public void RequestExit() { }
        public bool HudMinimized => false;
        public void SetHudMinimized(bool minimized) { }
        public void StartStage(int stageId) { }
        public int StageCount => 6;
        public bool StageUnlocked(int stageId) => stageId <= 2;
        public int StageStars(int stageId) => 0;
        public bool StageCrown(int stageId) => false;
        public int StageMedals(int stageId) => 0;
        public int StageBestScore(int stageId) => 0;
        public TdRankProgress Rank => TdRank.Progress(longText ? 90000 : 300);
        public TdRecordOutcome LastOutcome => default;
        public int SealsAvailable => 0;
        public bool NodePurchased(string nodeId) => false;
        public bool PurchaseNode(string nodeId) => true;
        public int LegacyTotal => longText ? 40 : 0;
        public int PreviousLegacyTotal => 0;
        public int PreviousBestRunWaves => longText ? 0 : 36;
    }

    /// <summary>Corrida persistente derrotada (determinística); no caso longo, vazamentos grandes de 3 tipos e recomeço armado.</summary>
    private static (TdUiController Ui, TdMatchState Match) Setup(bool longText, bool modal)
    {
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54), null, TdMatchOptions.Default);
        match.Apply(new LayoutChanged(StripLayout.Create(Width, Width - 46, 88, 4, Width - 80, TowerHost.Band, [new FreeZone(32, Width - 32)])));
        for (int i = 0; i < 30 * 3000 && match.Phase is MatchPhase.Onda or MatchPhase.Preparo; i++)
        {
            if (match.Phase == MatchPhase.Preparo) match.Apply(new StartWave());
            match.Step();
        }
        Assert.Equal(MatchPhase.Derrota, match.Phase);
        if (longText)
        {
            var t = typeof(TdMatchState);
            var count = (int[])t.GetField("_leakCount", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(match)!;
            var dmg = (int[])t.GetField("_leakDamage", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(match)!;
            for (int i = 0; i < count.Length; i++) { count[i] = i < 4 ? 1000 + i * 111 : 0; dmg[i] = count[i]; }
        }
        var ui = new TdUiController(() => match, c => match.Apply(c), new Host(longText), () => 0.0);
        ui.Sync();
        if (modal) typeof(TdUiController).GetField("_endCorner", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(ui, false);
        return (ui, match);
    }

    public static TheoryData<bool, bool, int> Cases()
    {
        var d = new TheoryData<bool, bool, int>();
        foreach (bool longText in new[] { false, true })
            foreach (bool modal in new[] { true, false })
                foreach (int dpi in new[] { 96, 144, 192 }) d.Add(longText, modal, dpi);
        return d;
    }

    private static SKBitmap Render(bool longText, bool modal, int dpi)
    {
        var (ui, _) = Setup(longText, modal);
        var p = ui.BuildPopover()!;
        using var typo = new Typography();
        using var r = new TdUiRenderer(typo, dpi) { TextEnabled = true };
        var (pw, ph) = r.PhysicalSize(p.Width, p.Height);
        var bmp = new SKBitmap(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        c.Clear(Backdrop);
        using var layer = new SKBitmap(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var lc = new SKCanvas(layer)) r.DrawPopover(lc, p, -1);
        GoldenImage.Blit(c, layer, 0, 0);
        return bmp;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void RunEnd_MatchesGolden(bool longText, bool modal, int dpi)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "texto Skia difere entre SOs; golden com texto só no Linux (RF-17.3)");
        using var bmp = Render(longText, modal, dpi);
        string? failure = GoldenImage.Verify($"td-runend-{(modal ? "modal" : "corner")}-{(longText ? "long" : "short")}-{dpi * 100 / 96}", bmp);
        Assert.True(failure is null, failure);
    }

    /// <summary>Retângulos de blocos diferentes (rótulos, patente, botões) nunca se intersectam e ficam dentro da janela; rótulos cabem em suas linhas.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void RunEnd_blocks_never_overlap_and_stay_inside(bool longText, bool modal)
    {
        var (ui, _) = Setup(longText, modal);
        var p = ui.BuildPopover()!;
        var blocks = p.Items.Where(i => i.Action is not (TdUiAction.Close or TdUiAction.ModalTab)).ToArray();
        Assert.True(blocks.Length >= 6);
        foreach (var b in blocks)
        {
            Assert.True(b.Rect.X >= 0 && b.Rect.Y >= 0 && b.Rect.X + b.Rect.W <= p.Width && b.Rect.Y + b.Rect.H <= p.Height, $"fora da janela: {b.Text}");
            if (b.Kind == TdUiKind.Label)
                Assert.True(TdUiController.QuizLines(b.Text, b.Rect.W, b.FontSize) * b.FontSize * TdUiController.QuizLineFactor <= b.Rect.H + 0.5f || b.Text.StartsWith("Vazou", StringComparison.Ordinal) || b.Text.StartsWith("Advisor", StringComparison.Ordinal) || b.Tone == TdTone.Bad, $"texto não cabe: {b.Text}");
            if (b.Kind is TdUiKind.Label or TdUiKind.Button) Assert.True(b.FontSize >= 14f, $"fonte < 14: {b.Text}");
        }
        for (int i = 0; i < blocks.Length; i++)
            for (int j = i + 1; j < blocks.Length; j++)
            {
                var a = blocks[i].Rect; var b = blocks[j].Rect;
                bool overlap = a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;
                Assert.False(overlap, $"sobreposição: '{blocks[i].Text}' x '{blocks[j].Text}'");
            }
        Assert.Contains(blocks, i => i.Action == TdUiAction.Restart);
        Assert.Contains(blocks, i => i.Action == TdUiAction.ViewRunes);
    }
}
