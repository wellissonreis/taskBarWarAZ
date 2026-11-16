using SideWar.Platform.Windows.Threading;

namespace SideWar.Platform.Windows.Windows;

/// <summary>
/// Janela de UI da partida TD (HUD e popover, TD.8): clicável (nunca WS_EX_TRANSPARENT), sem ativação, dona da cena. Entrega o mouse em
/// pixels do cliente à T-Game por <c>Post</c> (movimento, saída, botão esquerdo/direito soltos). O desenho fica por conta do host
/// (<see cref="OverlayChildWindow.Surface"/>).
/// </summary>
public sealed class TdUiWindow : OverlayChildWindow
{
    internal TdUiWindow(GameThread owner, SceneWindow scene, string className) : base(owner, scene, className, clickThrough: false) { }

    public event Action<int, int>? Moved;
    public event Action? Left;
    public event Action<int, int>? LeftUp;
    public event Action<int, int>? RightUp;

    private protected override void OnMouseMove(int x, int y) => Moved?.Invoke(x, y);
    private protected override void OnMouseLeave() => Left?.Invoke();
    private protected override void OnLeftButtonUp(int x, int y) { if (IsVisible) LeftUp?.Invoke(x, y); }
    private protected override void OnRightButtonUp(int x, int y) { if (IsVisible) RightUp?.Invoke(x, y); }
    private protected override bool UsesHandCursor => false;
}
