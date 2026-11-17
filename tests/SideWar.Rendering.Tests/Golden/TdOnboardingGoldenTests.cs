using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Rendering.Panel;
using SideWar.Rendering.Scene;
using SideWar.Rendering.Text;
using SideWar.Runtime.Input;
using SideWar.Runtime.Td;
using SkiaSharp;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using StartWave = SideWar.Core.Td.StartWave;

namespace SideWar.Rendering.Tests.Golden;

/// <summary>
/// TD.15: goldens de cada passo do tutorial (balão com seta + destaque do alvo real no HUD, no popover ou na faixa), do cartão
/// "Como jogar" e da dica de 1ª vez (100% e 150%; faixa só a 100%). A posição do balão reproduz a regra do TdUiPresenter
/// (centrado no alvo, limitado à largura da área).
/// </summary>
public sealed class TdOnboardingGoldenTests
{
    private const int BandWidth = 1200;
    private static readonly SKColor Backdrop = new(0x1b, 0x22, 0x30);

    public enum Scene { Build, Point, Tower, Wave, Upgrade, UpgradeInspector, Ability, Crate, Help, Tip }

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
        public bool StageUnlocked(int stageId) => stageId <= 2;
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
        public bool Done;
        public bool TutorialDone => Done;
        public void SetTutorialDone(bool done) => Done = done;
        public bool TipSeen(string id) => false;
        public void MarkTipSeen(string id) { }
    }

    public static TheoryData<Scene, int> Cases()
    {
        var d = new TheoryData<Scene, int>();
        foreach (var s in Enum.GetValues<Scene>())
            foreach (int dpi in new[] { 96, 144 }) d.Add(s, dpi);
        return d;
    }

    private static void Prop(TdMatchState m, string name, object value) => typeof(TdMatchState).GetProperty(name)!.SetValue(m, value);

    private static (TdUiController Ui, TdMatchState Match) Setup(Scene scene)
    {
        var m = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(42, 54));
        m.Apply(new LayoutChanged(StripLayout.Create(BandWidth, BandWidth - 46, 88, 4, BandWidth - 80, TowerHost.Band, [new FreeZone(32, BandWidth - 32)])));
        var store = new Store { Done = scene == Scene.Help };
        var ui = new TdUiController(() => m, c => m.Apply(c is TdWaveRequest ? new StartWave() : c), new Host(), () => 0, store);
        ui.Sync();
        var item = (TdUiAction a, string? s) => new TdUiItem(default, TdUiKind.Button, "", a, 0, s);
        void Bolt(int x) { m.Apply(new BuildTower("bolt", x)); ui.Sync(); }
        switch (scene)
        {
            case Scene.Point: ui.Activate(item(TdUiAction.BuildToggle, null)); break;
            case Scene.Tower: ui.SceneLeftClick(600, 20, HitTarget.None); break;
            case Scene.Wave: Bolt(330); break;
            case Scene.Upgrade:
            case Scene.UpgradeInspector:
                Bolt(330); m.Apply(new StartWave()); Prop(m, "Credits", 500L); ui.Sync();
                if (scene == Scene.UpgradeInspector) ui.SceneLeftClick(0, 0, HitTarget.TdTower(m.Towers[0].Id));
                break;
            case Scene.Ability: Bolt(330); m.Apply(new StartWave()); Prop(m, "Credits", 0L); ui.Sync(); break;
            case Scene.Crate:
                Bolt(330); m.Apply(new StartWave()); Prop(m, "Credits", 0L); ui.Activate(item(TdUiAction.Ability, "scale"));
                Prop(m, "CrateActive", true); Prop(m, "CrateX", 700); Prop(m, "CrateTicksLeft", 120); ui.Sync();
                break;
            case Scene.Help: ui.OpenHowTo(); break;
            case Scene.Tip: m.Apply(new BuildTower("bolt", 330)); store.Done = true; ui.Sync(); break;
        }
        return (ui, m);
    }

    private static SKBitmap Layer(TdUiRenderer r, int w, int h, Action<SKCanvas> draw)
    {
        var layer = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var lc = new SKCanvas(layer);
        draw(lc);
        return layer;
    }

    private static SKBitmap Render(Scene scene, int dpi)
    {
        var (ui, match) = Setup(scene);
        using var typo = new Typography();
        using var r = new TdUiRenderer(typo, dpi);
        float s = dpi / 96f;
        if (scene == Scene.Tip)
        {
            string text = ui.CurrentTip ?? throw new InvalidOperationException("sem dica");
            var (tw, th) = r.PhysicalSize(TdUiRenderer.TooltipWidthDip, r.TooltipHeightDip(text));
            var tb = new SKBitmap(new SKImageInfo(tw, th, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var tc = new SKCanvas(tb);
            tc.Clear(Backdrop);
            using var tl = Layer(r, tw, th, lc => r.DrawTooltip(lc, text, firstTimeTip: true));
            GoldenImage.Blit(tc, tl, 0, 0);
            return tb;
        }
        var pop = ui.BuildPopover() ?? throw new InvalidOperationException(scene.ToString());
        bool hudTarget = pop.TargetHudX >= 0;
        bool bandTarget = pop.AnchorX > 0 && pop.HasArrow;
        float areaW = hudTarget ? TdHudView.DefaultWidth : BandWidth;
        if (hudTarget || bandTarget)
        {
            float target = hudTarget ? pop.TargetHudX : pop.AnchorX;
            float left = Math.Clamp(target - pop.Width / 2f, 0, areaW - pop.Width);
            pop = pop with { ArrowX = target - left };
            var hud = ui.BuildHud();
            float belowH = hudTarget ? hud.Height : TdBand.HeightDip;
            var (aw, ah) = r.PhysicalSize(areaW, pop.Height + 2 + belowH);
            var bmp = new SKBitmap(new SKImageInfo(aw, ah, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var c = new SKCanvas(bmp);
            c.Clear(Backdrop);
            var (pw, ph) = r.PhysicalSize(pop.Width, pop.Height);
            using (var pl = Layer(r, pw, ph, lc => r.DrawPopover(lc, pop, -1))) GoldenImage.Blit(c, pl, (int)MathF.Round(left * s), 0);
            int by = (int)MathF.Round((pop.Height + 2) * s);
            if (hudTarget)
            {
                var (hw, hh) = r.PhysicalSize(hud.Width, hud.Height);
                using var hl = Layer(r, hw, hh, lc => r.DrawHud(lc, hud, -1));
                GoldenImage.Blit(c, hl, 0, by);
            }
            else
            {
                using var br = new TdBandRenderer(typo);
                br.SetDpi(dpi);
                var (bw, bh) = br.PhysicalSize(BandWidth);
                using var bl = Layer(r, bw, bh, lc => br.Draw(lc, match, 100.0, ui.View));
                GoldenImage.Blit(c, bl, 0, by);
            }
            return bmp;
        }
        // Passos no popover de construção/inspetor (ou o cartão Como jogar): só o popover.
        var (w, h) = r.PhysicalSize(pop.Width, pop.Height);
        var pb = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var pc = new SKCanvas(pb);
        pc.Clear(Backdrop);
        using var layer = Layer(r, w, h, lc => r.DrawPopover(lc, pop, -1));
        GoldenImage.Blit(pc, layer, 0, 0);
        return pb;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Onboarding_MatchesGolden(Scene scene, int dpi)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "texto Skia difere entre SOs; golden com texto só no Linux (RF-17.3)");
        using var bmp = Render(scene, dpi);
        string? failure = GoldenImage.Verify($"td-tut-{scene.ToString().ToLowerInvariant()}-{dpi * 100 / 96}", bmp);
        Assert.True(failure is null, failure);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Onboarding_IsDeterministic(Scene scene, int dpi)
    {
        using var a = Render(scene, dpi);
        using var b = Render(scene, dpi);
        Assert.True(a.Bytes.AsSpan().SequenceEqual(b.Bytes));
    }

    [Fact]
    public void Band_hint_changes_the_band_and_hud_highlight_changes_the_hud()
    {
        var (ui, match) = Setup(Scene.Point);
        using var typo = new Typography();
        using var br = new TdBandRenderer(typo);
        br.SetDpi(96);
        var (bw, bh) = br.PhysicalSize(BandWidth);
        using var with = new SKBitmap(new SKImageInfo(bw, bh, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var without = new SKBitmap(new SKImageInfo(bw, bh, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(with)) br.Draw(c, match, 100.0, ui.View);
        using (var c = new SKCanvas(without)) br.Draw(c, match, 100.0, TdView.None);
        Assert.True(ui.View.BandHint);
        Assert.False(with.Bytes.AsSpan().SequenceEqual(without.Bytes));

        var (ui2, _) = Setup(Scene.Build);
        using var r = new TdUiRenderer(typo, 96) { TextEnabled = false };
        var hud = ui2.BuildHud();
        var plain = hud with { Items = [.. hud.Items.Select(i => i with { Highlight = false })] };
        var (w, h) = r.PhysicalSize(hud.Width, hud.Height);
        using var a = Layer(r, w, h, lc => r.DrawHud(lc, hud, -1));
        using var b = Layer(r, w, h, lc => r.DrawHud(lc, plain, -1));
        Assert.False(a.Bytes.AsSpan().SequenceEqual(b.Bytes));
    }
}
