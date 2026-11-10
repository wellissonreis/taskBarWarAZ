using System.Text.Json.Serialization;
using SideWar.Persistence.Profile;

namespace SideWar.Persistence.Session;

[JsonConverter(typeof(JsonStringEnumConverter<PresenceStateData>))]
public enum PresenceStateData { Presente, Ausente, Offline }

[JsonConverter(typeof(JsonStringEnumConverter<CardStateData>))]
public enum CardStateData { Aberto, Recolhido }

/// <summary>
/// Payload de session.json = o <c>OfflineSnapshot</c> (SDD-14 RF-14.6, SDD-25 RF-25.6/25.11) mais o estado exato da simulação.
/// DTO puro, desacoplado do SimState: o mapeamento SimState ⇄ DTO e o cálculo do <see cref="StateHash"/> ficam para a integração.
/// </summary>
public sealed record OfflineSnapshot
{
    public const string Schema = "sidewar.session";
    public const int SchemaVersion = 1;

    /// <summary>Instante do snapshot (UTC). Obrigatório: o OfflineCalculator calcula <c>agora − SavedUtc</c>.</summary>
    public required DateTime SavedUtc { get; init; }
    /// <summary>ContentHash do conteúdo com que a sessão foi gravada (RF-14.8 passo 4/5).</summary>
    public required string GameContentHash { get; init; }
    /// <summary>StateHash (fnv64 hex) da simulação gravada; conferido pelo restore antes do cálculo offline (RF-14.6).</summary>
    public required string StateHash { get; init; }
    public required PresenceData Presence { get; init; }
    public bool AutoScaleOn { get; init; } = true;
    public PendingData Pending { get; init; } = new();
    public CardStateData CardState { get; init; } = CardStateData.Aberto;
    public required LayoutData Layout { get; init; }
    public required SimData Sim { get; init; }
    public EventsData Events { get; init; } = new();
}

public sealed record PresenceData
{
    public PresenceStateData State { get; init; } = PresenceStateData.Presente;
    public DateTime? LastInteractionUtc { get; init; }
    public double PresentSeconds { get; init; }
    public double AutoScaleAccumSeconds { get; init; }
}

/// <summary>Pendências do relatório de retorno (SDD-25 RF-25.9): nunca se perdem ao fechar o app.</summary>
public sealed record PendingData
{
    /// <summary>Id do relatório de retorno (chave do ledger do COLETAR ×2, SDD-26 RF-26.16); 0 = sem relatório.</summary>
    public long ReportId { get; init; }
    public long Credits { get; init; }
    public long Xp { get; init; }
    public IReadOnlyList<UpgradeLineData> Melhorias { get; init; } = [];
    public int Quedas { get; init; }
    public int OndaDe { get; init; }
    public int OndaPara { get; init; }
    public double TempoS { get; init; }
    public LossesData Perdas { get; init; } = new();
}

public sealed record UpgradeLineData(string Tower, int From, int To);

public sealed record LossesData
{
    public int Picos { get; init; }
    public int Caixas { get; init; }
    public int Chefes { get; init; }
}

/// <summary>Layout da faixa (ADR-026): usado como está no restore; <c>LayoutChanged</c> vem depois, entre ticks.</summary>
public sealed record LayoutData
{
    public float SpawnX { get; init; }
    public float LeakX { get; init; }
    public float CoreX { get; init; }
    public float PortalX { get; init; }
    public IReadOnlyList<float> SlotX { get; init; } = [];
}

public sealed record SimData
{
    public long Tick { get; init; }
    public int Stage { get; init; } = 1;
    /// <summary>StageRun (SDD-27 RF-27.9): tentativa, quedas, ondaMax e chefes vencidos da passagem atual.</summary>
    public int Attempt { get; init; } = 1;
    public int Falls { get; init; }
    public int Wave { get; init; } = 1;
    public int OndaMax { get; init; } = 1;
    public IReadOnlyList<int> ChefesVencidos { get; init; } = [];
    public long Credits { get; init; }
    public double Carry { get; init; }
    public long Xp { get; init; }
    public int Level { get; init; } = 1;
    public int BaseHp { get; init; }
    public int MaxBaseHp { get; init; }
    public long Reservas { get; init; }
    public string Region { get; init; } = ProfileData.DefaultRegion;
    public IReadOnlyList<string> UnlockedTowers { get; init; } = [];
    public RngData Rng { get; init; } = new(0, 1);
    public int NextId { get; init; } = 1;
    public WaveStateData WaveState { get; init; } = new();
    public IReadOnlyList<TowerData> Towers { get; init; } = [];
    public IReadOnlyList<EnemyData> Enemies { get; init; } = [];
}

/// <summary>Estado da onda automática (fila, cursor, descanso de 1,4 s). O formato final depende de T3.5/T3.8 (pendência de mapeamento).</summary>
public sealed record WaveStateData
{
    public IReadOnlyList<QueuedSpawnData> Queue { get; init; } = [];
    public int Cursor { get; init; }
    public float Clock { get; init; }
    public float Rest { get; init; }
    public int SpawnIndex { get; init; }
}

public sealed record QueuedSpawnData(float AtS, string Kind);

public sealed record TowerData
{
    public int Id { get; init; }
    public string Type { get; init; } = "";
    /// <summary>0-based. <c>≥ layout.SlotX.Count</c> = SEM SLOT (SDD-21 RF-21.8): continua gravado e volta sem custo.</summary>
    public int SlotIndex { get; init; }
    public int Level { get; init; } = 1;
    public float Cooldown { get; init; }
    public string Mode { get; init; } = "First";
    public int Hits { get; init; }
}

public sealed record EnemyData
{
    public int Id { get; init; }
    public string Kind { get; init; } = "";
    public float X { get; init; }
    public double Hp { get; init; }
    public double MaxHp { get; init; }
    public float Speed { get; init; }
    public float SpawnSpeed { get; init; }
    public int Armor { get; init; }
    public bool Chilled { get; init; }
    public bool Boss { get; init; }
    public string LastHit { get; init; } = "None";
}

/// <summary>Estado dos eventos ao vivo (SDD-26). Ainda não há equivalente no SimState; o formato é provisório (pendência de mapeamento).</summary>
public sealed record EventsData
{
    public BossEncounterData? Boss { get; init; }
    public double? PeakBannerRemainingS { get; init; }
    public IReadOnlyList<double> PeakQueue { get; init; } = [];
    public double? PeakBonusRemainingS { get; init; }
    public bool PeakDue { get; init; }
    public SupplyCrateData? Crate { get; init; }
    public double? NextPeakInS { get; init; }
    public double? NextCrateInS { get; init; }
}

public sealed record BossEncounterData(string QuestionId, double RemainingS, bool CardOpen);

public sealed record SupplyCrateData(int X, double RemainingS);
