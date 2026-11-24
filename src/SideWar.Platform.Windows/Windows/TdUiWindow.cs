using SideWar.Platform.Windows.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace SideWar.Platform.Windows.Windows;

/// <summary>
/// Janela de UI da partida TD (HUD e popover, TD.8): clicável (nunca WS_EX_TRANSPARENT), sem ativação, dona da cena. Entrega o mouse em
/// pixels do cliente à T-Game por <c>Post</c> (movimento, saída, botão esquerdo/direito soltos). O desenho fica por conta do host
/// (<see cref="OverlayChildWindow.Surface"/>).
/// </summary>
public sealed class TdUiWindow : OverlayChildWindow
{
    internal TdUiWindow(GameThread owner, SceneWindow scene, string className) : base(owner, scene, className, clickThrough: false) { }

    private protected override bool Activatable => classIsModal;
    private bool classIsModal;
    private nint previousFocus;
    public event Action<int>? KeyDown;
    private protected override void OnKeyDown(int key) => KeyDown?.Invoke(key);
    public void ActivateModal()
    {
        classIsModal = true;
        previousFocus = NativeFocus.Foreground();
        NativeFocus.AllowActivation(Handle);
        NativeFocus.Activate(Handle);
    }
    /// <summary>Volta a janela ao estado sem ativação (cartão no cantinho: clicar nunca rouba o foco).</summary>
    public void DeactivateModal()
    {
        classIsModal = false;
        NativeFocus.BlockActivation(Handle);
    }
    public void ForgetPreviousFocus() => previousFocus = 0;
    public static nint ForegroundWindow => NativeFocus.Foreground();
    public void RestoreFocus()
    {
        if (previousFocus != 0) NativeFocus.Activate(previousFocus);
        previousFocus = 0;
    }

    /// <summary>TD.29b: quando definido, só os pixels em que devolve true recebem clique (botões); o resto é HTTRANSPARENT. Roda na T-Game.</summary>
    public Func<int, int, bool>? ClickableAt { get; set; }
    private protected override bool HitTransparentAt(int clientX, int clientY) => ClickableAt is { } f && !f(clientX, clientY);

    /// <summary>
    /// Arrasto da janela pelo cabeçalho (modal): quando <see cref="DragHandleAt"/> aceita o ponto do botão esquerdo, a janela captura o mouse
    /// (SetCapture, sem foco) e reporta o deslocamento do cursor em pixels de tela desde o início (<see cref="DragMoved"/>). Roda na T-Game.
    /// </summary>
    public Func<int, int, bool>? DragHandleAt { get; set; }
    public Func<int, int, bool>? CapturePointerAt { get; set; }
    public event Action<int, int>? LeftDown;
    public event Action? PointerCaptureLost;
    private bool pointerCaptured;
    public event Action<int, int>? DragMoved;
    public event Action? DragEnded;
    private bool dragging;
    private int dragStartX, dragStartY;
    /// <summary>True se o último botão esquerdo virou arrasto de janela (o presenter ignora o clique ao soltar).</summary>
    public bool Dragging => dragging;
    private protected override void OnLeftButtonDown(int x, int y)
    {
        if (!IsVisible) return;
        if (DragHandleAt is { } test && test(x, y) && PInvoke.GetCursorPos(out var p))
        {
            dragging = true; dragStartX = p.X; dragStartY = p.Y;
            PInvoke.SetCapture(new HWND(Handle));
        }
        else if (CapturePointerAt is { } capture && capture(x, y))
        {
            pointerCaptured = true;
            PInvoke.SetCapture(new HWND(Handle));
            LeftDown?.Invoke(x, y);
        }
    }
    private void EndDrag()
    {
        if (!dragging) return;
        dragging = false;
        if (PInvoke.GetCapture() == new HWND(Handle)) PInvoke.ReleaseCapture();
        DragEnded?.Invoke();
    }
    private protected override void OnCaptureLost()
    {
        EndDrag();
        if (pointerCaptured) { pointerCaptured = false; PointerCaptureLost?.Invoke(); }
    }

    public event Action<int, int>? Moved;
    public event Action? Left;
    public event Action<int, int>? LeftUp;
    public event Action<int, int>? RightUp;
    public event Action<int, int, int>? Wheel;
    private protected override void OnMouseWheel(int delta)
    {
        if (IsVisible && PInvoke.GetCursorPos(out var p)) Wheel?.Invoke(delta, p.X - Bounds.Left, p.Y - Bounds.Top);
    }

    /// <summary>Botão esquerdo pressionado no último movimento do mouse (arrastar a teia de runas).</summary>
    public bool LeftHeld => MouseLeftHeld;
    private protected override void OnMouseMove(int x, int y)
    {
        if (dragging)
        {
            if (!MouseLeftHeld) { EndDrag(); return; }
            if (PInvoke.GetCursorPos(out var p)) DragMoved?.Invoke(p.X - dragStartX, p.Y - dragStartY);
            return;
        }
        Moved?.Invoke(x, y);
    }
    private protected override void OnMouseLeave() => Left?.Invoke();
    private protected override void OnLeftButtonUp(int x, int y)
    {
        if (dragging) { EndDrag(); return; }
        if (IsVisible) LeftUp?.Invoke(x, y);
        if (pointerCaptured)
        {
            pointerCaptured = false;
            if (PInvoke.GetCapture() == new HWND(Handle)) PInvoke.ReleaseCapture();
        }
    }
    private protected override void OnRightButtonUp(int x, int y) { if (IsVisible) RightUp?.Invoke(x, y); }
    private protected override bool UsesHandCursor => false;
}


internal static class NativeFocus
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint window, int index, int value);
    internal static nint Foreground() => GetForegroundWindow();
    internal static void AllowActivation(nint window) => _ = SetWindowLong(window, -20, GetWindowLong(window, -20) & ~0x08000000);
    internal static void BlockActivation(nint window) => _ = SetWindowLong(window, -20, GetWindowLong(window, -20) | 0x08000000);
    internal static void Activate(nint window) => _ = SetForegroundWindow(window);
}
