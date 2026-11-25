namespace SideWar.Persistence.Profile;

/// <summary>
/// Payload de profile-td.json (SDD-28 RF-28.10, 28.11, 28.13; ADR-035): perfil v3 do jogo Tower Defense.
/// DTO puro, sem referência ao estado da simulação. Selos gastos NÃO são gravados: derivam dos nós comprados e do custo em
/// <c>td-meta.json</c> (<see cref="TdProfileRules"/>), para o arquivo nunca divergir do conteúdo.
/// </summary>
public sealed record TdProfileData
{
    public const string Schema = "sidewar.td-profile";
    public const int SchemaVersion = 11;

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

    /// <summary>Teia de runas permanente (v6): bitset dos nós despertados (slot = anel*16+setor) em hexadecimal de 32 dígitos. Vale para toda partida/corrida nova.</summary>
    public string RuneWeb { get; init; } = TdRuneWeb.Empty;
    /// <summary>Fragmentos de runa ainda não gastos (v6), carregados na partida nova.</summary>
    public long RuneFragments { get; init; }

    /// <summary>
    /// Corrida infinita (v7): número ABSOLUTO da fase em que a corrida atual está (1 a 6 = primeira volta; 7+ = ciclo 2, 3, …). 0 = sem corrida salva
    /// (vale a fase sugerida pelo mapa). Sobe a cada fase concluída; a próxima abertura do jogo recomeça nessa fase, com a defesa inicial do ciclo.
    /// </summary>
    public int RunStageNumber { get; init; }
    /// <summary>Recorde da corrida infinita (v7): fase absoluta mais alta alcançada (ciclo = (n-1)/6 + 1) e a onda mais alta dentro dela. 0 = nunca jogou.</summary>
    public int BestStageNumber { get; init; }
    public int BestStageWave { get; init; }

    /// <summary>Torres da corrida atual (v10): sobrevivem a reabrir o app, trocar de fase e reiniciar a corrida. Só "Recomeçar do zero" ou vender as apaga.</summary>
    public IReadOnlyList<TdRunTowerData> RunTowers { get; init; } = [];

    /// <summary>Modo sombra ativo (v11): XP congelado e recompensas reduzidas; só o usuário sai, entre ondas. "Recomeçar do zero" desliga.</summary>
    public bool Shadow { get; init; }
    /// <summary>Resto (0 a 99) dos selos de sombra: cada selo de fase de ciclo vale <c>TdShadow.RewardPercent</c>% e o resto acumula aqui.</summary>
    public int ShadowSealCents { get; init; }

    /// <summary>Contadores persistentes por id de inimigo ou variação.</summary>
    public IReadOnlyList<TdEncounterData> Encounters { get; init; } = [];

    public static TdProfileData Default { get; } = new();

    /// <summary>
    /// "Recomeçar do zero": perfil novo com todo o progresso apagado (reputação, runas, fragmentos, fases, recordes, nós, legado).
    /// <see cref="IdleImportDone"/> fica true para o save idle antigo nunca ser importado de novo e devolver selos.
    /// </summary>
    public static TdProfileData ResetToNew() => new() { IdleImportDone = true };
}

/// <summary>Conversão do bitset da teia (<see cref="UInt128"/>) de/para o texto do perfil.</summary>
public static class TdRuneWeb
{
    public const string Empty = "00000000000000000000000000000000";
    public const long MaxFragments = 1_000_000_000_000L;
    public static string ToText(UInt128 bits) => bits.ToString("x32", System.Globalization.CultureInfo.InvariantCulture);
    /// <summary>Texto inválido vira teia vazia; bits acima do slot 127 não existem (UInt128 inteiro).</summary>
    public static UInt128 Parse(string? text) =>
        text is { Length: 32 } && UInt128.TryParse(text, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : UInt128.Zero;
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

/// <summary>Quantidade encontrada de um inimigo ou de uma variação.</summary>
public sealed record TdEncounterData
{
    public string Id { get; init; } = "";
    public int Count { get; init; }
}

/// <summary>Torre da corrida salva: tipo (id do conteúdo), posição lógica, preço pago, mira e módulo de fusão.</summary>
public sealed record TdRunTowerData
{
    public string DefId { get; init; } = "";
    public int X { get; init; }
    public bool Placed { get; init; } = true;
    public long PurchasePrice { get; init; }
    public int Targeting { get; init; }
    public string? ModuleDefId { get; init; }
    public long ModulePrice { get; init; }
    /// <summary>Módulos além do primeiro, como <c>id:preço,id:preço</c> (texto mantém a igualdade do registro).</summary>
    public string? ExtraModules { get; init; }
}
