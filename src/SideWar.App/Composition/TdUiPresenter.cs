using SideWar.Core;
using SideWar.Core.Td;
using SideWar.Platform.Windows.Input;
using SideWar.Platform.Windows.Threading;
using SideWar.Platform.Windows.Windows;
using SideWar.Rendering.Frame;
using SideWar.Rendering.Panel;
using SideWar.Rendering.Text;
using SideWar.Runtime;
using SideWar.Runtime.Composition;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Input;
using SideWar.Runtime.Td;

namespace SideWar.App.Composition;

/// <summary>
/// Lado Windows da UI da partida TD (TD.8): HUD na área de 160 DIP à direita da faixa, popover (construção, inspetor, preview, menu, Mapa,
/// fim de partida) acima da faixa, ESC temporário e modo Interativo durante a construção. A lógica é do <see cref="TdUiController"/>
/// (Runtime); aqui só janelas, renderer e fiação. Só na T-Game.
/// </summary>
internal sealed class TdUiPresenter : ITdUiHost, ITdOnboardingStore, IDisposable
{
    private const double TickSeconds = 0.1, KickSeconds = 0.03;

    private readonly GameThread game;
    private readonly GameRuntime runtime;
    private readonly TdComposition td;
    private readonly TdRenderDriver driver;
    private readonly Typography typography;
    private readonly IClock clock;
    private readonly Action<UiRequest> ui;
    private readonly Func<HotkeyService?> hotkeys;
    private readonly SkiaFrameCanvas hudCanvas = new(), popCanvas = new();
    private readonly TdUiController controller;
    private readonly SkiaFrameCanvas tipCanvas = new();
    private readonly TdFocusState focus;
    private readonly int bandHeight;
    private readonly float k, hudScale;
    private readonly Action menuChanged;
    private volatile bool hudMinimized;
    private volatile bool tutorialDone;
    private volatile string tipsSeenText;
    private volatile bool bonusQuestions;
    private volatile string questionsSeenText;
    private readonly HashSet<string> tipsSeen;
    private TdUiWindow? hudWindow, popWindow, tipWindow;
    private int tipIndex = -1;
    private string? tipText;
    private bool tipFirstTime;
    private readonly SideWar.Rendering.Sprites.SpriteAtlas? atlas;
    private TdUiRenderer? renderer;
    private int dpi;
    private TdHudView? hud;
    private TdPopoverView? pop;
    private int hudHover = -1, popHover = -1;
    private long hudSig = long.MinValue, popSig = long.MinValue;
    private PixelRect popBounds;
    private bool buildApplied, escApplied, disposed;
    private long? deadline;

    public TdUiPresenter(GameThread game, GameRuntime runtime, TdComposition td, TdRenderDriver driver, Typography typography,
        IClock clock, Action<UiRequest> ui, Func<HotkeyService?> hotkeys,
        TdFocusState focus, int bandHeight, bool hudMinimized, Action menuChanged, SideWar.Rendering.Sprites.SpriteAtlas? atlas = null,
        bool tutorialDone = false, string tipsSeen = "", bool bonusQuestions = true, string questionsSeen = "")
    {
        this.bonusQuestions = bonusQuestions;
        questionsSeenText = questionsSeen ?? "";
        this.tutorialDone = tutorialDone;
        tipsSeenText = tipsSeen ?? "";
        this.tipsSeen = new HashSet<string>(tipsSeenText.Split(';', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        this.focus = focus; this.bandHeight = bandHeight; this.hudMinimized = hudMinimized; this.menuChanged = menuChanged;
        k = TdBand.Scale(bandHeight);
        hudScale = TdBand.HudScale(bandHeight);
        this.atlas = atlas;
        this.game = game; this.runtime = runtime; this.td = td; this.driver = driver; this.typography = typography;
        this.clock = clock; this.ui = ui; this.hotkeys = hotkeys;
        TdQuestionPicker? picker = null;
        try { picker = new TdQuestionPicker(SideWar.Core.Content.Td.TdQuestionBank.LoadEmbedded(), questionsSeenText, Random.Shared.Next, SetQuestionsSeen); }
        catch (Exception) { /* banco indisponível: sem pergunta-bônus */ }
        controller = new TdUiController(() => td.Host.Match, Send, this, () => clock.NowTicks / (double)clock.TicksPerSecond, this, picker);
        td.Host.WaveGate = () => !controller.StartBlocked;
        controller.Changed += () => { Refresh(false); Kick(); };
        focus.Changed += ApplyFade;
    }

    /// <summary>HUD recolhido a um chip (lembrado em settings; a bandeja tem "Mostrar HUD").</summary>
    public bool HudMinimized => hudMinimized;

    /// <summary>Item "Mostrar HUD/Recolher HUD" da bandeja.</summary>
    public void ToggleHud() => SetHudMinimized(!hudMinimized);

    /// <summary>"Como jogar" da bandeja (TD.15).</summary>
    public void OpenHowTo() { controller.OpenHowTo(); Kick(); }

    /// <summary>TD.16b: atalho global "Coletar caixa" (a faixa é click-through; o botão do HUD usa o mesmo comando).</summary>
    public void CollectCrate()
    {
        Present();
        runtime.NotifyInteraction();
        td.Host.RequestCollectCrate();
        Kick();
    }

    public string TipsSeen => tipsSeenText;
    public string QuestionsSeen => questionsSeenText;

    /// <summary>td.bonusQuestions (Configurações): liga/desliga a pergunta-bônus V/F do Preparo.</summary>
    public bool BonusQuestions => bonusQuestions;

    public void SetBonusQuestions(bool on)
    {
        if (bonusQuestions == on) return;
        bonusQuestions = on;
        Kick();
    }

    private void SetQuestionsSeen(string seen)
    {
        questionsSeenText = seen;
        ui(UiRequest.SaveOnboarding);
    }

    // ITdOnboardingStore (T-Game): grava nas configurações pela T-UI.
    public bool TutorialDone => tutorialDone;

    public void SetTutorialDone(bool done)
    {
        if (tutorialDone == done) return;
        tutorialDone = done;
        ui(UiRequest.SaveOnboarding);
    }

    public bool TipSeen(string id) => tipsSeen.Contains(id);

    public void MarkTipSeen(string id)
    {
        if (!tipsSeen.Add(id)) return;
        tipsSeenText = string.Join(';', tipsSeen.Order(StringComparer.Ordinal));
        ui(UiRequest.SaveOnboarding);
    }

    /// <summary>TD.14: aplica a opacidade de ausência (faixa, HUD, popover e dica) e o teto de 8 fps do render.</summary>
    private void ApplyFade()
    {
        if (disposed) return;
        byte alpha = focus.Alpha;
        game.Mode.SetTdFade(alpha);
        hudWindow?.Surface.SetOpacity(alpha);
        popWindow?.Surface.SetOpacity(alpha);
        tipWindow?.Surface.SetOpacity(alpha);
        driver.Absent = focus.Absent;
        driver.MarkDirty();
        Kick();
    }

    /// <summary>Mouse sobre a faixa/HUD/popover ou clique: o jogador está presente (volta a 100%, fps normal).</summary>
    private void Present() => focus.OnInteraction();

    public TdUiController Controller => controller;
    public long? NextDeadline => disposed ? null : deadline;

    public void Setup()
    {
        hudWindow = game.Scene.GetOrCreateTdHud();
        popWindow = game.Scene.GetOrCreateTdPopover();
        tipWindow = game.Scene.GetOrCreateTdTooltip();
        hudWindow.Moved += (x, y) =>
        {
            Present();
            int h = hud?.HitTest(x / UiScale, y / UiScale) ?? -1;
            int t = hud?.TipAt(x / UiScale, y / UiScale) ?? -1;
            if (h != hudHover || t != tipIndex) { hudHover = h; tipIndex = t; Refresh(true); }
        };
        hudWindow.Left += () => { if (hudHover != -1 || tipIndex != -1) { hudHover = -1; tipIndex = -1; Refresh(true); } };
        hudWindow.LeftUp += (x, y) => { tipIndex = -1; Click(hud?.Items, hud?.HitTest(x / UiScale, y / UiScale) ?? -1); };
        popWindow.Moved += (x, y) =>
        {
            Present();
            int h = pop?.HitTest(x / UiScale, y / UiScale) ?? -1;
            if (h == popHover) return;
            popHover = h;
            controller.Hover(h >= 0 ? pop!.Items[h] : null);
            Refresh(true);
        };
        popWindow.Left += () => { if (popHover != -1) { popHover = -1; controller.Hover(null); Refresh(true); } };
        popWindow.LeftUp += (x, y) => Click(pop?.Items, pop?.HitTest(x / UiScale, y / UiScale) ?? -1);
        popWindow.RightUp += (_, _) => controller.Cancel();
        tipWindow.LeftUp += (_, _) => { Present(); controller.DismissTip(); };

        game.Mode.TdPointerMoved += (x, _) => { Present(); controller.PointerMoved(x / k); };
        game.Mode.TdRightClicked += () => { Present(); runtime.NotifyInteraction(); controller.SceneRightClick(); };
        game.Mode.TdLeftClicked += (x, y, target) =>
        {
            Present();
            runtime.NotifyInteraction();
            controller.SceneLeftClick(x / k, y / k, target);
            Kick();
        };
        Relayout();
        hudWindow.Show();
        ApplyFade();
        Refresh(true);
        Kick();
    }

    /// <summary>DPI do monitor / 96 (pixels por DIP real).</summary>
    private float Scale => game.Dock.Current is { } c ? c.Monitor.Dpi / 96f : 1f;

    /// <summary>Pixels por DIP de desenho do HUD/popover/dica: DPI × escala do HUD (proporcional à faixa).</summary>
    private float UiScale => Scale * hudScale;

    private void Click(TdUiItem[]? items, int index)
    {
        if (items is null || index < 0 || index >= items.Length) return;
        Present();
        runtime.NotifyInteraction();
        var item = items[index];
        controller.Activate(item);
        Kick();
    }

    private void Send(IGameCommand command)
    {
        td.Host.OnCommand(command);
        try { game.Post(static () => { }); } catch (InvalidOperationException) { }
    }

    /// <summary>Ocultação da cena (tela cheia, sessão bloqueada, atalho): cancela o modo construção/popover (RF-28.20 passo 5).</summary>
    public void OnSceneHidden() => controller.Cancel();

    public void Relayout()
    {
        if (disposed || game.Dock.Current is not { } c) return;
        int newDpi = (int)c.Monitor.Dpi;
        if (renderer is null || newDpi != dpi)
        {
            renderer?.Dispose();
            renderer = new TdUiRenderer(typography, (int)MathF.Round(newDpi * hudScale), atlas);
            dpi = newDpi;
        }
        hudSig = popSig = long.MinValue;
        Refresh(true);
    }

    private PixelRect HudBounds(DockLayoutResult c, TdHudView model)
    {
        int w = (int)MathF.Round(model.Width * UiScale), h = (int)MathF.Round(model.Height * UiScale);
        // TD.16: HUD (ou chip recolhido) flutua logo ACIMA da faixa, alinhado à direita, sem cobri-la.
        int bottom = Math.Max(c.Monitor.WorkArea.Top + h, c.Scene.Top - (int)MathF.Round(2 * Scale));
        return new PixelRect(c.Scene.Right - w, bottom - h, c.Scene.Right, bottom);
    }

    public void Kick() => deadline = clock.NowTicks + (long)(KickSeconds * clock.TicksPerSecond);

    public void OnWake()
    {
        if (disposed) return;
        controller.Sync();
        Refresh(false);
        var m = td.Host.Match;
        bool busy = runtime.App.IsVisible && (m.Phase == MatchPhase.Onda || controller.PopoverOpen || controller.CurrentTip is not null || m.CountdownRemainingSeconds > 0 || m.CrateActive || controller.QuizActive || controller.Pulsing);
        deadline = busy ? clock.NowTicks + (long)(TickSeconds * clock.TicksPerSecond) : null;
    }

    private void Refresh(bool force)
    {
        if (disposed || renderer is null || hudWindow is null || popWindow is null || tipWindow is null || game.Dock.Current is not { } c) return;
        float s = UiScale, ds = Scale;
        hud = controller.BuildHud();
        var hudBounds = HudBounds(c, hud);
        bool hudMoved = hudBounds != hudWindow.Bounds;
        if (hudMoved) { hudWindow.Apply(hudBounds); hudCanvas.Invalidate(); }
        long hs = hud.Signature() * 31 + hudHover;
        if (force || hudMoved || hs != hudSig)
        {
            hudSig = hs;
            Draw(hudWindow, hudCanvas, cv => renderer.DrawHud(cv, hud, hudHover));
        }
        RefreshTooltip(c, hudBounds);
        pop = controller.BuildPopover();
        if (pop is null)
        {
            if (popWindow.WantsVisible) { popWindow.Hide(); popCanvas.Invalidate(); }
            popSig = long.MinValue;
        }
        else
        {
            int w = (int)MathF.Round(pop.Width * s), h = (int)MathF.Round(pop.Height * s);
            int left, bottom;
            var work = c.Monitor.WorkArea;
            if (pop.AnchorX > 0)
            {
                // AnchorX está no espaço de desenho da faixa (32 DIP de altura): × k para DIP reais.
                left = c.Scene.Left + (int)MathF.Round(pop.AnchorX * k * ds) - w / 2;
                bottom = c.Scene.Top - (int)MathF.Round(2 * ds);
                // TD.16: o HUD fica acima da faixa; se o popover cair sob ele, sobe acima do HUD.
                if (left < hudBounds.Right && left + w > hudBounds.Left) bottom = hudBounds.Top - (int)MathF.Round(2 * ds);
            }
            else if (pop.TargetHudX >= 0)
            {
                // Balão do tutorial apontando para um item do HUD: centrado nele (TD.15).
                left = hudBounds.Left + (int)MathF.Round(pop.TargetHudX * s) - w / 2;
                bottom = hudBounds.Top - (int)MathF.Round(2 * ds);
            }
            else
            {
                left = hudBounds.Right - w;
                bottom = hudBounds.Top - (int)MathF.Round(2 * ds);
            }
            left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - w));
            if (pop.HasArrow)
            {
                // A seta aponta para o alvo mesmo quando a janela foi deslocada pelo limite da tela.
                float targetPx = pop.AnchorX > 0 ? c.Scene.Left + pop.AnchorX * k * ds : hudBounds.Left + pop.TargetHudX * s;
                pop = pop with { ArrowX = (targetPx - left) / s };
            }
            int top = Math.Max(work.Top, bottom - h);
            var bounds = new PixelRect(left, top, left + w, top + h);
            long ps = pop.Signature() * 31 + popHover;
            bool moved = bounds != popBounds || !popWindow.WantsVisible;
            if (moved) { popWindow.Apply(bounds); popBounds = bounds; }
            if (!popWindow.WantsVisible) { popWindow.Show(); popWindow.Surface.SetOpacity(focus.Alpha); }
            if (force || moved || ps != popSig)
            {
                popSig = ps;
                Draw(popWindow, popCanvas, cv => renderer.DrawPopover(cv, pop, popHover));
            }
        }
        var view = controller.View;
        if (view != driver.View)
        {
            driver.SetView(view);
            if (runtime.App.IsVisible) driver.ForceDraw(); // evento de mouse: desenha já (o laço pode não acordar a tempo)
        }
        if (controller.BuildActive != buildApplied) { buildApplied = controller.BuildActive; game.Mode.SetBuildMode(buildApplied); }
        bool esc = controller.PopoverOpen;
        if (esc != escApplied) { escApplied = esc; hotkeys()?.SetEscapeActive(esc); }
    }

    /// <summary>Dica do item do HUD sob o mouse (texto completo): janela acima do HUD, longe do cursor.</summary>
    private void RefreshTooltip(DockLayoutResult c, PixelRect hudBounds)
    {
        var tip = hud is null || tipWindow is null ? null : TdUiController.TooltipFor(hud, tipIndex);
        // Dica de 1ª vez (TD.15): ocupa a janela do tooltip quando não há dica de hover.
        bool firstTime = tip is null && controller.CurrentTip is not null;
        if (firstTime) tip = new TdTooltipView(controller.CurrentTip!, hud!.Width / 2f, 0f);
        if (tip is null)
        {
            if (tipWindow!.WantsVisible) { tipWindow.Hide(); tipCanvas.Invalidate(); }
            tipText = null;
            return;
        }
        float s = UiScale;
        int w = (int)MathF.Round(TdUiRenderer.TooltipWidthDip * s), h = (int)MathF.Round(renderer!.TooltipHeightDip(tip.Text) * s);
        var work = c.Monitor.WorkArea;
        int left = Math.Clamp(hudBounds.Left + (int)MathF.Round(tip.AnchorX * s) - w / 2, work.Left, Math.Max(work.Left, work.Right - w));
        int bottom = hudBounds.Top - (int)MathF.Round(2 * Scale);
        var bounds = new PixelRect(left, Math.Max(work.Top, bottom - h), left + w, Math.Max(work.Top, bottom - h) + h);
        var window = tipWindow!;
        bool changed = tipText != tip.Text || bounds != window.Bounds || !window.WantsVisible || tipFirstTime != firstTime;
        if (!changed) return;
        window.Apply(bounds);
        if (!window.WantsVisible) { window.Show(); window.Surface.SetOpacity(focus.Alpha); }
        tipCanvas.Invalidate();
        tipText = tip.Text;
        tipFirstTime = firstTime;
        Draw(window, tipCanvas, cv => renderer.DrawTooltip(cv, tip.Text, firstTime));
    }

    private static void Draw(TdUiWindow window, SkiaFrameCanvas canvas, Action<SkiaSharp.SKCanvas> draw)
    {
        if (!window.IsVisible && !window.WantsVisible) return;
        var frame = window.Surface.BeginFrame();
        draw(canvas.Begin(frame));
        canvas.End();
        window.Surface.EndFrame(null);
    }

    // ---------------------------------------------------------------- ITdUiHost

    public bool Paused => (runtime.App.PauseReasons & Runtime.Timing.PauseReason.User) != 0;
    public void TogglePause() => runtime.TogglePause();
    public void OpenSettings() => ui(UiRequest.OpenSettings);
    public void RequestExit() => ui(UiRequest.Exit);
    bool ITdUiHost.HudMinimized => hudMinimized;

    public void SetHudMinimized(bool minimized)
    {
        if (hudMinimized == minimized) return;
        hudMinimized = minimized;
        tipIndex = -1; hudHover = -1;
        ui(UiRequest.SaveHudState);
        menuChanged();
        Refresh(true);
        Kick();
    }
    public void StartStage(int stageId) { td.StartStage(stageId); driver.MarkDirty(); }
    public int StageCount => td.StageCount;
    public bool StageUnlocked(int stageId) => td.Recorder.StageUnlocked(stageId);
    public int StageStars(int stageId) => td.Recorder.StageStars(stageId);
    public bool StageCrown(int stageId) => td.Recorder.StageCrown(stageId);
    public int StageMedals(int stageId) => td.Recorder.StageMedals(stageId);
    public int StageBestScore(int stageId) => td.Recorder.StageBestScore(stageId);
    public TdRankProgress Rank => td.Recorder.Rank;
    public TdRecordOutcome LastOutcome => td.Recorder.LastOutcome;
    public int SealsAvailable => td.Recorder.SealsAvailable;
    public bool NodePurchased(string nodeId) => td.Recorder.NodePurchased(nodeId);
    public bool PurchaseNode(string nodeId) => td.Recorder.Purchase(nodeId);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        renderer?.Dispose();
        hudCanvas.Dispose();
        popCanvas.Dispose();
        tipCanvas.Dispose();
        focus.Changed -= ApplyFade;
    }
}
