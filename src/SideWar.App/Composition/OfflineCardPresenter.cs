using SideWar.Platform.Windows.Threading;
using SideWar.Platform.Windows.Windows;
using SideWar.Rendering.Frame;
using SideWar.Rendering.Icons;
using SideWar.Rendering.Panel;
using SideWar.Rendering.Text;
using SideWar.Runtime.Composition;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Panel;
using SideWar.Runtime.Presence;

namespace SideWar.App.Composition;

/// <summary>
/// Lado Windows do card "Bem-vindo de volta" e da pergunta do COLETAR ×2 (SDD-24 RF-24.2/24.9/24.10; T4.10b). Cria sob
/// demanda a <see cref="OfflineCardWindow"/> e a <see cref="QuestionCardWindow"/> (NOACTIVATE, sem foco), desenha com
/// <see cref="OfflineCardRenderer"/>/<see cref="QuestionCardRenderer"/>, posiciona com <see cref="PanelGeometry"/> sobre a
/// faixa e repassa o mouse (hit-test em Runtime) ao <see cref="OfflineReturnCoordinator"/>. Só na T-Game.
/// </summary>
internal sealed class OfflineCardPresenter : IOfflineCardPresenter, IDisposable
{
    private readonly GameThread game;
    private readonly Typography typography;
    private readonly IconCatalog icons;
    private readonly OfflineCardController card;
    private readonly Func<bool> autoScaleOn;
    private readonly Action badgeChanged;
    private readonly SkiaFrameCanvas cardCanvas = new();
    private readonly SkiaFrameCanvas questionCanvas = new();

    private OfflineReturnCoordinator? coordinator;
    private OfflineCardWindow? cardWindow;
    private QuestionCardWindow? questionWindow;
    private OfflineCardRenderer? cardRenderer;
    private QuestionCardRenderer? questionRenderer;
    private int rendererDpi;

    private OfflineCardModel? cardModel;
    private OfflineCardLayout? cardLayout;
    private long cardModelVersion = -1;
    private OfflineCardHitKind cardPressed = OfflineCardHitKind.None;

    private OfflineQuestionSession? session;
    private int questionStage;
    private QuestionCardModel? questionModel;
    private QuestionCardLayout? questionLayout;
    private QuestionHit questionPressed;
    private bool disposed;

    public OfflineCardPresenter(GameThread game, Typography typography, IconCatalog icons, OfflineCardController card,
        Func<bool> autoScaleOn, Action badgeChanged)
    {
        this.game = game;
        this.typography = typography;
        this.icons = icons;
        this.card = card;
        this.autoScaleOn = autoScaleOn;
        this.badgeChanged = badgeChanged;
    }

    /// <summary>Liga o coordenador (criado depois, pois precisa do presenter).</summary>
    public void Attach(OfflineReturnCoordinator value) => coordinator = value;

    // ---------------------------------------------------------------- IOfflineCardPresenter

    public void ShowCard()
    {
        if (disposed || card.Pending is null || !TryPlace(OverlayKind.OfflineCard, 0, out var placement)) return;
        var window = EnsureCardWindow();
        window.Apply(placement);
        window.Show();
        window.Redraw();
    }

    public void HideCard() => cardWindow?.Hide();

    public void RefreshCard() => cardWindow?.Redraw();

    public void ShowQuestion(OfflineQuestionSession value, int stage)
    {
        if (disposed) return;
        session = value;
        questionStage = stage;
        RebuildQuestion();
        if (!TryPlace(OverlayKind.QuestionCard, questionLayout!.Height, out var placement)) return;
        var window = EnsureQuestionWindow();
        window.Apply(placement);
        window.Show();
        window.Redraw();
    }

    public void RefreshQuestion(OfflineQuestionSession value, int stage)
    {
        if (disposed || questionWindow is null) return;
        session = value;
        questionStage = stage;
        RebuildQuestion();
        if (TryPlace(OverlayKind.QuestionCard, questionLayout!.Height, out var placement) && placement.Bounds != questionWindow.Placement.Bounds)
            questionWindow.Apply(placement); // a caixa cresce para cima com a explicação (RF-24.9)
        questionWindow.Redraw();
    }

    public void HideQuestion()
    {
        session = null;
        questionModel = null;
        questionLayout = null;
        questionWindow?.Hide();
    }

    public void BadgeChanged() => badgeChanged();

    /// <summary>Novo monitor/DPI: reposiciona e redesenha o que estiver visível.</summary>
    public void Relayout()
    {
        if (disposed) return;
        if (cardWindow is { IsVisible: true } && TryPlace(OverlayKind.OfflineCard, 0, out var p)) { cardWindow.Apply(p); cardWindow.Redraw(); }
        if (questionWindow is { IsVisible: true } && session is not null)
        {
            RebuildQuestion();
            if (TryPlace(OverlayKind.QuestionCard, questionLayout!.Height, out var q)) { questionWindow.Apply(q); questionWindow.Redraw(); }
        }
    }

    // ---------------------------------------------------------------- janelas

    private OfflineCardWindow EnsureCardWindow()
    {
        if (cardWindow is { } existing) return existing;
        var window = game.Scene.GetOrCreateOfflineCard();
        window.Renderer = (frame, _) => DrawCard(frame);
        window.Interaction = () => coordinator?.NotifyInteraction();
        window.LeftDown = OnCardDown;
        window.LeftUp = OnCardUp;
        return cardWindow = window;
    }

    private QuestionCardWindow EnsureQuestionWindow()
    {
        if (questionWindow is { } existing) return existing;
        var window = game.Scene.GetOrCreateQuestionCard();
        window.Renderer = (frame, _) => DrawQuestion(frame);
        window.Interaction = () => coordinator?.NotifyInteraction();
        window.LeftDown = OnQuestionDown;
        window.LeftUp = OnQuestionUp;
        return questionWindow = window;
    }

    /// <summary>RF-24.2: centralizado sobre a faixa, 8 DIP acima do topo; a pergunta mantém a base e cresce para cima.</summary>
    private bool TryPlace(OverlayKind kind, float heightDip, out OverlayPlacement placement)
    {
        placement = default;
        if (game.Dock.Current is not { } current) return false;
        placement = PanelGeometry.Compute(kind, current.Monitor, current.Scene);
        if (kind == OverlayKind.QuestionCard && heightDip > 0)
        {
            double s = placement.Scale;
            int boxH = (int)Math.Round(heightDip * s, MidpointRounding.AwayFromZero);
            int bottom = placement.Bounds.Top + placement.Box.Height;
            int top = Math.Max(current.Monitor.WorkArea.Top, bottom - boxH);
            placement = placement with
            {
                Bounds = new PixelRect(placement.Bounds.Left, top, placement.Bounds.Right, bottom + placement.ShadowPx),
                Box = new PixelSize(placement.Box.Width, bottom - top),
            };
        }
        EnsureRenderers((int)current.Monitor.Dpi);
        return true;
    }

    private void EnsureRenderers(int dpi)
    {
        if (cardRenderer is not null && rendererDpi == dpi) return;
        cardRenderer?.Dispose();
        questionRenderer?.Dispose();
        cardRenderer = new OfflineCardRenderer(typography, icons, dpi);
        questionRenderer = new QuestionCardRenderer(typography, icons, dpi);
        rendererDpi = dpi;
        cardModelVersion = -1; // o layout em DIP não muda, mas o texto medido pode (escala fracionária)
    }

    // ---------------------------------------------------------------- desenho (re-render por controller.Version)

    private void DrawCard(SideWar.Runtime.Rendering.OverlayFrame frame)
    {
        if (cardRenderer is null || card.Pending is not { } pending) return;
        if (cardModel is null || cardLayout is null || cardModelVersion != card.Version)
        {
            cardModel = OfflineCardModel.From(pending, autoScaleOn(), version: card.Version);
            cardLayout = OfflineCardLayout.Compute(cardModel, new PanelTextMetricsSkia(typography));
            cardModelVersion = card.Version;
        }
        var canvas = cardCanvas.Begin(frame);
        cardRenderer.Draw(canvas, cardModel, cardLayout);
        cardCanvas.End();
    }

    private void RebuildQuestion()
    {
        questionModel = QuestionCardModel.FromOfflineSession(session!, questionStage);
        questionLayout = QuestionCardLayout.Compute(questionModel, new PanelTextMetricsSkia(typography));
    }

    private void DrawQuestion(SideWar.Runtime.Rendering.OverlayFrame frame)
    {
        if (questionRenderer is null || questionModel is null || questionLayout is null) return;
        var canvas = questionCanvas.Begin(frame);
        questionRenderer.Draw(canvas, questionModel, questionLayout);
        questionCanvas.End();
    }

    // ---------------------------------------------------------------- mouse (RF-24.11; só down/up, hover não chama)

    private OfflineCardHitKind CardHit(int x, int y)
    {
        if (cardLayout is null || cardWindow is null) return OfflineCardHitKind.None;
        float s = (float)cardWindow.Placement.Scale;
        return cardLayout.Hit(x / s, y / s, buttonsEnabled: card.State == OfflineCardState.Open);
    }

    private void OnCardDown(int x, int y)
    {
        cardPressed = CardHit(x, y);
        coordinator?.CardMouseDown();
    }

    private void OnCardUp(int x, int y)
    {
        var down = cardPressed;
        cardPressed = OfflineCardHitKind.None;
        var up = CardHit(x, y);
        if (down == up && up is OfflineCardHitKind.CollectX2 or OfflineCardHitKind.Collect) coordinator?.CardClick(up);
    }

    private QuestionHit QuestionHitAt(int x, int y)
    {
        if (questionLayout is null || questionModel is null || questionWindow is null) return default;
        float s = (float)questionWindow.Placement.Scale;
        return questionLayout.Hit(x / s, y / s, questionModel);
    }

    private void OnQuestionDown(int x, int y) => questionPressed = QuestionHitAt(x, y);

    private void OnQuestionUp(int x, int y)
    {
        var down = questionPressed;
        questionPressed = default;
        var up = QuestionHitAt(x, y);
        if (down != up) return;
        switch (up.Kind)
        {
            case QuestionHitKind.Option: coordinator?.QuestionAnswered(up.Index); break;
            // Minimizar e fechar encerram a pergunta sem resposta: vale ×1 (RF-25.9); resolvida, só fecha a janela.
            case QuestionHitKind.Minimize:
            case QuestionHitKind.Close: coordinator?.QuestionClosedByUser(); break;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cardRenderer?.Dispose();
        questionRenderer?.Dispose();
        cardCanvas.Dispose();
        questionCanvas.Dispose();
    }
}
