namespace SideWar.Runtime.Diagnostics;

/// <summary>Janelas do jogo (SDD-03 RF-03.9) que um cenário do <c>--bench</c> mantém visíveis.</summary>
[Flags]
public enum BenchWindows
{
    None = 0,
    Strip = 1,
    Widget = 2,
    Panel = 4,
    Banner = 8,
    SupplyBox = 16,
    Seal = 32,
    All = Strip | Widget | Panel | Banner | SupplyBox | Seal,
}

/// <summary>Presença que o cenário mantém (SDD-16 RF-16.2: a presença é forçada, sem esperar 5 min).</summary>
public enum BenchPresence { Present, Away }

/// <summary>
/// Cenário do <c>--bench</c> dirigido pelo <c>SanityBot</c> (SDD-16 RF-16.2, T4.11): população-alvo de inimigos e
/// de torres, presença forçada e conjunto de janelas. Dados puros; a montagem é de <c>BenchScenarioRig</c> (Runtime)
/// e do <c>BenchGameHost</c> (App, Windows).
/// </summary>
public sealed record BenchScenario(
    string Name, BenchPresence Presence, int TargetEnemies, int TargetTowers, BenchWindows Windows, IReadOnlyList<string> Aliases,
    bool TowersInBar = false); // faixa de 96 DIP sobre a barra: padrão towersInBar = false / hospedeiro Band (PLANO §8.2, DP-33.4)

/// <summary>Catálogo dos cenários de jogo do <c>--bench</c> (T4.11).</summary>
public static class BenchScenarios
{
    public const string PresenteOndaMedia = "presente-onda-media";
    public const string PresenteOndaPesada = "presente-onda-pesada";
    public const string Ausente = "ausente";
    public const string VariasJanelas = "varias-janelas";

    /// <summary>
    /// `presente-onda-media`: 20 inimigos e 6 torres, Presente; `presente-onda-pesada`: 44 e 12 com o banner;
    /// `ausente`: 24 e 12 em Ausente (8 fps, opacidade 128, selo); `varias-janelas`: Presente, 24 e 12, todas as janelas.
    /// </summary>
    public static readonly IReadOnlyList<BenchScenario> Game =
    [
        new(PresenteOndaMedia, BenchPresence.Present, 20, 6, BenchWindows.Strip | BenchWindows.Widget, ["presente-onda"]),
        new(PresenteOndaPesada, BenchPresence.Present, 44, 12, BenchWindows.Strip | BenchWindows.Widget | BenchWindows.Banner, []),
        new(Ausente, BenchPresence.Away, 24, 12, BenchWindows.Strip | BenchWindows.Widget | BenchWindows.Seal, ["strip-away"]),
        new(VariasJanelas, BenchPresence.Present, 24, 12, BenchWindows.All, []),
    ];

    /// <summary>Nomes canônicos mais os apelidos aceitos na linha de comando.</summary>
    public static IEnumerable<string> AllNames => Game.SelectMany(s => s.Aliases.Prepend(s.Name));

    public static BenchScenario? Find(string? name) =>
        name is null ? null : Game.FirstOrDefault(s =>
            string.Equals(s.Name, name, StringComparison.Ordinal) || s.Aliases.Contains(name, StringComparer.Ordinal));

    public static bool IsGameScenario(string? name) => Find(name) is not null;
}
