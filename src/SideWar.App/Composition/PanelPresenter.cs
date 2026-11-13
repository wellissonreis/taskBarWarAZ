using SideWar.Platform.Windows.Threading;
using SideWar.Platform.Windows.Windows;
using SideWar.Rendering.Frame;
using SideWar.Rendering.Icons;
using SideWar.Rendering.Panel;
using SideWar.Rendering.Sprites;
using SideWar.Rendering.Text;
using SideWar.Runtime;
using SideWar.Runtime.Composition;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Input;
using SideWar.Runtime.Panel;
using SideWar.Runtime.Rendering;
using SideWar.Runtime.Widget;

namespace SideWar.App.Composition;

/// <summary>
/// Lado Windows do painel das 4 abas (SDD-24 RF-24.1 a RF-24.13; T6.11b): cria a <see cref="PanelWindow"/> sob demanda (sem
/// foco), posiciona com <see cref="PanelGeometry"/> sobre a faixa, desenha com o cromo único (título, X, filete e barra de
/// rolagem) do <see cref="MissionsMigratePanelRenderer"/> — Arsenal e Campanha agora também ganham esse cromo — e repassa o mouse do
/// corpo em DIP ao <see cref="PanelCoordinator"/>. A janela cuida da rolagem, do hover do cromo e do X. Só na T-Game.
/// </summary>
internal sealed class PanelPresenter : IPanelPresenter, IDisposable
{
    private readonly GameThread game;
    private readonly GameRuntime runtime;
    private readonly Typography typography;
    private readonly IconCatalog icons;
    private readonly Func<ScaledSpriteCache> sprites;
    private readonly SkiaFrameCanvas canvas = new();
    private PanelCoordinator? coordinator;
    private PanelWindow? window;
    private MissionsMigratePanelRenderer? renderer;
    private int rendererDpi;
    private bool disposed;

    public PanelPresenter(GameThread game, GameRuntime runtime, Typography typography, IconCatalog icons, Func<ScaledSpriteCache> sprites)
    {
        this.game = game;
        this.runtime = runtime;
        this.typography = typography;
        this.icons = icons;
        this.sprites = sprites;
    }

    public void Attach(PanelCoordinator value) => coordinator = value;

    public PanelViewState View => Ensure().View;

    public void Show()
    {
        if (disposed || !OverlayPlacements.TryPlace(game, OverlayKind.Panel, 0, out var placement)) return;
        EnsureRenderer((int)game.Dock.Current!.Monitor.Dpi);
        var w = Ensure();
        w.Apply(placement); // também ajusta o viewport (monitor baixo)
        w.Show();
        w.Redraw();
    }

    public void Hide() => window?.Hide();

    public void Redraw() => window?.Redraw();

    /// <summary>O ícone/REPOSICIONAR iniciou o modo construção: a captura real e o overlay na cena são da TA.8 (sem janela nova aqui).</summary>
    public void BuildModeChanged(BuildOutcome outcome) { }

    /// <summary>Novo monitor/DPI: reposiciona, remonta o layout (medidas) e redesenha se visível.</summary>
    public void Relayout()
    {
        if (disposed || window is not { IsVisible: true }) return;
        if (!OverlayPlacements.TryPlace(game, OverlayKind.Panel, 0, out var placement)) return;
        EnsureRenderer((int)game.Dock.Current!.Monitor.Dpi);
        window.Apply(placement);
        coordinator?.Rebuild(force: true);
        window.Redraw();
    }

    private PanelWindow Ensure()
    {
        if (window is { } existing) return existing;
        var w = game.Scene.GetOrCreatePanel();
        w.Renderer = (frame, context) => Draw(frame, context);
        w.Interaction = () => runtime.NotifyInteraction(); // RF-25.1: todo clique no painel é presença
        w.Clicked = hit => { if (hit == PanelHit.Close) coordinator?.CloseClicked(); };
        w.LeftDown = (x, y) => Pointer(x, y, down: true);
        w.LeftUp = (x, y) => Pointer(x, y, down: false);
        return window = w;
    }

    private void EnsureRenderer(int dpi)
    {
        if (renderer is not null && rendererDpi == dpi) return;
        renderer?.Dispose();
        renderer = new MissionsMigratePanelRenderer(typography, icons, dpi);
        rendererDpi = dpi;
    }

    private void Pointer(int xPx, int yPx, bool down)
    {
        if (coordinator is null || window is null) return;
        float s = (float)window.Placement.Scale;
        var bounds = window.Placement.Bounds;
        // Posição de tela do ponteiro e X em DIP da faixa (a captura real do arraste é da TA.8; aqui só a origem/limiar).
        int screenX = bounds.Left + xPx, screenY = bounds.Top + yPx;
        var current = game.Dock.Current;
        float stripX = current is null ? 0f : (screenX - current.Scene.Left) / s;
        bool over = current is not null && screenX >= current.Scene.Left && screenX < current.Scene.Right
            && screenY >= current.Scene.Top && screenY < current.Scene.Bottom;
        var cursor = new BuildPointer(screenX, screenY, stripX, over);
        if (down) coordinator.PointerDown(xPx / s, yPx / s, cursor);
        else coordinator.PointerUp(xPx / s, yPx / s, cursor);
    }

    private void Draw(OverlayFrame frame, OverlayRenderContext context)
    {
        if (renderer is null || coordinator is not { ActiveTab: { } tab } c) return;
        var surface = canvas.Begin(frame);
        var paint = new ArsenalCampaignPaintContext(typography, sprites(), rendererDpi);
        var view = context.View;
        switch (tab)
        {
            case WidgetTab.Arsenal when c.Arsenal is { } arsenal:
                renderer.DrawArsenal(surface, arsenal, default, view, paint, c.Building);
                break;
            case WidgetTab.Fases when c.Campaign is { } campaign:
                renderer.DrawCampaign(surface, campaign, view, paint);
                break;
            case WidgetTab.Missoes when c.MissionsLayout is { } missions:
                renderer.DrawMissions(surface, c.MissionsModel!, missions, view, TabBodyHit.None);
                break;
            case WidgetTab.Migrar when c.Migrate is { } migrate:
                renderer.DrawMigrate(surface, c.MigrateModel!, migrate, view, TabBodyHit.None);
                break;
        }
        canvas.End();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        renderer?.Dispose();
        canvas.Dispose();
    }
}
