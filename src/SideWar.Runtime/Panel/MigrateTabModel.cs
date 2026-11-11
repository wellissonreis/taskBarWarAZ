using SideWar.Core.Economy;

namespace SideWar.Runtime.Panel;

/// <summary>Tom do texto de uma região (cor resolvida pelo Rendering): principal = escolhida; secundário = atual; rótulo = demais.</summary>
public enum RegionTone { Principal, Secundario, Rotulo }

/// <summary>Linha de região. <see cref="Selectable"/> = desbloqueada (ADR-031 item 11); clique define a região da próxima migração.</summary>
public sealed record RegionRowView(string Id, string Text, bool Selectable, RegionTone Tone);

/// <summary>Estado do botão MIGRAR (SDD-24 RF-24.8): normal, armado (confirmação por segundo clique) ou indisponível (onda máx. &lt; 15).</summary>
public enum MigrateButtonState { Normal, Armed, Unavailable }

/// <summary>Modelo imutável da aba Migrar (SDD-24 RF-24.8). Textos já formatados.</summary>
public sealed record MigrateTabModel(
    string CardBody,
    string CurrentReserves,
    string CurrentProduction,
    string GainNow,
    string TotalProduction,
    IReadOnlyList<RegionRowView> Regions,
    MigrateButtonState Button,
    string ButtonLabel,
    long Version = 0)
{
    public const string TabTitle = "MIGRAR (PRESTÍGIO)";
    public const string CardTitle = "MIGRAR PARA NOVA REGIÃO";
    public const string TileCurrentLabel = "RESERVAS ATUAIS";
    public const string TileGainLabel = "AO MIGRAR AGORA";
    public const string RegionsLabel = "REGIÕES";
    public const string ArmedLabel = "CONFIRMAR · REINICIA TORRES E ONDAS";
    public const string UnavailableLabel = "DISPONÍVEL NA ONDA 15";
    public const int MinWave = 15;

    public const string Body =
        "Reinicia ondas, torres e créditos da fase. Você mantém nível, estrelas e perguntas respondidas, e ganha Reservas que aumentam toda a produção para sempre.";

    public static string NormalLabel(long gain) => $"MIGRAR · +{CreditFormatter.Format(gain)} RESERVAS";

    /// <summary>Monta o modelo a partir dos números (SDD-27 RF-27.10): produção = 1% × Reservas; total = (Reservas + ganho)%.</summary>
    public static MigrateTabModel Create(long reserves, long gain, IReadOnlyList<RegionRowView> regions, int maxWave, bool armed)
    {
        var state = maxWave < MinWave ? MigrateButtonState.Unavailable : armed ? MigrateButtonState.Armed : MigrateButtonState.Normal;
        var label = state switch
        {
            MigrateButtonState.Unavailable => UnavailableLabel,
            MigrateButtonState.Armed => ArmedLabel,
            _ => NormalLabel(gain),
        };
        return new(Body, CreditFormatter.Format(reserves), $"+{CreditFormatter.Format(reserves)}% produção",
            "+" + CreditFormatter.Format(gain), $"total +{CreditFormatter.Format(reserves + gain)}%", regions, state, label);
    }

    /// <summary>Linhas de região do design (Brazil South atual; East US; West Europe bloqueada), com a escolha opcional (<c>→ </c> e tom principal).</summary>
    public static RegionRowView[] TestRegions(int? chosen = null) =>
    [
        Row(0, "Brazil South · atual", true, RegionTone.Secundario, chosen),
        Row(1, "East US · inimigos +40% vida, Reservas ×1,5", true, RegionTone.Rotulo, chosen),
        Row(2, "West Europe · desbloqueia com 30 Reservas", false, RegionTone.Rotulo, chosen),
    ];

    public static RegionRowView Row(int index, string text, bool selectable, RegionTone tone, int? chosen) =>
        chosen == index && selectable
            ? new($"r{index}", "→ " + text, true, RegionTone.Principal)
            : new($"r{index}", text, selectable, tone);

    /// <summary>Dados de teste idênticos ao design: 14 Reservas, +8 ao migrar, total +22%.</summary>
    public static MigrateTabModel TestData(int? chosenRegion = null, bool armed = false, int maxWave = 40) =>
        Create(14, 8, TestRegions(chosenRegion), maxWave, armed);
}
