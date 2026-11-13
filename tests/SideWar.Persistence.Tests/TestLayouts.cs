using SideWar.Core.Simulation;

namespace SideWar.Persistence.Tests;

/// <summary>
/// Layouts de teste do modelo de posição livre (ADR-034). <see cref="FromCenters"/> converte os antigos "centros de slot"
/// em zonas livres de 44 DIP (as separadas por vão < 44 DIP são fundidas); <see cref="X"/> devolve o centro do slot padrão (200 + 60·i).
/// </summary>
internal static class TestLayouts
{
    public static int X(int slot) => 200 + 60 * slot;

    public static StripLayout Default() => FromCenters(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 380f });

    public static StripLayout FromCenters(float spawn, float leak, float core, float portal, ReadOnlySpan<float> centers)
    {
        var zones = new List<FreeZone>();
        foreach (float c in centers)
        {
            int x = (int)MathF.Round(c, MidpointRounding.ToEven);
            var z = new FreeZone(x - 22, x + 22);
            if (zones.Count > 0 && z.X0 - zones[^1].X1 < PlacementValidator.BoxWidth) zones[^1] = new FreeZone(zones[^1].X0, z.X1);
            else zones.Add(z);
        }
        int width = (int)MathF.Ceiling(spawn);
        if (zones.Count > 0) width = Math.Max(width, zones[^1].X1);
        return StripLayout.Create(width, spawn, leak, core, portal, TowerHost.Taskbar, zones.ToArray());
    }

    /// <summary>Handle da torre cujo centro X é o do slot padrão <paramref name="slot"/> (ou None).</summary>
    public static Handle TowerAt(SimState s, int slot)
    {
        for (int i = 0; i < s.Towers.Capacity; i++)
            if (s.Towers.IsAlive(i) && s.TowerCenterX(in s.Towers.At(i)) == X(slot)) return s.Towers.HandleAt(i);
        return Handle.None;
    }
}
