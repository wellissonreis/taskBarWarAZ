using SideWar.Platform.Windows.Threading;
using SideWar.Platform.Windows.Windows;
using SideWar.Rendering.Animation;
using SideWar.Rendering.Frame;
using SideWar.Rendering.Icons;
using SideWar.Rendering.Panel;
using SideWar.Rendering.Scene;
using SideWar.Rendering.Text;
using SideWar.Runtime;
using SideWar.Runtime.Composition;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.LiveEvents;
using SideWar.Runtime.Panel;
using SideWar.Runtime.Rendering;

namespace SideWar.App.Composition;

/// <summary>
/// Lado Windows dos eventos ao vivo (SDD-22 RF-22.8, SDD-26; T6.11b): as janelas hotspot do banner (pico, "defesa escalada",
/// chefe) e da caixa de suprimentos, desenhadas pelo <see cref="HotspotRenderer"/>, e o card de pergunta do chefe na
/// <see cref="QuestionCardWindow"/> (compartilhada com o card offline: a pergunta do COLETAR ×2 tem prioridade e o card do chefe
/// volta quando ela fecha). Cria tudo sob demanda, sem foco. Também anima o anel/piscar a no máximo 15 Hz, só visível e com a
/// simulação andando (<see cref="ILoopParticipant"/>). Só na T-Game.
/// </summary>
internal sealed class LiveEventsPresenter : ILiveEventsPresenter, ILoopParticipant, IDisposable
{
    private readonly GameThread game;
    private readonly GameRuntime runtime;
    private readonly Typography typography;
    private readonly IconCatalog icons;
    private readonly SceneRenderer scene;
    private readonly Func<AnimationPolicy> policy;
    private readonly Func<bool> offlineQuestionOpen;
    private readonly Action changed;
    private readonly SkiaFrameCanvas bannerCanvas = new();
    private readonly SkiaFrameCanvas crateCanvas = new();
    private readonly SkiaFrameCanvas questionCanvas = new();

    private LiveEventsCoordinator? coordinator;
    private HotspotRenderer? hotspots;
    private QuestionCardRenderer? questionRenderer;
    private int hotspotDpi, questionDpi;
    private HotspotWindow? bannerWindow, crateWindow;
    private LiveEventsView view = LiveEventsView.Empty;
    private BannerVariant bannerVariant;
    private string bannerChip = "";

    private QuestionCardWindow? questionWindow;
    private Action<OverlayFrame, OverlayRenderContext>? questionDraw;
    private QuestionCardModel? card;
    private QuestionCardLayout? cardLayout;
    private QuestionHit pressed;
    private long nextAnim;
    private long nextRetry;
    private bool disposed;

    public LiveEventsPresenter(GameThread game, GameRuntime runtime, Typography typography, IconCatalog icons, SceneRenderer scene,
        Func<AnimationPolicy> policy, Func<bool> offlineQuestionOpen, Action changed)
    {
        this.game = game;
        this.runtime = runtime;
        this.typography = typography;
        this.icons = icons;
        this.scene = scene;
        this.policy = policy;
        this.offlineQuestionOpen = offlineQuestionOpen;
        this.changed = changed;
    }

    public void Attach(LiveEventsCoordinator value) => coordinator = value;

    // ---------------------------------------------------------------- ILiveEventsPresenter

    public void ViewChanged(LiveEventsView value)
    {
        if (disposed) return;
        view = value;
        SyncBanner();
        SyncCrate();
        changed(); // o chip "×3 m:ss" do widget acompanha a visão
    }

    public void BossCardChanged(QuestionCardModel? value)
    {
        if (disposed) return;
        card = value;
        SyncCard();
    }

    /// <summary>Novo monitor/DPI: reposiciona e redesenha o que estiver visível.</summary>
    public void Relayout()
    {
        if (disposed) return;
        SyncBanner();
        SyncCrate();
        if (card is not null) SyncCard();
    }

    // ---------------------------------------------------------------- hotspots

    private bool TryScene(out PixelRect rect, out uint dpi)
    {
        rect = default;
        dpi = 0;
        if (game.Dock.Current is not { } current) return false;
        rect = current.Scene;
        dpi = current.Monitor.Dpi;
        EnsureHotspots((int)dpi);
        return true;
    }

    private void EnsureHotspots(int dpi)
    {
        if (hotspots is null) hotspots = new HotspotRenderer(scene.SpriteCache, typography, icons);
        if (hotspotDpi != dpi)
        {
            hotspots.SetDpi(dpi);
            hotspotDpi = dpi;
        }
    }

    private void SyncBanner()
    {
        if (view.Banner is not { } banner)
        {
            bannerWindow?.Hide();
            return;
        }
        if (!TryScene(out var sceneRect, out uint dpi)) return;
        bannerVariant = banner.Kind switch
        {
            LiveBannerKind.Boss => BannerVariant.Boss,
            LiveBannerKind.PeakScaled => BannerVariant.PeakScaled,
            _ => BannerVariant.Peak,
        };
        bannerChip = HotspotRenderer.FormatChip(banner.ChipSeconds);
        var window = bannerWindow ??= CreateBanner();
        var place = HotspotGeometry.Banner(sceneRect, dpi, hotspots!.MeasureBannerWidth(bannerVariant, bannerChip));
        if (place.Window != window.Placement.Window || place.Control != window.Placement.Control) window.Apply(place);
        window.Show((int)banner.Kind);
        DrawBanner();
    }

    private HotspotWindow CreateBanner()
    {
        var window = game.Scene.GetOrCreateBanner();
        window.Clicked += OnHotspotClicked;
        return window;
    }

    private void SyncCrate()
    {
        if (view.Crate is not { } crate)
        {
            crateWindow?.Hide();
            return;
        }
        if (!TryScene(out var sceneRect, out uint dpi)) return;
        var window = crateWindow ??= CreateCrate();
        var place = HotspotGeometry.SupplyBox(sceneRect, dpi, crate.X);
        if (place.Window != window.Placement.Window) window.Apply(place);
        window.Show(1);
        DrawCrate();
    }

    private HotspotWindow CreateCrate()
    {
        var window = game.Scene.GetOrCreateSupplyBox();
        window.Clicked += OnHotspotClicked;
        return window;
    }

    private void OnHotspotClicked(HotspotKind kind, int id)
    {
        if (kind == HotspotKind.Banner) coordinator?.BannerClicked();
        else coordinator?.CrateClicked();
    }

    private void DrawBanner()
    {
        if (bannerWindow is not { IsVisible: true } window || hotspots is null) return;
        var frame = window.Surface.BeginFrame();
        hotspots.DrawBanner(bannerCanvas.Begin(frame), bannerVariant, bannerChip, runtime.Host.PresentationSeconds, policy());
        bannerCanvas.End();
        window.Surface.EndFrame(null);
    }

    private void DrawCrate()
    {
        if (crateWindow is not { IsVisible: true } window || hotspots is null) return;
        var frame = window.Surface.BeginFrame();
        hotspots.DrawSupplyBox(crateCanvas.Begin(frame), runtime.Host.PresentationSeconds, policy());
        crateCanvas.End();
        window.Surface.EndFrame(null);
    }

    // ---------------------------------------------------------------- animação (adPulse/adBlink, ≤ 15 Hz) e retomada do card

    public long? NextDeadline
    {
        get
        {
            if (disposed || !runtime.App.IsVisible) return null;
            long? next = null;
            if (AnimationNeeded) next = nextAnim;
            if (card is not null && !OwnsQuestionWindow) next = next is { } n ? Math.Min(n, nextRetry) : nextRetry;
            return next;
        }
    }

    private bool AnimationNeeded =>
        runtime.App.SimulationRunning && policy().AmbientEnabled && (bannerWindow is { IsVisible: true } || crateWindow is { IsVisible: true });

    public void OnWake(IClock clock)
    {
        if (disposed || !runtime.App.IsVisible) return;
        long now = clock.NowTicks;
        if (AnimationNeeded && now >= nextAnim)
        {
            nextAnim = now + (long)(clock.TicksPerSecond / AnimationPolicy.PulseMaxFps);
            DrawBanner();
            DrawCrate();
        }
        else if (!AnimationNeeded) nextAnim = now;
        if (card is not null && !OwnsQuestionWindow && now >= nextRetry)
        {
            nextRetry = now + (long)clock.TicksPerSecond;
            SyncCard();
        }
    }

    // ---------------------------------------------------------------- card de pergunta do chefe

    private bool OwnsQuestionWindow => questionWindow is { IsVisible: true } w && w.Renderer == questionDraw;

    private void SyncCard()
    {
        if (card is not { } model)
        {
            if (OwnsQuestionWindow) questionWindow!.Hide();
            return;
        }
        if (offlineQuestionOpen()) return; // a pergunta do COLETAR ×2 usa a mesma janela e tem prioridade (retomada em OnWake)
        cardLayout = QuestionCardLayout.Compute(model, new PanelTextMetricsSkia(typography));
        if (!OverlayPlacements.TryPlace(game, OverlayKind.QuestionCard, cardLayout.Height, out var placement)) return;
        int dpi = (int)game.Dock.Current!.Monitor.Dpi;
        if (questionRenderer is null || questionDpi != dpi)
        {
            questionRenderer?.Dispose();
            questionRenderer = new QuestionCardRenderer(typography, icons, dpi);
            questionDpi = dpi;
        }
        var window = questionWindow ??= game.Scene.GetOrCreateQuestionCard();
        questionDraw ??= (frame, _) => DrawQuestion(frame);
        window.Renderer = questionDraw;
        window.Interaction = () => coordinator?.NotifyInteraction();
        window.LeftDown = (x, y) => pressed = QuestionHitAt(x, y);
        window.LeftUp = OnQuestionUp;
        window.Apply(placement);
        window.Show();
        window.Redraw();
    }

    private void DrawQuestion(OverlayFrame frame)
    {
        if (questionRenderer is null || card is null || cardLayout is null) return;
        var canvas = questionCanvas.Begin(frame);
        questionRenderer.Draw(canvas, card, cardLayout);
        questionCanvas.End();
    }

    private QuestionHit QuestionHitAt(int x, int y)
    {
        if (cardLayout is null || card is null || questionWindow is null) return default;
        float s = (float)questionWindow.Placement.Scale;
        return cardLayout.Hit(x / s, y / s, card);
    }

    private void OnQuestionUp(int x, int y)
    {
        var down = pressed;
        pressed = default;
        var up = QuestionHitAt(x, y);
        if (down != up || !OwnsQuestionWindow) return;
        switch (up.Kind)
        {
            case QuestionHitKind.Option: coordinator?.BossAnswered(up.Index); break;
            case QuestionHitKind.Minimize:
            case QuestionHitKind.Close: coordinator?.BossClosed(); break; // só esconde; o tempo corre (RF-26.10)
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (bannerWindow is not null) bannerWindow.Clicked -= OnHotspotClicked;
        if (crateWindow is not null) crateWindow.Clicked -= OnHotspotClicked;
        hotspots?.Dispose();
        questionRenderer?.Dispose();
        bannerCanvas.Dispose();
        crateCanvas.Dispose();
        questionCanvas.Dispose();
    }
}
