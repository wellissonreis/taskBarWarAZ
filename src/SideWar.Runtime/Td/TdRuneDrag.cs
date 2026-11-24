namespace SideWar.Runtime.Td;

/// <summary>Separates a click from a web drag in physical pixels.</summary>
public sealed class TdRuneDrag
{
    private float startX, startY, lastX, lastY;
    public bool Active { get; private set; }
    public bool Dragged { get; private set; }
    public void Begin(float x, float y) { startX = lastX = x; startY = lastY = y; Active = true; Dragged = false; }
    public (float X, float Y) Move(float x, float y)
    {
        if (!Active) return (0, 0);
        if (!Dragged && (x - startX) * (x - startX) + (y - startY) * (y - startY) >= 16)
        {
            Dragged = true;
            lastX = startX; lastY = startY;
        }
        float dx = Dragged ? x - lastX : 0, dy = Dragged ? y - lastY : 0;
        if (Dragged) { lastX = x; lastY = y; }
        return (dx, dy);
    }
    public bool End() { bool dragged = Active && Dragged; Active = false; Dragged = false; return dragged; }
}
