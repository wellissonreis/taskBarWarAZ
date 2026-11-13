using SideWar.Core.Simulation;
using SideWar.Runtime.Localization;

namespace SideWar.Runtime.Panel;

public enum ArsenalRowKind : byte
{
    /// <summary>Torre posicionada: MELHORAR.</summary>
    Tower,
    /// <summary>Tipo desbloqueado sem torre posicionada: só o ícone arrastável (constrói a primeira).</summary>
    Available,
    /// <summary>Torre existente sem posição livre (SDD-21 RF-21.8, ADR-034): SEM POSIÇÃO + REPOSICIONAR.</summary>
    NoPosition,
    /// <summary>Tipo ainda bloqueado (Scale Set até a fase 09).</summary>
    Locked,
}

/// <summary>Estilo do botão da linha (RF-24.12): <c>ambar</c> com hover, ou bloqueado (<c>bg-trilho</c>, sem hover e sem clique).</summary>
public enum ArsenalButtonStyle : byte { Amber, Locked }

/// <summary>Linha do Arsenal já formatada (textos prontos; nada de Skia).</summary>
/// <param name="IconEnabled">O ícone pode iniciar o modo construção (desbloqueado, créditos e limite de 24 torres).</param>
/// <param name="HintTitle">Título da dica do fantasma (nome + custo de construção; só no ícone).</param>
public sealed record ArsenalRowModel(
    ArsenalRowKind Kind,
    TowerType Type,
    int TowerId,
    string Name,
    string LevelText,
    bool LevelIsSeal,
    string Azure,
    string Stats,
    string ButtonLabel,
    string CostText,
    ArsenalButtonStyle ButtonStyle,
    bool IconEnabled = false,
    string HintTitle = "")
{
    /// <summary>Opacidade da linha inteira: 0,45 (bloqueada e SEM POSIÇÃO) ou 1.</summary>
    public float Opacity => Kind is ArsenalRowKind.Locked or ArsenalRowKind.NoPosition ? 0.45f : 1f;
}

/// <summary>Modelo imutável da aba Arsenal. <see cref="SameDisplay"/> compara só o que é exibido (RF-24.13).</summary>
public sealed record ArsenalModel(bool AutoScaleOn, IReadOnlyList<ArsenalRowModel> Rows)
{
    public const string AutoScaleTitle = Strings.ArsenalAutoScaleTitle;
    public const string AutoScaleDescription = Strings.ArsenalAutoScaleDescription;
    public const string HelpText = Strings.ArsenalHelp;

    public bool SameDisplay(ArsenalModel? other)
    {
        if (other is null || AutoScaleOn != other.AutoScaleOn || Rows.Count != other.Rows.Count) return false;
        for (int i = 0; i < Rows.Count; i++)
            if (!Rows[i].Equals(other.Rows[i])) return false;
        return true;
    }
}
