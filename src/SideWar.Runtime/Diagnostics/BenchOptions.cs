using System.Globalization;

namespace SideWar.Runtime.Diagnostics;

/// <summary>Parsed `--bench` command line (SDD-16 RF-16.2). Pure: no I/O.</summary>
public sealed record BenchOptions(string Scenario, int Seconds, string OutPath, bool NoInput)
{
    public const string Flag = "--bench";
    public const int DefaultSeconds = 60;

    /// <summary>Scenarios implemented for E1 (T1.13). `baseline-wpf` is P-00: WPF without windows plus an idle T-Game.</summary>
    public static readonly IReadOnlyList<string> Implemented = ["e1-synthetic", "hidden", "idle-static", "baseline-wpf"];

    /// <summary>Scenarios named by RF-16.2 that belong to later phases (SanityBot, painel, offline, ...).</summary>
    public static readonly IReadOnlyList<string> Deferred =
    [
        "presente-onda-media", "presente-onda-pesada", "soak", "strip-present", "strip-away", "ausente",
        "offline-retorno", "painel-aberto", "varias-janelas",
    ];

    public static bool IsRequested(string[] args) => args.Contains(Flag, StringComparer.Ordinal);

    public static bool IsVersionRequested(string[] args) => args.Contains("--version", StringComparer.Ordinal);

    public static bool IsDiagRequested(string[] args) => args.Contains("--diag", StringComparer.Ordinal);

    /// <summary>Returns the options or a user-facing error (exit code 1).</summary>
    public static (BenchOptions? Options, string? Error) Parse(string[] args)
    {
        var scenario = Value(args, Flag);
        if (string.IsNullOrWhiteSpace(scenario) || scenario.StartsWith("--", StringComparison.Ordinal))
            return (null, "Usage: --bench <" + string.Join('|', Implemented) + "> [--seconds N] [--out file.csv] [--no-input]");
        if (Deferred.Contains(scenario, StringComparer.Ordinal))
            return (null, $"Scenario '{scenario}' is defined by RF-16.2 but is not available in this phase.");
        if (!Implemented.Contains(scenario, StringComparer.Ordinal))
            return (null, $"Unknown scenario '{scenario}'. Available: {string.Join(", ", Implemented)}.");

        var seconds = DefaultSeconds;
        var secondsText = Value(args, "--seconds");
        if (secondsText is not null &&
            (!int.TryParse(secondsText, NumberStyles.None, CultureInfo.InvariantCulture, out seconds) || seconds < 1 || seconds > 86_400))
            return (null, "--seconds must be an integer between 1 and 86400.");

        var out_ = Value(args, "--out") ?? $"bench-{scenario}.csv";
        return (new BenchOptions(scenario, seconds, out_, args.Contains("--no-input", StringComparer.Ordinal)), null);
    }

    private static string? Value(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
