using SideWar.Core.Td;
using SideWar.Rendering.Panel;
using SideWar.Rendering.Text;
using SideWar.Runtime.Input;
using SideWar.Runtime.Td;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Golden;

/// <summary>TD.8: goldens do HUD compacto, do popover de torres, do inspetor, do preview/Mapa e dos cards de fim (100% e 150%).</summary>
public sealed class TdUiGoldenTests
{
    private static readonly SKColor Backdrop = new(0x1b, 0x22, 0x30);

    private sealed class Host : ITdUiHost
    {
        public bool Paused => false;
        public void TogglePause() { }
        public void OpenSettings() { }
        public void RequestExit() { }
        public bool HudMinimized { get; private set; }
        public void SetHudMinimized(bool minimized) => HudMinimized = minimized;
        public void StartStage(int stageId) { }
        public int StageCount => 6;
        public bool StageUnlocked(int stageId) => stageId <= 2;
        public int StageStars(int stageId) => stageId == 1 ? 3 : 0;
        public bool StageCrown(int stageId) => stageId == 1;
        public int StageMedals(int stageId) => stageId == 1 ? 5 : stageId == 2 ? 2 : 0;
        public int StageBestScore(int stageId) => stageId == 1 ? 4123 : stageId == 2 ? 2987 : 0;
        public TdRankProgress Rank => TdRank.Progress(7110);
        public TdRecordOutcome LastOutcome => new(true, true, TdMedals.SemVender | TdMedals.Pressa, 4);
        public int SealsAvailable => 2;
        public bool NodePurchased(string nodeId) => nodeId == "infrastructure.credits";
        public bool PurchaseNode(string nodeId) => true;
    }

    public enum UiScene { HudPrep, HudWave, HudMin, HudTip, Chooser, ChooserPoint, Inspector, Preview, Map, Seals, Victory, Menu, HudPlacing, Towers, Runes, Help, Defeat, QuickTowers }

    public static TheoryData<UiScene, int> Cases()
    {
        var d = new TheoryData<UiScene, int>();
        foreach (var s in Enum.GetValues<UiScene>())
            foreach (int dpi in new[] { 96, 144 }) d.Add(s, dpi);
        return d;
    }

    private static (TdUiController Ui, TdMatchState Match) Setup(UiScene scene)
    {
        var (match, _, _) = TdStripScenes.Build(TdStripScene.PrepRange);
        typeof(TdMatchState).GetProperty("Credits")!.SetValue(match, 240L);
        var ui = new TdUiController(() => match, c => match.Apply(c), new Host(), () => 0);
        ui.Sync();
        var item = (TdUiAction a, int arg) => new TdUiItem(default, TdUiKind.Button, "", a, arg);
        switch (scene)
        {
            case UiScene.HudMin: ui.Activate(item(TdUiAction.HudMinimize, 0)); break;
            case UiScene.HudWave:
                match.Apply(new StartWave());
                for (int i = 0; i < 90; i++) match.Step();
                match.ClearEvents();
                break;
            case UiScene.ChooserPoint: ui.Activate(item(TdUiAction.BuildToggle, 0)); ui.PointerMoved(1100); break;
            case UiScene.Chooser: ui.ClickPoint(700); break;
            case UiScene.Inspector: ui.OpenTowerFromList(match.Towers[0].Id); break;
            case UiScene.Preview: ui.Activate(item(TdUiAction.Preview, 0)); break;
            case UiScene.QuickTowers: ui.Activate(item(TdUiAction.QuickTowers, 0)); break;
            case UiScene.Runes: ui.Activate(item(TdUiAction.ModalTab, (int)TdUiMode.Runes)); break;
            case UiScene.Help: ui.OpenHowTo(); break;
            case UiScene.Defeat:
                typeof(TdMatchState).GetProperty("Phase")!.SetValue(match, MatchPhase.Derrota); ui.Sync(); break;
            case UiScene.Menu: ui.Activate(item(TdUiAction.Menu, 0)); break;
            case UiScene.Towers: ui.Activate(item(TdUiAction.TowersOpen, 0)); break;
            case UiScene.HudPlacing: ui.Activate(item(TdUiAction.BuildToggle, 0)); break;
            case UiScene.Map: ui.Activate(item(TdUiAction.MapOpen, 0)); break;
            case UiScene.Seals: ui.Activate(item(TdUiAction.MapOpen, 0)); ui.Activate(item(TdUiAction.MapTab, 1)); break;
            case UiScene.Victory:
                match.Apply(new StartWave());
                for (int i = 0; i < 30 * 600 && match.Phase == MatchPhase.Onda; i++) match.Step();
                ui.Sync();
                break;
        }
        return (ui, match);
    }

    private static SKBitmap Render(UiScene scene, int dpi, bool text)
    {
        var (ui, _) = Setup(scene);
        using var typo = new Typography();
        using var r = new TdUiRenderer(typo, dpi) { TextEnabled = text };
        if (scene == UiScene.HudTip)
        {
            var tipHud = ui.BuildHud();
            var tip = TdUiController.TooltipFor(tipHud, Array.FindIndex(tipHud.Items, i => i.Icon == SideWar.Runtime.Td.TdIcon.Heart))!;
            var (tw, th) = r.PhysicalSize(TdUiRenderer.TooltipWidthDip, r.TooltipHeightDip(tip.Text));
            var tb = new SKBitmap(new SKImageInfo(tw, th, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var tc = new SKCanvas(tb);
            tc.Clear(Backdrop);
            using var tl = new SKBitmap(new SKImageInfo(tw, th, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var tlc = new SKCanvas(tl)) r.DrawTooltip(tlc, tip.Text);
            GoldenImage.Blit(tc, tl, 0, 0);
            return tb;
        }
        bool hud = scene is UiScene.HudPrep or UiScene.HudWave or UiScene.HudMin or UiScene.HudPlacing;
        if (hud)
        {
            var v = ui.BuildHud();
            var (w, h) = r.PhysicalSize(v.Width, v.Height);
            var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var c = new SKCanvas(bmp);
            c.Clear(Backdrop);
            using var layer = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var lc = new SKCanvas(layer)) r.DrawHud(lc, v, 3);
            GoldenImage.Blit(c, layer, 0, 0);
            return bmp;
        }
        var p = ui.BuildPopover() ?? throw new InvalidOperationException(scene.ToString());
        var (pw, ph) = r.PhysicalSize(p.Width, p.Height);
        var pb = new SKBitmap(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var pc = new SKCanvas(pb);
        pc.Clear(Backdrop);
        using var pl = new SKBitmap(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var plc = new SKCanvas(pl)) r.DrawPopover(plc, p, 1);
        GoldenImage.Blit(pc, pl, 0, 0);
        return pb;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Ui_MatchesGolden(UiScene scene, int dpi)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "texto Skia difere entre SOs; golden com texto só no Linux (RF-17.3)");
        using var bmp = Render(scene, dpi, text: true);
        string? failure = GoldenImage.Verify($"td-ui-{scene.ToString().ToLowerInvariant()}-{dpi * 100 / 96}", bmp);
        Assert.True(failure is null, failure);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Ui_IsDeterministic(UiScene scene, int dpi)
    {
        using var a = Render(scene, dpi, text: true);
        using var b = Render(scene, dpi, text: true);
        Assert.True(a.Bytes.AsSpan().SequenceEqual(b.Bytes));
    }

    /// <summary>TD.14: o HUD escala com a faixa (dpi efetivo = dpi × HudScale): faixa de 40 (piso 0,75) e de 88 (1,375).</summary>
    [Theory]
    [InlineData(40)]
    [InlineData(88)]
    public void Hud_MatchesGoldenAtBandHeight(int bandHeightDip)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "texto Skia difere entre SOs; golden com texto só no Linux (RF-17.3)");
        int effDpi = (int)MathF.Round(96 * TdBand.HudScale(bandHeightDip));
        using var bmp = Render(UiScene.HudPrep, effDpi, text: true);
        string? failure = GoldenImage.Verify($"td-ui-hud-band{bandHeightDip}", bmp);
        Assert.True(failure is null, failure);
    }

    [Fact]
    public void Ghost_is_drawn_in_orange_when_invalid_and_mint_when_valid()
    {
        var (match, _, _) = TdStripScenes.Build(TdStripScene.PrepRange);
        using var typo = new Typography();
        using var r = new SideWar.Rendering.Scene.TdBandRenderer(typo);
        var (w, h) = r.PhysicalSize(TdStripScenes.WidthDip);
        using var none = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var valid = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var invalid = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(none)) r.Draw(c, match, 1, TdView.None);
        using (var c = new SKCanvas(valid)) r.Draw(c, match, 1, new TdView(0, 0) { GhostX = 700, GhostValid = true, GhostDef = 0 });
        using (var c = new SKCanvas(invalid)) r.Draw(c, match, 1, new TdView(0, 0) { GhostX = 700, GhostValid = false, GhostDef = 0 });
        Assert.False(none.Bytes.AsSpan().SequenceEqual(valid.Bytes));
        Assert.False(valid.Bytes.AsSpan().SequenceEqual(invalid.Bytes));
    }
}
