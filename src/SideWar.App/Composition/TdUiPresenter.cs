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
internal sealed class TdUiPresenter : ITdUiHost, IDisposable
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
    private TdUiWindow? hudWindow, popWindow;
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
        IClock clock, Action<UiRequest> ui, Func<HotkeyService?> hotkeys, SideWar.Rendering.Sprites.SpriteAtlas? atlas = null)
    {
        this.atlas = atlas;
        this.game = game; this.runtime = runtime; this.td = td; this.driver = driver; this.typography = typography;
        this.clock = clock; this.ui = ui; this.hotkeys = hotkeys;
        controller = new TdUiController(() => td.Host.Match, Send, this, () => clock.NowTicks / (double)clock.TicksPerSecond);
        controller.Changed += () => { Refresh(false); Kick(); };
    }

    public TdUiController Controller => controller;
    public long? NextDeadline => disposed ? null : deadline;

    public void Setup()
    {
        hudWindow = game.Scene.GetOrCreateTdHud();
        popWindow = game.Scene.GetOrCreateTdPopover();
        hudWindow.Moved += (x, y) => { int h = hud?.HitTest(x / Scale, y / Scale) ?? -1; if (h != hudHover) { hudHover = h; Refresh(true); } };
        hudWindow.Left += () => { if (hudHover != -1) { hudHover = -1; Refresh(true); } };
        hudWindow.LeftUp += (x, y) => Click(hud?.Items, hud?.HitTest(x / Scale, y / Scale) ?? -1);
        popWindow.Moved += (x, y) =>
        {
            int h = pop?.HitTest(x / Scale, y / Scale) ?? -1;
            if (h == popHover) return;
            popHover = h;
            controller.Hover(h >= 0 ? pop!.Items[h] : null);
            Refresh(true);
        };
        popWindow.Left += () => { if (popHover != -1) { popHover = -1; controller.Hover(null); Refresh(true); } };
        popWindow.LeftUp += (x, y) => Click(pop?.Items, pop?.HitTest(x / Scale, y / Scale) ?? -1);
        popWindow.RightUp += (_, _) => controller.Cancel();

        game.Mode.TdPointerMoved += (x, _) => controller.PointerMoved(x);
        game.Mode.TdRightClicked += () => { runtime.NotifyInteraction(); controller.SceneRightClick(); };
        game.Mode.TdLeftClicked += (x, y, target) =>
        {
            runtime.NotifyInteraction();
            if (target.Kind == HitKind.TdCrate) td.Host.RequestCollectCrate();
            else controller.SceneLeftClick(x, y, target);
            Kick();
        };
        Relayout();
        hudWindow.Show();
        Refresh(true);
        Kick();
    }

    private float Scale => game.Dock.Current is { } c ? c.Monitor.Dpi / 96f : 1f;

    private void Click(TdUiItem[]? items, int index)
    {
        if (items is null || index < 0 || index >= items.Length) return;
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
            renderer = new TdUiRenderer(typography, newDpi, atlas);
            dpi = newDpi;
        }
        float s = Scale;
        int w = (int)MathF.Round(TdHudView.DefaultWidth * s), h = (int)MathF.Round(TdHudView.DefaultHeight * s);
        // HUD: logo à direita da faixa, base alinhada à base dela (área de 160 DIP reservada pela TD.7).
        hudWindow?.Apply(new PixelRect(c.Scene.Right, c.Scene.Bottom - h, c.Scene.Right + w, c.Scene.Bottom));
        hudSig = popSig = long.MinValue;
        Refresh(true);
    }

    public void Kick() => deadline = clock.NowTicks + (long)(KickSeconds * clock.TicksPerSecond);

    public void OnWake()
    {
        if (disposed) return;
        controller.Sync();
        Refresh(false);
        var m = td.Host.Match;
        bool busy = runtime.App.IsVisible && (m.Phase == MatchPhase.Onda || controller.PopoverOpen || m.CountdownRemainingSeconds > 0 || m.CrateActive);
        deadline = busy ? clock.NowTicks + (long)(TickSeconds * clock.TicksPerSecond) : null;
    }

    private void Refresh(bool force)
    {
        if (disposed || renderer is null || hudWindow is null || popWindow is null || game.Dock.Current is not { } c) return;
        float s = Scale;
        hud = controller.BuildHud();
        long hs = hud.Signature() * 31 + hudHover;
        if (force || hs != hudSig)
        {
            hudSig = hs;
            Draw(hudWindow, hudCanvas, cv => renderer.DrawHud(cv, hud, hudHover));
        }
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
                left = c.Scene.Left + (int)MathF.Round(pop.AnchorX * s) - w / 2;
                bottom = c.Scene.Top - (int)MathF.Round(2 * s);
            }
            else
            {
                left = c.Scene.Right + (int)MathF.Round(TdHudView.DefaultWidth * s) - w;
                bottom = c.Scene.Bottom - (int)MathF.Round(TdHudView.DefaultHeight * s) - (int)MathF.Round(2 * s);
            }
            left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - w));
            int top = Math.Max(work.Top, bottom - h);
            var bounds = new PixelRect(left, top, left + w, top + h);
            long ps = pop.Signature() * 31 + popHover;
            bool moved = bounds != popBounds || !popWindow.WantsVisible;
            if (moved) { popWindow.Apply(bounds); popBounds = bounds; }
            if (!popWindow.WantsVisible) popWindow.Show();
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
    public void StartStage(int stageId) { td.StartStage(stageId); driver.MarkDirty(); }
    public int StageCount => td.StageCount;
    public bool StageUnlocked(int stageId) => td.Recorder.StageUnlocked(stageId);
    public int StageStars(int stageId) => td.Recorder.StageStars(stageId);
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
    }
}
