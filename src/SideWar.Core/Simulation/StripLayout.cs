namespace SideWar.Core.Simulation;

/// <summary>Hospedeiro das torres (SDD-11 RF-11.3, SDD-21 RF-21.7): dentro da barra ou na faixa de fallback no topo da cena.</summary>
public enum TowerHost : byte { Taskbar = 0, Band = 1 }

/// <summary>Zona livre de construção <c>[X0, X1)</c> em DIP inteiros (origem da faixa; SDD-21 RF-21.7).</summary>
public readonly record struct FreeZone(int X0, int X1)
{
    public int Width => X1 - X0;
}

/// <summary>
/// Layout 1D da faixa (SDD-11 RF-11.3, ADR-026, ADR-034): estado imutável da simulação, trocado inteiro por <c>LayoutChanged</c>.
/// <c>Width</c> e <c>FreeZones</c> são DIP inteiros (determinismo); <c>SpawnX/LeakX/CoreX/PortalX</c> são quantizados por <see cref="Det.Quantize"/>.
/// </summary>
public sealed class StripLayout
{
    public const int MaxFreeZones = 8;
    public const float DefaultLeakX = 88f;

    private readonly FreeZone[] _zones;

    private StripLayout(int width, float spawnX, float leakX, float coreX, float portalX, TowerHost host, FreeZone[] zones)
    {
        Width = width; SpawnX = spawnX; LeakX = leakX; CoreX = coreX; PortalX = portalX; Host = host; _zones = zones;
    }

    public int Width { get; }
    public float SpawnX { get; }
    public float LeakX { get; }
    public float CoreX { get; }
    public float PortalX { get; }
    public TowerHost Host { get; }
    public int FreeZoneCount => _zones.Length;
    public float PathLength => SpawnX - LeakX;
    public ReadOnlySpan<FreeZone> FreeZones => _zones;

    public FreeZone ZoneAt(int index) => _zones[index];

    /// <summary>
    /// Valida e quantiza. Falha: <c>Width ≤ 0</c>, <c>SpawnX ≤ LeakX</c>, valor não finito, host indefinido, mais de 8 zonas,
    /// zona vazia ou fora de <c>[0, Width]</c>, zonas fora de ordem ou sobrepostas (podem se tocar).
    /// </summary>
    public static bool TryCreate(int width, float spawnX, float leakX, float coreX, float portalX, TowerHost host,
        ReadOnlySpan<FreeZone> freeZones, out StripLayout? layout, out string? error)
    {
        layout = null;
        if (width <= 0) { error = "Width deve ser positivo"; return false; }
        if (!Enum.IsDefined(host)) { error = "host inválido"; return false; }
        if (freeZones.Length > MaxFreeZones) { error = $"mais de {MaxFreeZones} zonas livres"; return false; }
        if (!float.IsFinite(spawnX) || !float.IsFinite(leakX) || !float.IsFinite(coreX) || !float.IsFinite(portalX))
        { error = "valor não finito"; return false; }
        var zones = freeZones.ToArray();
        for (int i = 0; i < zones.Length; i++)
        {
            var z = zones[i];
            if (z.X0 >= z.X1) { error = "zona vazia ou invertida"; return false; }
            if (z.X0 < 0 || z.X1 > width) { error = "zona fora de [0, Width]"; return false; }
            if (i > 0 && z.X0 < zones[i - 1].X1) { error = "zonas fora de ordem ou sobrepostas"; return false; }
        }
        float s = Det.Quantize(spawnX), l = Det.Quantize(leakX);
        if (!(s > l)) { error = "SpawnX deve ser maior que LeakX"; return false; }
        layout = new StripLayout(width, s, l, Det.Quantize(coreX), Det.Quantize(portalX), host, zones);
        error = null;
        return true;
    }

    public static StripLayout Create(int width, float spawnX, float leakX, float coreX, float portalX, TowerHost host,
        ReadOnlySpan<FreeZone> freeZones)
    {
        if (!TryCreate(width, spawnX, leakX, coreX, portalX, host, freeZones, out var layout, out var error))
            throw new ArgumentException(error);
        return layout!;
    }
}
