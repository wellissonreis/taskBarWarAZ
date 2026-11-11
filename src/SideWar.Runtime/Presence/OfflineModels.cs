using SideWar.Core.Simulation;

namespace SideWar.Runtime.Presence;

/// <summary>Torre do snapshot offline. <c>Slot</c> nulo = SEM SLOT (não atira nem entra no DPS, ADR-032).</summary>
public sealed record OfflineTower(int Id, TowerType Type, int? Slot, int Level);

/// <summary>
/// Entrada do cálculo offline (SDD-25 RF-25.6), gravada no Hide, nos saves periódicos e no encerramento.
/// <c>Stage</c> é o id (1-based) da fase; <c>Wave</c> é a onda que estava em jogo.
/// Pendências (RF-25.11) e chefes vencidos não entram: o modelo nunca joga onda de chefe.
/// </summary>
public sealed record OfflineSnapshot(
    DateTime SavedUtc, int Stage, int Wave, int WaveMax, string RegionId, long Reservas, int Level, long Xp,
    long Credits, double Carry, IReadOnlyList<OfflineTower> Towers, bool AutoScaleOn, double AutoScaleAccumSeconds);

/// <summary>Parâmetros do modelo (SDD-25; valores iniciais de presence.json, ajustáveis).</summary>
public sealed record OfflineConfig(
    double MaxElapsedSeconds = 12 * 3600,
    double CardMinSeconds = 10 * 60,
    int MaxWaves = 2000,
    double WinFactor = 0.8,
    double WaveSlackSeconds = 10,
    double AutoScaleIntervalSeconds = 15 * 60,
    double AutoScaleBudgetFraction = 0.5,
    double PeakDivisorMinutes = 11.5,
    double BoxDivisorMinutes = 4.5)
{
    public static OfflineConfig Default { get; } = new();
}

/// <summary>Melhoria da auto-escala de uma torre (tipo, nível inicial, nível final).</summary>
public readonly record struct AutoUpgrade(TowerType Tipo, int DeNivel, int ParaNivel);

/// <summary>Relatório offline (SDD-25 RF-25.8). <c>Creditos</c> é o bruto (já a ×0,25 e Reservas), antes do gasto da auto-escala.</summary>
public sealed record OfflineReport(
    double TempoAcumuladoS, long Creditos, int OndaDe, int OndaPara, int Quedas, long Xp, int NivelDe, int NivelPara,
    IReadOnlyList<AutoUpgrade> MelhoriasAuto, int NMelhorias, long GastoAuto,
    int PerdaPicos, int PerdaCaixas, int PerdaChefes, bool ParouNoChefe);

/// <summary>
/// Saída de <see cref="OfflineCalculator.Compute"/>. <c>State</c> é o snapshot novo (saldo após a auto-escala, torres,
/// onda e <c>ondaMax</c>; XP e nível só mudam ao coletar). <c>PendingCredits</c> é o pendente líquido (RF-25.8);
/// <c>ShowCard</c> é <c>elapsed ≥ 10 min</c> (abaixo disso os ganhos vão direto ao saldo, DP-25.2).
/// </summary>
public sealed record OfflineResult(
    OfflineReport Report, OfflineSnapshot State, long PendingCredits, long PendingXp, bool ShowCard,
    IReadOnlyList<string> Logs);
