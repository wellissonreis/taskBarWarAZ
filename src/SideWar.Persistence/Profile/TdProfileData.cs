namespace SideWar.Persistence.Profile;

/// <summary>
/// Payload de profile-td.json (SDD-28 RF-28.10, 28.11, 28.13; ADR-035): perfil v3 do jogo Tower Defense.
/// DTO puro, sem referência ao estado da simulação. Selos gastos NÃO são gravados: derivam dos nós comprados e do custo em
/// <c>td-meta.json</c> (<see cref="TdProfileRules"/>), para o arquivo nunca divergir do conteúdo.
/// </summary>
public sealed record TdProfileData
{
    public const string Schema = "sidewar.td-profile";
    public const int SchemaVersion = 5;

    /// <summary>Total de selos já ganhos (1 por estrela nova, 1 por desafio, mais os iniciais da migração do idle).</summary>
    public int SealsEarned { get; init; }
    /// <summary>Selos concedidos pela migração do save idle (DP-28.4); já incluídos em <see cref="SealsEarned"/>. 0 se não migrou.</summary>
    public int MigratedSeals { get; init; }
    /// <summary>true depois que o save idle foi consultado (importado ou ausente), para a migração nunca rodar duas vezes.</summary>
    public bool IdleImportDone { get; init; }

    public IReadOnlyList<TdStageProgressData> Stages { get; init; } = [new TdStageProgressData { StageId = 1, Unlocked = true }];
    /// <summary>Ids dos nós da árvore de selos comprados (ex.: <c>defense.damage1</c>).</summary>
    public IReadOnlyList<string> PurchasedNodes { get; init; } = [];
    /// <summary>Torres desbloqueadas por fase (RF-28.5); torres nunca são compradas com selos.</summary>
    public IReadOnlyList<string> UnlockedTowers { get; init; } = [];

    /// <summary>Modo infinito (pós-MVP, RF-28.11): onda máxima por seed jogada.</summary>
    public IReadOnlyList<TdInfiniteRunData> InfiniteRuns { get; init; } = [];

    public int LegacyTotal { get; init; }
    public int BestRunWaves { get; init; }
    public string LegacyLastRun { get; init; } = "";
    public int RunsPlayed { get; init; }

    /// <summary>Reputação (XP de patente, v5): sobe ao vencer ondas e desce quando inimigos chegam à base; nunca abaixo de 0. A patente deriva dela.</summary>
    public int Reputation { get; init; }

    public static TdProfileData Default { get; } = new();
}

/// <summary>Progresso por fase (RF-28.10): estrelas (máximo, nunca diminui) e desafio concluído (separado das estrelas).</summary>
public sealed record TdStageProgressData
{
    public int StageId { get; init; }
    public bool Unlocked { get; init; }
    public int Stars { get; init; }
    public bool ChallengeCompleted { get; init; }
    /// <summary>Coroa: fase vencida sem perder nenhuma integridade (RF-28.21; schema v2).</summary>
    public bool Crown { get; init; }
    /// <summary>Medalhas ganhas (bits de <c>TdMedals</c>, RF-28.21; schema v2).</summary>
    public int Medals { get; init; }
    /// <summary>Recorde pessoal de pontuação da fase (0 = nunca vencida; schema v2).</summary>
    public int BestScore { get; init; }
}

/// <summary>Melhor onda por seed do modo infinito. A seed é <c>ulong</c> (PCG32, ADR-026): vai ao JSON como número exato.</summary>
public sealed record TdInfiniteRunData
{
    public ulong Seed { get; init; }
    public int MaxWave { get; init; }
}
