namespace SideWar.Runtime.Widget;

/// <summary>Abas do painel acionadas pelos 4 botões do nav (SDD-23 RF-23.7), na ordem de desenho.</summary>
public enum WidgetTab { Arsenal = 0, Fases = 1, Missoes = 2, Migrar = 3 }

/// <summary>
/// Modelo imutável do widget (SDD-23 RF-23.9). Só textos já formatados e estados; <see cref="Version"/> sobe
/// quando algum campo exibido muda (comparação do texto formatado).
/// </summary>
public readonly record struct WidgetModel(
    long Version,
    string CreditsText,
    string RateText,
    string WaveText,
    bool IsPresent,
    string MultiplierText,
    string? BonusChipText,
    string? PendingText,
    int MissionBadge,
    WidgetTab? ActiveTab,
    int HoverIndex)
{
    public const int NavButtonCount = 4;

    /// <summary>Texto desenhado no selo: o rótulo completo do canvas, "PRESENTE ×2,0"/"AUSENTE ×0,25" (ADR-034; resolve D-16).</summary>
    public string SealText => PresenceAccessibleName;

    /// <summary>Rótulo completo do selo, também usado no nome acessível (UIA futuro).</summary>
    public string PresenceAccessibleName => (IsPresent ? "PRESENTE " : "AUSENTE ") + MultiplierText;

    /// <summary>Nome acessível do selo/chip: presença e, com pico ativo, o bônus (o chip ×3 fica depois do selo).</summary>
    public string AccessibleName => BonusChipText is { } chip ? PresenceAccessibleName + ", bônus " + chip : PresenceAccessibleName;

    /// <summary>Texto do badge de Missões: vazio com contagem 0; "9+" acima de 9 (DP-23.5).</summary>
    public string BadgeText => MissionBadge <= 0 ? "" : MissionBadge > 9 ? "9+" : MissionBadge.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static WidgetModel Empty { get; } = new(0, "0", "+0/s", "0/0", false, "×0,25", null, null, 0, null, -1);
}
