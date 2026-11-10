namespace SideWar.Core.Simulation;

/// <summary>
/// Layout 1D da faixa (SDD-11 RF-11.3, ADR-026): estado imutável da simulação, trocado inteiro por <c>LayoutChanged</c>.
/// Valores em DIP, quantizados por <see cref="Det.Quantize"/>. <c>SlotX[i]</c> = CenterXDip do slot <c>i</c> (SDD-21 RF-21.7).
/// </summary>
public sealed class StripLayout
{
    public const int MaxSlots = 12;
    public const float DefaultLeakX = 94f;

    private readonly float[] _slotX;

    private StripLayout(float spawnX, float leakX, float coreX, float portalX, float[] slotX)
    {
        SpawnX = spawnX; LeakX = leakX; CoreX = coreX; PortalX = portalX; _slotX = slotX;
    }

    public float SpawnX { get; }
    public float LeakX { get; }
    public float CoreX { get; }
    public float PortalX { get; }
    public int SlotCount => _slotX.Length;
    public float PathLength => SpawnX - LeakX;
    public ReadOnlySpan<float> SlotX => _slotX;

    public float SlotXAt(int slot) => _slotX[slot];
    public bool HasSlot(int slotIndex) => (uint)slotIndex < (uint)_slotX.Length;

    /// <summary>Valida e quantiza. Falha: <c>SpawnX ≤ LeakX</c>, mais de 12 slots ou valor não finito.</summary>
    public static bool TryCreate(float spawnX, float leakX, float coreX, float portalX,
        ReadOnlySpan<float> slotX, out StripLayout? layout, out string? error)
    {
        layout = null;
        if (slotX.Length > MaxSlots) { error = $"mais de {MaxSlots} slots"; return false; }
        if (!float.IsFinite(spawnX) || !float.IsFinite(leakX) || !float.IsFinite(coreX) || !float.IsFinite(portalX))
        { error = "valor não finito"; return false; }
        var slots = new float[slotX.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            if (!float.IsFinite(slotX[i])) { error = "slot não finito"; return false; }
            slots[i] = Det.Quantize(slotX[i]);
        }
        float s = Det.Quantize(spawnX), l = Det.Quantize(leakX);
        if (!(s > l)) { error = "SpawnX deve ser maior que LeakX"; return false; }
        layout = new StripLayout(s, l, Det.Quantize(coreX), Det.Quantize(portalX), slots);
        error = null;
        return true;
    }

    public static StripLayout Create(float spawnX, float leakX, float coreX, float portalX, ReadOnlySpan<float> slotX)
    {
        if (!TryCreate(spawnX, leakX, coreX, portalX, slotX, out var layout, out var error))
            throw new ArgumentException(error);
        return layout!;
    }
}
