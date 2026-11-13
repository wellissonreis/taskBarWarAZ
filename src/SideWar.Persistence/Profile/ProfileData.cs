using System.Text.Json.Serialization;

namespace SideWar.Persistence.Profile;

/// <summary>Estado de uma missão diária (SDD-27 RF-27.11): Em andamento → Concluída → Coletada.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MissionStateData>))]
public enum MissionStateData { InProgress, Completed, Collected }

/// <summary>
/// Payload de profile.json (SDD-14 RF-14.5 ajustado por SDD-27/26/25 RF-25.11): o que sobrevive ao Migrar.
/// DTO puro, sem referência ao SimState; o mapeamento fica para a integração (T6.9 / T3.x).
/// </summary>
public sealed record ProfileData
{
    public const string Schema = "sidewar.profile";
    public const int SchemaVersion = 1;
    public const string DefaultRegion = "brazil-south";

    public int Level { get; init; } = 1;
    public long Xp { get; init; }
    public long Reservas { get; init; }
    public long TotalKills { get; init; }
    public double PlaySeconds { get; init; }

    /// <summary>Seed do Rng, gerada na primeira partida e nunca reiniciada (SDD-27 RF-27.9). Nula até a primeira partida.</summary>
    public ulong? Seed { get; init; }
    /// <summary>Estado do Rng persistido (RF-14.5). A sessão também o grava (RF-14.6); quem vence no restore é decisão do T6.9.</summary>
    public RngData? Rng { get; init; }

    public IReadOnlyList<StageProgressData> Stages { get; init; } = [];
    public string Region { get; init; } = DefaultRegion;
    public IReadOnlyList<string> UnlockedRegions { get; init; } = [DefaultRegion];
    public IReadOnlyList<string> UnlockedTowers { get; init; } = [];
    public MissionsData Missions { get; init; } = new();

    /// <summary>Ledger de respostas de chefe por (fase, tentativa, onda) (SDD-26 RF-26.7).</summary>
    public IReadOnlyList<LedgerEntryData> Ledger { get; init; } = [];
    /// <summary>Perguntas já sorteadas por (fase, tentativa) (SDD-26 RF-26.5).</summary>
    public IReadOnlyList<DrawnQuestionsData> DrawnQuestions { get; init; } = [];
    /// <summary>Ledger do COLETAR ×2, por id de relatório de retorno (SDD-26 RF-26.16).</summary>
    public IReadOnlyList<CollectLedgerEntryData> CollectLedger { get; init; } = [];
    public EventCountersData Counters { get; init; } = new();

    public static ProfileData Default { get; } = new();
}

public sealed record RngData(ulong State, ulong Inc);

/// <summary>Progresso por fase: desbloqueio, estrelas (máximo, nunca diminui), tentativa vigente e chefes vencidos (SDD-27 RF-27.9).</summary>
public sealed record StageProgressData
{
    public int StageId { get; init; }
    public bool Unlocked { get; init; }
    public int Stars { get; init; }
    public int Attempt { get; init; } = 1;
    public IReadOnlyList<int> ChefesVencidos { get; init; } = [];
}

public sealed record MissionsData
{
    /// <summary>Data local <c>yyyy-MM-dd</c> do sorteio (SDD-27 RF-27.11); nula antes da primeira.</summary>
    public string? DateLocal { get; init; }
    public IReadOnlyList<MissionProgressData> Items { get; init; } = [];
}

public sealed record MissionProgressData
{
    public string Id { get; init; } = "";
    public MissionStateData State { get; init; } = MissionStateData.InProgress;
    public long Progress { get; init; }
}

public sealed record LedgerEntryData
{
    public int Stage { get; init; }
    public int Attempt { get; init; }
    public int Wave { get; init; }
    public string QuestionId { get; init; } = "";
    /// <summary>Nulo = tempo esgotado.</summary>
    public int? OptionIndex { get; init; }
    public bool Correct { get; init; }
    public bool BonusConsumed { get; init; }
}

public sealed record DrawnQuestionsData
{
    public int Stage { get; init; }
    public int Attempt { get; init; }
    public IReadOnlyList<string> QuestionIds { get; init; } = [];
}

public sealed record CollectLedgerEntryData
{
    public long ReportId { get; init; }
    public string QuestionId { get; init; } = "";
    /// <summary>false = pergunta sorteada e ainda sem resposta (T6.4b); entradas antigas valem como respondidas.</summary>
    public bool Answered { get; init; } = true;
    public int? OptionIndex { get; init; }
    public bool Correct { get; init; }
}

/// <summary>Contadores de eventos (SDD-25 RF-25.11, SDD-27 RF-27.11) e o relógio dos eventos (SDD-26: tempo presente acumulado).</summary>
public sealed record EventCountersData
{
    public long Answers { get; init; }
    public long PeaksScaled { get; init; }
    public long CratesCollected { get; init; }
    public long Kills { get; init; }
    public long ManualUpgrades { get; init; }
    /// <summary>Minutos presentes no dia local de <see cref="PresentMinutesDate"/>.</summary>
    public int PresentMinutesToday { get; init; }
    public string? PresentMinutesDate { get; init; }
    public double EventClockSeconds { get; init; }
}
