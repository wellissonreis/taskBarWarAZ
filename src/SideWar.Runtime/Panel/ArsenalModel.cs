using SideWar.Core.Simulation;

namespace SideWar.Runtime.Panel;

public enum ArsenalRowKind : byte
{
    /// <summary>Torre existente com slot: MELHORAR.</summary>
    Tower,
    /// <summary>Torre existente com <c>slotIndex ≥ contagemDeSlots</c> (SDD-21 RF-21.8): SEM SLOT.</summary>
    NoSlot,
    /// <summary>Tipo ainda bloqueado (Scale Set até a fase 09).</summary>
    Locked,
    /// <summary>Linha "construir em slot vazio" (RF-24.5 §5.4): CONSTRUIR.</summary>
    Build,
}

/// <summary>Estilo do botão da linha (RF-24.12): <c>ambar</c> com hover, ou bloqueado (<c>bg-trilho</c>, sem hover e sem clique).</summary>
public enum ArsenalButtonStyle : byte { Amber, Locked }

/// <summary>Linha do Arsenal já formatada (textos prontos; nada de Skia).</summary>
public sealed record ArsenalRowModel(
    ArsenalRowKind Kind,
    TowerType Type,
    int TowerId,
    int Slot,
    string Name,
    string LevelText,
    bool LevelIsSeal,
    string Azure,
    string Stats,
    string ButtonLabel,
    string CostText,
    ArsenalButtonStyle ButtonStyle)
{
    /// <summary>Opacidade da linha inteira: 0,45 (bloqueada e SEM SLOT) ou 1.</summary>
    public float Opacity => Kind is ArsenalRowKind.Locked or ArsenalRowKind.NoSlot ? 0.45f : 1f;
}

/// <summary>Modelo imutável da aba Arsenal. <see cref="SameDisplay"/> compara só o que é exibido (RF-24.13).</summary>
public sealed record ArsenalModel(bool AutoScaleOn, int? BuildSlot, IReadOnlyList<ArsenalRowModel> Rows)
{
    public const string AutoScaleTitle = "AUTO-ESCALA (AUSENTE)";
    public const string AutoScaleDescription = "Enquanto você trabalha em outra janela, usa 50% dos créditos para melhorar a torre mais barata a cada 15 min.";

    /// <summary>Rótulo "CONSTRUIR NO SLOT n" (slot de 1 a N para o usuário), ou nulo sem foco em slot vazio.</summary>
    public string? BuildLabel => BuildSlot is { } s ? $"CONSTRUIR NO SLOT {s + 1}" : null;

    public bool SameDisplay(ArsenalModel? other)
    {
        if (other is null || AutoScaleOn != other.AutoScaleOn || BuildSlot != other.BuildSlot || Rows.Count != other.Rows.Count) return false;
        for (int i = 0; i < Rows.Count; i++)
            if (!Rows[i].Equals(other.Rows[i])) return false;
        return true;
    }
}
