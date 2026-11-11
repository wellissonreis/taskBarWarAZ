using SideWar.Runtime.Widget;

namespace SideWar.Platform.Windows.Windows;

/// <summary>
/// Liga o desenho do widget (<see cref="IWidgetPainter"/>/<see cref="WidgetModel"/>, T5.1) à <see cref="WidgetWindow"/> (T1.9):
/// desenha por evento (só quando a versão do modelo, o hover ou a janela mudam), faz hit-test/cursores via
/// <see cref="WidgetInteraction"/> e abre o menu de clique direito (SDD-23 RF-23.8, RF-23.9). Só na T-Game.
/// </summary>
public sealed class WidgetController : IDisposable
{
    private readonly WidgetWindow window;
    private readonly IWidgetPainter painter;
    private readonly IWidgetMenu menu;
    private readonly Func<WidgetMenuState> menuState;
    private readonly Action<WidgetMenuResult> onMenu;
    private WidgetModel external = WidgetModel.Empty;
    private long drawnVersion = -1;
    private int drawnHover = -2;
    private bool invalid = true;
    private bool disposed;

    public WidgetController(WidgetWindow window, IWidgetPainter painter, IPresenceSink presence,
        Action<PanelCommand> panel, IWidgetMenu menu, Func<WidgetMenuState> menuState, Action<WidgetMenuResult> onMenu,
        int dpi = 96)
    {
        this.window = window;
        this.painter = painter;
        this.menu = menu;
        this.menuState = menuState;
        this.onMenu = onMenu;
        Interaction = new WidgetInteraction(presence, panel);
        Dpi = dpi;
        window.Surface.Scale = dpi / 96f;
        window.Interaction = Interaction;
        window.HoverChanged += OnHoverChanged;
        window.ContextMenuRequested += OnContextMenu;
        window.Shown += OnShown;
        window.Hidden += OnHidden;
        window.Resized += OnResized;
    }

    public WidgetInteraction Interaction { get; }
    public int Dpi { get; private set; }

    /// <summary>Quadros apresentados pelo controlador (CA-23.6: 0 sem mudança, ≤ 2/s com créditos subindo).</summary>
    public long PresentCount { get; private set; }

    /// <summary>Define o modelo do host (o hover é do controlador); redesenha só se algo exibido mudou.</summary>
    public void SetModel(in WidgetModel model)
    {
        external = model;
        Redraw();
    }

    /// <summary>Novo DPI (WM_DPICHANGED fica com a integração): ajusta a escala e redesenha tudo.</summary>
    public void SetDpi(int dpi)
    {
        Dpi = dpi;
        window.Surface.Scale = dpi / 96f;
        invalid = true;
        Redraw();
    }

    private void OnHoverChanged() => Redraw();
    private void OnShown() { invalid = true; Redraw(); }
    private void OnHidden() { painter.Invalidate(); invalid = true; }
    private void OnResized() { painter.Invalidate(); invalid = true; Redraw(); }

    private void OnContextMenu(int x, int y)
    {
        var result = menu.Show(window.Handle, x, y, menuState());
        if (result is { } r) onMenu(r);
    }

    private void Redraw()
    {
        if (disposed || !window.IsVisible || window.Bounds.Width == 0) return;
        int hover = Interaction.HoverIndex;
        if (!invalid && external.Version == drawnVersion && hover == drawnHover) return;
        float scale = Dpi / 96f;
        float heightDip = window.Bounds.Height / scale;
        WidgetModel model = external with { HoverIndex = hover };
        var surface = window.Surface;
        var frame = surface.BeginFrame();
        try
        {
            Interaction.Layout = painter.Paint(frame, model, heightDip, Dpi);
        }
        finally
        {
            surface.EndFrame(null);
        }
        drawnVersion = external.Version;
        drawnHover = hover;
        invalid = false;
        PresentCount++;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        window.HoverChanged -= OnHoverChanged;
        window.ContextMenuRequested -= OnContextMenu;
        window.Shown -= OnShown;
        window.Hidden -= OnHidden;
        window.Resized -= OnResized;
        window.Interaction = null;
        painter.Invalidate();
    }
}
