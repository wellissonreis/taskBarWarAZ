namespace SideWar.Runtime.Widget;

/// <summary>
/// Porta de presença (SDD-25 RF-25.1): as janelas do jogo chamam <see cref="NotifyInteraction"/> uma vez por
/// pressionamento de botão do mouse. Contrato de SDD-25; declarada aqui até a integração (T1.13) unificar.
/// </summary>
public interface IPresenceSink
{
    void NotifyInteraction();
}

public enum PanelCommandKind { ToggleTab, OpenArsenal }

/// <summary>Comando do widget ao painel (SDD-23 RF-23.8; consumidor: SDD-24).</summary>
public readonly record struct PanelCommand(PanelCommandKind Kind, WidgetTab Tab, int TowerId = 0)
{
    public static PanelCommand ToggleTab(WidgetTab tab) => new(PanelCommandKind.ToggleTab, tab);

    /// <summary>ADR-034: clique numa torre da cena abre o Arsenal e rola até a linha dela (uma vez, ao abrir; <c>ArsenalLayout.TryGetTowerRowTop</c>).</summary>
    public static PanelCommand OpenArsenal(int towerId) => new(PanelCommandKind.OpenArsenal, WidgetTab.Arsenal, towerId);
}

/// <summary>Alvo do hit-test do widget (RF-23.8).</summary>
public enum WidgetHitTarget { None, Background, Button, Badge }

/// <summary>Forma do widget: retângulo sem raio (ADR-034): todo pixel tem alfa ≥ 1 e consome o clique (RF-23.3, CA-23.9).</summary>
public static class WidgetShape
{
    public static bool Contains(float x, float y, float width, float height) =>
        x >= 0 && y >= 0 && x < width && y < height;
}

/// <summary>
/// Lógica pura de mouse do widget (SDD-23 RF-23.8): hit-test em DIP, hover, clique no mesmo botão,
/// <see cref="IPresenceSink.NotifyInteraction"/> uma vez por pressionamento. Coordenadas em DIP, origem na
/// janela. Sem Win32; chamado só na T-Game, sem alocação por evento.
/// </summary>
public sealed class WidgetInteraction
{
    private readonly IPresenceSink _presence;
    private readonly Action<PanelCommand> _panel;
    private int _pressed = -1;
    private bool _badgePressed;

    public WidgetInteraction(IPresenceSink presence, Action<PanelCommand> panel)
    {
        _presence = presence;
        _panel = panel;
    }

    /// <summary>Layout vigente em DIP (atualizado a cada render).</summary>
    public WidgetLayout Layout { get; set; } = WidgetLayout.Compute(48, 0, 0, 0);

    /// <summary>Botão sob o cursor, ou -1.</summary>
    public int HoverIndex { get; private set; } = -1;

    /// <summary>Botão pressionado (captura ativa), ou -1.</summary>
    public int PressedIndex => _pressed;

    /// <summary>
    /// Há ganhos pendentes do card "Bem-vindo de volta" recolhido: o badge "+X" aparece e é clicável (SDD-23 RF-23.7;
    /// <c>WidgetModel.PendingText != null</c>). O host mantém em sincronia com o modelo.
    /// </summary>
    public bool BadgeVisible { get; set; }

    /// <summary>Clique no badge: o host reabre o card (<c>OfflineCardController.ReopenFromBadge</c>), sem ativar a janela.</summary>
    public Action? OnBadgeClick { get; set; }

    public WidgetHitTarget Hit(float x, float y)
    {
        if (!WidgetShape.Contains(x, y, Layout.WidthDip, Layout.HeightDip)) return WidgetHitTarget.None;
        if (BadgeVisible && WidgetBadgeGeometry.Rect(Layout.Credits, Layout.HeightDip).Contains(x, y)) return WidgetHitTarget.Badge;
        return Layout.HitButton(x, y) >= 0 ? WidgetHitTarget.Button : WidgetHitTarget.Background;
    }

    /// <summary>WM_LBUTTONDOWN. Devolve true se a janela deve capturar o mouse (só sobre botão do nav).</summary>
    public bool OnLeftDown(float x, float y)
    {
        if (Hit(x, y) == WidgetHitTarget.None) return false;
        _presence.NotifyInteraction();
        _badgePressed = Hit(x, y) == WidgetHitTarget.Badge;
        _pressed = Layout.HitButton(x, y);
        return _pressed >= 0 || _badgePressed;
    }

    /// <summary>WM_LBUTTONUP. Emite ToggleTab se solto no mesmo botão do pressionamento. Devolve true se havia captura.</summary>
    public bool OnLeftUp(float x, float y)
    {
        int pressed = _pressed;
        _pressed = -1;
        if (_badgePressed)
        {
            _badgePressed = false;
            if (Hit(x, y) == WidgetHitTarget.Badge) OnBadgeClick?.Invoke();
            return true;
        }
        if (pressed < 0) return false;
        if (Hit(x, y) == WidgetHitTarget.Button && Layout.HitButton(x, y) == pressed)
            _panel(PanelCommand.ToggleTab((WidgetTab)pressed));
        return true;
    }

    /// <summary>WM_RBUTTONDOWN. Conta interação; devolve true se o menu de contexto deve abrir (dentro do widget).</summary>
    public bool OnRightDown(float x, float y)
    {
        if (Hit(x, y) == WidgetHitTarget.None) return false;
        _presence.NotifyInteraction();
        return true;
    }

    /// <summary>WM_MOUSEMOVE. Devolve true se o hover mudou (o host redesenha). Movimento não conta como interação.</summary>
    public bool OnMouseMove(float x, float y)
    {
        int hover = Hit(x, y) == WidgetHitTarget.None ? -1 : Layout.HitButton(x, y);
        if (hover == HoverIndex) return false;
        HoverIndex = hover;
        return true;
    }

    /// <summary>WM_MOUSELEAVE. Devolve true se o hover mudou.</summary>
    public bool OnLeave()
    {
        if (HoverIndex < 0) return false;
        HoverIndex = -1;
        return true;
    }

    /// <summary>WM_CAPTURECHANGED: cancela o clique em andamento.</summary>
    public void OnCaptureLost() { _pressed = -1; _badgePressed = false; }
}
