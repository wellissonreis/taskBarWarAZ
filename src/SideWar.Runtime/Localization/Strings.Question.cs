namespace SideWar.Runtime.Localization;

/// <summary>Cabeçalhos dos cards de pergunta (SDD-24 RF-24.9, SDD-26 RF-26.16). Parcial de <see cref="Strings"/>.</summary>
public static partial class Strings
{
    /// <summary>Cabeçalho do card de pergunta do chefe: <c>CHEFE · ONDA 40 · IAAS</c> (RF-24.9).</summary>
    public static string BossQuestionHeader(int wave, string topic) => $"CHEFE · ONDA {wave} · {topic.ToUpperInvariant()}";

    /// <summary>Cabeçalho do card de pergunta do COLETAR ×2: <c>COLETAR ×2 · FASE 07</c> (mesma tipografia do chefe; T6.4b).</summary>
    public static string CollectQuestionHeader(int stage) => $"COLETAR ×2 · FASE {stage:00}";
}
