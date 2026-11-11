namespace SideWar.Runtime.Panel;

/// <summary>Faixa do bônus de presença (SDD-24 RF-24.7): rótulo (<c>0–15 min</c>), multiplicador (<c>×1,5</c>) e destaque.</summary>
public sealed record PresenceBandView(string Range, string Multiplier, bool Current);

/// <summary>Linha de missão: progresso em 0..1, texto <c>{progresso} · {recompensa}</c>, concluída/coletada.</summary>
public sealed record MissionRowView(string Id, string Title, float Progress, string ProgressText, bool Completed, bool Collected)
{
    /// <summary>COLETAR só em missão concluída e não coletada (RF-24.7).</summary>
    public bool CanCollect => Completed && !Collected;
}

/// <summary>
/// Modelo imutável da aba Missões (SDD-24 RF-24.7, RF-24.13). Textos já formatados; a versão sobe só quando
/// um texto ou estado exibido muda. Os serviços reais (T6.7) e a ligação (T6.11) preenchem este modelo.
/// </summary>
public sealed record MissionsTabModel(
    string Title,
    string Multiplier,
    bool MultiplierMuted,
    string PresenceText,
    IReadOnlyList<PresenceBandView> Bands,
    string SectionLabel,
    IReadOnlyList<MissionRowView> Missions,
    long Version = 0)
{
    public const string TabTitle = "MISSÕES & PRESENÇA";
    public const string SectionText = "MISSÕES DE HOJE";
    public const string BonusTitle = "BÔNUS DE PRESENÇA";

    /// <summary>Texto do multiplicador, vírgula decimal (ADR-029): 1,5 → <c>×1,5</c>.</summary>
    public static string FormatMultiplier(double value) =>
        "×" + value.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>Faixas padrão (SDD-25 RF-25.3: 0–15, 15–60, 60+ min; ×1,5/×2,0/×2,5). <paramref name="currentBand"/> −1 = nenhuma (Ausente, DP-24.8).</summary>
    public static PresenceBandView[] DefaultBands(int currentBand) =>
    [
        new("0–15 min", FormatMultiplier(1.5), currentBand == 0),
        new("15–60 min", FormatMultiplier(2.0), currentBand == 1),
        new("60+ min", FormatMultiplier(2.5), currentBand == 2),
    ];

    /// <summary>Dados de teste idênticos ao design (Main.dc.html): presente há 42 min, ×2,0, 4 missões (DP-24.7: renda relativa, D-18).</summary>
    public static MissionsTabModel TestData() => new(
        BonusTitle, FormatMultiplier(2.0), false,
        "Você está presente há 42 min. Interaja ao menos uma vez a cada 5 min para manter o bônus.",
        DefaultBands(1), SectionText,
        [
            new("m1", "Responda 3 perguntas de chefe", 1f, "3/3 · +1 baú raro", true, false),
            new("m2", "Escale 5 picos de demanda", 1f, "5/5 · +10 min de renda", true, false),
            new("m3", "Fique presente por 60 min", 0.7f, "42/60 min · +2 Reservas", false, false),
            new("m4", "Colete 10 caixas de suprimentos", 0.4f, "4/10 · +500 XP", false, false),
        ]);
}
