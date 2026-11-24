using SideWar.Core.Td;
using SideWar.Rendering.Panel;
using SideWar.Rendering.Scene;
using SideWar.Rendering.Theme;
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
        public TdGameOptions GameOptions { get; set; } = new();
        public bool Paused => false;
        public void TogglePause() { }
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
        public TdRankProgress Rank { get; set; } = TdRank.Progress(7110);
        public TdRecordOutcome LastOutcome => new(true, true, TdMedals.SemVender | TdMedals.Pressa, 4);
        public int SealsAvailable => 2;
        public bool NodePurchased(string nodeId) => nodeId == "infrastructure.credits";
        public bool PurchaseNode(string nodeId) => true;
    }

    private sealed class Store : ITdOnboardingStore
    {
        private readonly HashSet<string> _seen = [];
        public bool TutorialDone => true;
        public void SetTutorialDone(bool done) { }
        public bool TipSeen(string id) => _seen.Contains(id);
        public void MarkTipSeen(string id) => _seen.Add(id);
        public void ClearTip(string id) => _seen.Remove(id);
    }

    public enum UiScene { HudMoney, HudPrep, HudWave, HudMin, HudLarge, HudLargeMin, HudTip, Chooser, ChooserPoint, Inspector, Preview, Map, MapLocked, Seals, Victory, Menu, HudPlacing, Towers, Runes, Help, BaseAttacked, QuickTowers, RuneWindow, RuneWindowPan, RuneWindowZoom, RuneWindowTipAvailable, RuneWindowTipAwake, RuneWindowTipLocked, RunesTipAvailable, RunesTipAwake, RunesTipLocked, RunesBranch, RuneWindowBranch, RuneCard, RuneWindowFromCard, RunesZoomMin, RunesZoomMid, HudSpeedExtras, MenuSpeedExtras }

    public static TheoryData<UiScene, int> Cases()
    {
        var d = new TheoryData<UiScene, int>();
        foreach (var s in Enum.GetValues<UiScene>())
            foreach (int dpi in new[] { 96, 144 }) d.Add(s, dpi);
        return d;
    }

    [Theory]
    [InlineData(UiScene.HudLarge, 96)]
    [InlineData(UiScene.HudLargeMin, 96)]
    [InlineData(UiScene.HudLarge, 144)]
    [InlineData(UiScene.HudLargeMin, 144)]
    public void Large_hud_text_fits_real_font(UiScene scene, int dpi)
    {
        var (ui, _) = Setup(scene);
        var hud = ui.BuildHud();
        using var typo = new Typography();
        float size = Math.Max(13, 11 / hud.LayoutScale);
        var font = typo.GetFont(FontRole.JetBrainsMonoRegular, size, dpi);
        var core = hud.Items.Single(i => i.SArg == "core");
        var wave = hud.Items.Single(i => i.SArg == "wave");
        Assert.True(font.MeasureText(core.Text) * hud.LayoutScale <= (core.Rect.W - 20 * hud.LayoutScale) * dpi / 96f);
        Assert.True(font.MeasureText(wave.Text) * hud.LayoutScale <= (wave.Rect.W - 115 * hud.LayoutScale) * dpi / 96f);
    }

    private static (TdUiController Ui, TdMatchState Match) Setup(UiScene scene)
    {
        var (match, _, _) = TdStripScenes.Build(TdStripScene.PrepRange);
        if (scene == UiScene.MapLocked) match = TdMatchState.Create(SideWar.Core.Content.Td.TdContentLoader.LoadEmbedded(), 1, SideWar.Core.Simulation.Rng.Seed(7, 54));
        typeof(TdMatchState).GetProperty("Credits")!.SetValue(match, 240L);
        var host = new Host();
        double now = 0;
        var ui = new TdUiController(() => match, c => match.Apply(c), host, () => now, scene is UiScene.RuneCard or UiScene.RuneWindowFromCard ? new Store() : null);
        ui.Sync();
        var item = (TdUiAction a, int arg) => new TdUiItem(default, TdUiKind.Button, "", a, arg);
        switch (scene)
        {
            case UiScene.HudSpeedExtras or UiScene.MenuSpeedExtras:
                host.GameOptions = new TdGameOptions(ExtraSpeeds: true, SpeedMask: 0b1000_1111, GameSpeed: 5);
                if (scene == UiScene.MenuSpeedExtras) ui.Activate(item(TdUiAction.Menu, 0));
                break;
            case UiScene.HudLarge or UiScene.HudLargeMin:
                host.Rank = TdRank.Progress(999999);
                typeof(TdMatchState).GetField("_streak", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(match, 10);
                var wavesField = typeof(TdMatchState).GetField("_waves", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                var original = (Array)wavesField.GetValue(match)!;
                var waves = Array.CreateInstance(original.GetType().GetElementType()!, 60);
                for (int i = 0; i < waves.Length; i++) waves.SetValue(original.GetValue(0), i);
                wavesField.SetValue(match, waves);
                typeof(TdMatchState).GetProperty("WaveNumber")!.SetValue(match, 60);
                if (scene == UiScene.HudLargeMin) ui.Activate(item(TdUiAction.HudMinimize, 0));
                break;
            case UiScene.HudMin: ui.Activate(item(TdUiAction.HudMinimize, 0)); break;
            case UiScene.HudMoney:
                // TD.33d: contador grande no meio da contagem animada, "+N" subindo e renda ligada.
                host.GameOptions = new TdGameOptions(ShowIncome: true);
                ui.BuildHud();
                typeof(TdMatchState).GetProperty("Credits")!.SetValue(match, 370L);
                now = 0.1; ui.BuildHud();
                now = 0.25; ui.BuildHud();
                break;
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
            case UiScene.RunesZoomMin or UiScene.RunesZoomMid:
                ui.Activate(item(TdUiAction.ModalTab, (int)TdUiMode.Runes));
                ui.ZoomWeb(scene == UiScene.RunesZoomMin ? -100 : -3);
                break;
            case UiScene.RuneWindowPan or UiScene.RuneWindowZoom:
                ui.Activate(item(TdUiAction.RuneWindow, 0));
                ui.PanWeb(-120, 65);
                if (scene == UiScene.RuneWindowZoom) ui.ZoomWeb(-4, 150, 120);
                break;
            case UiScene.RuneWindow or UiScene.RuneWindowTipAvailable or UiScene.RuneWindowTipAwake or UiScene.RuneWindowTipLocked:
            case UiScene.RunesTipAvailable or UiScene.RunesTipAwake or UiScene.RunesTipLocked:
            {
                typeof(TdMatchState).GetProperty("Fragments")!.SetValue(match, 5L);
                for (int i = 0; i < 4; i++) match.Apply(new AwakenRune(match.Frontier[0]));
                typeof(TdMatchState).GetProperty("Fragments")!.SetValue(match, 2L);
                ui.Sync();
                bool modal = scene >= UiScene.RunesTipAvailable;
                if (modal) ui.Activate(item(TdUiAction.ModalTab, (int)TdUiMode.Runes));
                else ui.Activate(item(TdUiAction.RuneWindow, 0));
                int want = scene is UiScene.RuneWindowTipAvailable or UiScene.RunesTipAvailable ? 1 : scene is UiScene.RuneWindowTipAwake or UiScene.RunesTipAwake ? 2 : scene is UiScene.RuneWindow ? -1 : 0;
                if (want >= 0)
                {
                    var pop = ui.BuildPopover()!;
                    // nó de cada estado mais próximo de um canto (balão forçado a virar/ficar dentro da janela)
                    float fill = want == 1 ? TdUiController.RuneAvailable : want == 2 ? TdUiController.RuneOn : TdUiController.RuneLocked;
                    var nodes = pop.Items.Where(i => i.Action == TdUiAction.SelectRune && i.Fill == fill).ToArray();
                    ui.Hover(nodes[nodes.Length / 2]);
                }
                break;
            }
            case UiScene.RunesBranch or UiScene.RuneWindowBranch:
            {
                typeof(TdMatchState).GetProperty("Fragments")!.SetValue(match, 5L);
                for (int i = 0; i < 4; i++) match.Apply(new AwakenRune(match.Frontier[0]));
                typeof(TdMatchState).GetProperty("Fragments")!.SetValue(match, 2L);
                ui.Sync();
                if (scene == UiScene.RunesBranch) ui.Activate(item(TdUiAction.ModalTab, (int)TdUiMode.Runes)); else ui.Activate(item(TdUiAction.RuneWindow, 0));
                ui.Hover(ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.RuneBranch && i.SArg == "frost"));
                break;
            }
            case UiScene.RuneCard or UiScene.RuneWindowFromCard:
                typeof(TdMatchState).GetProperty("ReputationLostTotal")!.SetValue(match, match.ReputationLostTotal + 25 * 1);
                ui.Sync();
                if (scene == UiScene.RuneWindowFromCard) ui.Activate(ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.RuneTutorialOpen));
                break;
            case UiScene.Help: ui.OpenHowTo(); break;
            case UiScene.BaseAttacked:
                ui.Sync(); // referência: sem XP perdido ainda
                typeof(TdMatchState).GetProperty("ReputationLostTotal")!.SetValue(match, match.ReputationLostTotal + 25); ui.Sync();
                now += 0.5; break; // toast já visível (passou a entrada suave)
            case UiScene.Menu: ui.Activate(item(TdUiAction.Menu, 0)); break;
            case UiScene.Towers: ui.Activate(item(TdUiAction.TowersOpen, 0)); break;
            case UiScene.HudPlacing: ui.Activate(item(TdUiAction.BuildToggle, 0)); break;
            case UiScene.Map: ui.Activate(item(TdUiAction.MapOpen, 0)); break;
            case UiScene.MapLocked: ui.Activate(item(TdUiAction.MapOpen, 0)); ui.Activate(item(TdUiAction.SelectStage, 3)); break;
            case UiScene.Seals: ui.Activate(item(TdUiAction.MapOpen, 0)); ui.Activate(item(TdUiAction.MapTab, 1)); break;
            case UiScene.Victory:
                match.Apply(new StartWave());
                for (int i = 0; i < 30 * 600 && match.Phase == MatchPhase.Onda; i++) match.Step();
                ui.Sync();
                break;
        }
        return (ui, match);
    }

    private static SKBitmap Render(UiScene scene, int dpi, bool text, bool arrowTop = false)
    {
        var (ui, _) = Setup(scene);
        using var typo = new Typography();
        using var r = new TdUiRenderer(typo, dpi) { TextEnabled = text };
        if (scene == UiScene.HudTip)
        {
            var tipHud = ui.BuildHud();
            var tip = TdUiController.TooltipFor(tipHud, Array.FindIndex(tipHud.Items, i => i.Action == TdUiAction.HudRestore))!;
            var (tw, th) = r.PhysicalSize(TdUiRenderer.TooltipWidthDip, r.TooltipHeightDip(tip.Text));
            var tb = new SKBitmap(new SKImageInfo(tw, th, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var tc = new SKCanvas(tb);
            tc.Clear(Backdrop);
            using var tl = new SKBitmap(new SKImageInfo(tw, th, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var tlc = new SKCanvas(tl)) r.DrawTooltip(tlc, tip.Text);
            GoldenImage.Blit(tc, tl, 0, 0);
            return tb;
        }
        bool hud = scene is UiScene.HudMoney or UiScene.HudPrep or UiScene.HudWave or UiScene.HudMin or UiScene.HudLarge or UiScene.HudLargeMin or UiScene.HudPlacing or UiScene.HudSpeedExtras;
        if (hud)
        {
            var v = ui.BuildHud();
            var (w, h) = r.PhysicalSize(v.Width, v.Height);
            var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var c = new SKCanvas(bmp);
            c.Clear(Backdrop);
            if (scene is UiScene.HudLarge or UiScene.HudLargeMin)
            {
                // A large boss frame behind the HUD must not bleed through its opaque body.
                string root = Path.GetFullPath(Path.Combine(GoldenImage.GoldenDirectory(), "../../.."));
                using var boss = SKBitmap.Decode(Path.Combine(root, "assets/source/anim/chefe_botnet-walk.png"));
                c.DrawBitmap(boss, new SKRect(0, 0, 192, 192), new SKRect(w - 150, 0, w + 42, 192), new SKSamplingOptions(SKFilterMode.Nearest));
            }
            using var layer = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var lc = new SKCanvas(layer)) r.DrawHud(lc, v, 3);
            GoldenImage.Blit(c, layer, 0, 0);
            return bmp;
        }
        var p = ui.BuildPopover() ?? throw new InvalidOperationException(scene.ToString());
        if (arrowTop) p = p with { ArrowTop = true };
        var (pw, ph) = r.PhysicalSize(p.Width, p.Height);
        var pb = new SKBitmap(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var pc = new SKCanvas(pb);
        pc.Clear(Backdrop);
        using var pl = new SKBitmap(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
        int hoverIndex = ui.HoverRune is { } hr ? Array.FindIndex(p.Items, i => i.SArg == hr && i.Action == TdUiAction.SelectRune)
            : ui.HoverBranch is { } hb ? Array.FindIndex(p.Items, i => i.SArg == hb && i.Action == TdUiAction.RuneBranch) : 1;
        using (var plc = new SKCanvas(pl)) r.DrawPopover(plc, p, hoverIndex);
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
    [InlineData(96)]
    [InlineData(144)]
    public void Rune_window_below_hud_matches_golden(int dpi)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "golden com texto só no Linux");
        using var bmp = Render(UiScene.RuneWindow, dpi, text: true, arrowTop: true);
        string? failure = GoldenImage.Verify($"td-ui-runewindow-below-{dpi * 100 / 96}", bmp);
        Assert.True(failure is null, failure);
    }

    [Theory]
    [InlineData(1, 96)]
    [InlineData(2, 96)]
    [InlineData(3, 96)]
    [InlineData(1, 144)]
    [InlineData(2, 144)]
    [InlineData(3, 144)]
    public void Notice_sizes_match_golden(int size, int dpi)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "golden com texto só no Linux");
        float scale = size == 2 ? 1.25f : size == 3 ? 1.5f : 1f;
        float w = 180 * scale, h = TdUiController.ToastH * scale;
        var item = new TdUiItem(new SideWar.Runtime.Geometry.DipRect(0, 0, w, h), TdUiKind.Label,
            "Base sendo atacada", Arg: -1, SArg: "toast", Tone: TdTone.Warn, Fill: 1);
        var view = new TdPopoverView("Base", [item], w, h, 0);
        using var typo = new Typography();
        using var renderer = new TdUiRenderer(typo, dpi);
        var (pw, ph) = renderer.PhysicalSize(w, h);
        using var bmp = new SKBitmap(pw, ph);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(Backdrop);
        renderer.DrawPopover(canvas, view, -1);
        string? failure = GoldenImage.Verify($"td-ui-notice-{size}-{dpi * 100 / 96}", bmp);
        Assert.True(failure is null, failure);
    }

    private static SKBitmap RenderRankToast(int rank, float fill, int dpi)
    {
        var item = new TdUiItem(new SideWar.Runtime.Geometry.DipRect(0, 0, 200, TdUiController.ToastH), TdUiKind.Label,
            $"Promovido a {TdRank.DisplayName((TdRankLevel)rank)}!", Arg: rank, SArg: "toast", Tone: TdTone.Accent, Fill: fill, Sub: "glow");
        var view = new TdPopoverView("Promoção", [item], 200f, TdUiController.ToastH, 0f);
        using var typo = new Typography();
        using var r = new TdUiRenderer(typo, dpi) { TextEnabled = true };
        var (w, h) = r.PhysicalSize(view.Width, view.Height);
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        c.Clear(Backdrop);
        using var layer = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var lc = new SKCanvas(layer)) r.DrawPopover(lc, view, -1);
        GoldenImage.Blit(c, layer, 0, 0);
        return bmp;
    }

    /// <summary>Toast de promoção (uma linha, 22 DIP, coroa 20 px em 1x): meio da entrada e opaco.</summary>
    [Theory]
    [InlineData(1, 0.4f, 96)]
    [InlineData(1, 1.0f, 96)]
    [InlineData(3, 1.0f, 144)]
    public void Rank_toast_matches_golden(int rank, float fill, int dpi)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "golden com texto só no Linux");
        using var bmp = RenderRankToast(rank, fill, dpi);
        string? failure = GoldenImage.Verify($"td-ui-ranktoast-r{rank}-f{(int)(fill * 100)}-{dpi * 100 / 96}", bmp);
        Assert.True(failure is null, failure);
    }

    [Fact]
    public void Rank_toast_animation_changes_only_opacity()
    {
        using var start = RenderRankToast(2, 0.3f, 96);
        using var end = RenderRankToast(2, 1f, 96);
        Assert.False(start.Bytes.AsSpan().SequenceEqual(end.Bytes));
        Assert.Equal(200, end.Width);
        Assert.Equal(22, end.Height);
    }

    [Theory]
    [InlineData(0, 96)]
    [InlineData(5, 144)]
    public void Rank_tooltip_matches_golden(int rank, int dpi)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "golden com texto só no Linux");
        using var typo = new Typography();
        using var r = new TdUiRenderer(typo, dpi) { TextEnabled = true };
        var (w, h) = r.PhysicalSize(TdUiRenderer.TooltipWidthDip, 58);
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        c.Clear(Backdrop);
        using var layer = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var lc = new SKCanvas(layer)) r.DrawRankTooltip(lc, rank);
        GoldenImage.Blit(c, layer, 0, 0);
        string? failure = GoldenImage.Verify($"td-ui-ranktip-r{rank}-{dpi * 100 / 96}", bmp);
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

    /// <summary>A escala configurável do canto combina com o DPI do monitor.</summary>
    [Theory]
    [InlineData(125)]
    [InlineData(200)]
    public void Hud_MatchesGoldenAtCornerScale(int percent)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "texto Skia difere entre SOs; golden com texto só no Linux (RF-17.3)");
        int effDpi = new TdCornerScale(96, percent).EffectiveDpi;
        using var bmp = Render(UiScene.HudPrep, effDpi, text: true);
        string? failure = GoldenImage.Verify($"td-ui-hud-corner{percent}", bmp);
        Assert.True(failure is null, failure);
    }

    private static double Luminance(SKColor c)
    {
        static double Ch(byte v) { double x = v / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
        return 0.2126 * Ch(c.Red) + 0.7152 * Ch(c.Green) + 0.0722 * Ch(c.Blue);
    }

    [Fact]
    public void Hud_wave_ticks_follow_the_displayed_wave()
    {
        var item = new TdUiItem(new SideWar.Runtime.Geometry.DipRect(0, 0, 167, 28), TdUiKind.Label,
            "5/10", SArg: "wave");
        using var typo = new Typography();
        using var renderer = new TdUiRenderer(typo, 96) { TextEnabled = false };
        using var bmp = new SKBitmap(167, 28);
        using (var canvas = new SKCanvas(bmp))
            renderer.DrawHud(canvas, new TdHudView([item], 167, 28, Wave: 5, TotalWaves: 10), -1);
        Assert.Equal(Palette.Td.Amber, bmp.GetPixel(107, 13));
        Assert.Equal(new SKColor(0x2a, 0x3d, 0x58), bmp.GetPixel(161, 13));
    }

    private static double Contrast(SKColor a, SKColor b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Fact]
    public void Locked_rune_glyph_keeps_at_least_3_to_1_contrast_against_its_node_and_the_web()
    {
        Assert.True(Contrast(TdUiRenderer.LockedGlyphColor, TdUiRenderer.LockedNodeFill) >= 3.0, $"{Contrast(TdUiRenderer.LockedGlyphColor, TdUiRenderer.LockedNodeFill):0.00}");
        Assert.True(Contrast(TdUiRenderer.LockedGlyphColor, Palette.Td.Background) >= 3.0);
        Assert.True(Contrast(Palette.Td.Amber, Palette.Td.Background) >= 3.0);
    }

    /// <summary>Todo nó (despertado, disponível e bloqueado) desenha o glifo do ramo: há pixels da cor do glifo dentro do nó, sem ponto vazio.</summary>
    [Theory]
    [InlineData(UiScene.RunesBranch)]
    [InlineData(UiScene.RuneWindow)]
    public void Every_rune_node_draws_its_branch_glyph_in_every_state(UiScene scene)
    {
        var (ui, _) = Setup(scene);
        var pop = ui.BuildPopover()!;
        using var typo = new Typography();
        using var r = new TdUiRenderer(typo, 96) { TextEnabled = true };
        var (pw, ph) = r.PhysicalSize(pop.Width, pop.Height);
        using var bmp = new SKBitmap(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bmp)) { c.Clear(Backdrop); r.DrawPopover(c, pop, -1); }
        int checkedStates = 0;
        foreach (float fill in new[] { TdUiController.RuneOn, TdUiController.RuneAvailable, TdUiController.RuneLocked })
        {
            var node = pop.Items.First(i => i.Action == TdUiAction.SelectRune && i.Fill == fill && !i.Selected);
            var want = fill == TdUiController.RuneLocked ? TdUiRenderer.LockedGlyphColor : Palette.Td.Amber;
            float cx = node.Rect.X + node.Rect.W / 2f, cy = node.Rect.Y + node.Rect.H / 2f, rad = node.FontSize * 0.3f;
            int hits = 0;
            for (int y = (int)(cy - rad); y <= (int)(cy + rad); y++)
                for (int x = (int)(cx - rad); x <= (int)(cx + rad); x++)
                {
                    var px = bmp.GetPixel(x, y);
                    if (Math.Abs(px.Red - want.Red) + Math.Abs(px.Green - want.Green) + Math.Abs(px.Blue - want.Blue) < 90) hits++;
                }
            Assert.True(hits >= 6, $"{scene} fill={fill}: {hits}");
            checkedStates++;
        }
        Assert.Equal(3, checkedStates);
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
