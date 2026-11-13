namespace SideWar.Core.Simulation;

/// <summary>Constrói uma torre <c>Level=1</c>, <c>Mode=First</c> na posição X (DIP inteiro) da faixa (SDD-27 RF-27.5, ADR-034).</summary>
public sealed record BuildTower(TowerType Type, int X) : IGameCommand;

/// <summary>Posiciona, sem custo, uma torre <c>SEM POSIÇÃO</c> em X (DP-33.1; SDD-11 RF-11.10).</summary>
public sealed record PlaceTower(int TowerId, int X) : IGameCommand;

/// <summary>Migração de save (DP-33.7): coloca as torres <c>SEM POSIÇÃO</c> na primeira posição válida, uma única vez.</summary>
public sealed record AutoPlaceUnpositioned : IGameCommand;

/// <summary>Melhora a torre de <c>Id</c> dado em um nível (SDD-27 RF-27.5). Conta como melhoria manual.</summary>
public sealed record UpgradeTower(int TowerId) : IGameCommand;

/// <summary>Muda o modo de mira da torre (vale também com o jogo pausado; SDD-11 RF-11.10).</summary>
public sealed record SetTargeting(int TowerId, Targeting Mode) : IGameCommand;

/// <summary>Clique no banner de pico (SDD-26 RF-26.15).</summary>
public sealed record ClickPeakBanner : IGameCommand;

/// <summary>Abre o card da pergunta do chefe (SDD-26 RF-26.15); só registra o estado do card, sem pausar.</summary>
public sealed record OpenBossQuestion : IGameCommand;

/// <summary>Responde à pergunta do chefe com o índice da alternativa (SDD-26 RF-26.15).</summary>
public sealed record AnswerBossQuestion(int OptionIndex) : IGameCommand;

/// <summary>Clique na caixa de suprimentos (SDD-26 RF-26.15).</summary>
public sealed record ClickSupplyCrate : IGameCommand;

/// <summary>Migrar para a região de índice dado (SDD-27 RF-27.10).</summary>
public sealed record MigrateTo(int RegionIndex) : IGameCommand;

public enum CommandStatus : byte { Ok = 0, Rejected }

/// <summary>Motivo da rejeição, na ordem de validação de SDD-27 RF-27.5.</summary>
public enum CommandReason : byte
{
    None = 0,
    UnknownCommand,
    InvalidLayout,
    /// <summary>A caixa de 44 DIP não cabe numa zona livre (SDD-11 RF-11.9).</summary>
    OutOfZone,
    /// <summary>A caixa intersecta a de outra torre posicionada (SDD-11 RF-11.9).</summary>
    Overlap,
    /// <summary>Já existem 24 torres, inclusive as SEM POSIÇÃO (DP-33.3).</summary>
    TowerLimit,
    /// <summary><c>PlaceTower</c> numa torre que já está posicionada (DP-33.1).</summary>
    NotUnpositioned,
    TowerLocked,
    TowerNotFound,
    MaxLevel,
    Unreachable,
    InsufficientCredits,
    /// <summary>Evento ausente, não ativo ou fora de Presente (SDD-26 RF-26.15).</summary>
    NotAvailable,
    /// <summary>Ledger já tem resposta para a chave (SDD-26 RF-26.7).</summary>
    AlreadyUsed,
    /// <summary>Sem partida em andamento (<c>RunState != InStage</c>, SDD-10 RF-10.5).</summary>
    InvalidPhase,
    /// <summary>Região ainda bloqueada ou inexistente (SDD-27 RF-27.10).</summary>
    RegionLocked,
}

/// <summary>Resultado de um comando: <c>Missing</c> = créditos que faltam (só em <see cref="CommandReason.InsufficientCredits"/>).</summary>
public readonly record struct CommandResult(CommandStatus Status, CommandReason Reason, long Missing)
{
    public bool IsOk => Status == CommandStatus.Ok;
    public static CommandResult Ok { get; } = new(CommandStatus.Ok, CommandReason.None, 0);
    public static CommandResult Reject(CommandReason reason, long missing = 0) => new(CommandStatus.Rejected, reason, missing);
}

/// <summary>
/// Custos de SDD-27 RF-27.5 (stub mínimo de economia, ex-T3.9): potências por multiplicação iterativa em <c>double</c>
/// (sem <c>Math.Pow</c>, determinístico), teto em <c>long.MaxValue/2</c> ("inalcançável", RF-27.1).
/// O <c>ceil</c> desconta 1e-12 relativo para não subir um inteiro exato por erro de representação (ex.: 45×1,2 = 54).
/// </summary>
public static class TowerCosts
{
    private const double Unreachable = long.MaxValue / 2.0;

    /// <summary><c>ceil(base × factor^n)</c>; <c>n</c> = torres existentes de qualquer tipo, inclusive SEM POSIÇÃO (ADR-032).</summary>
    public static bool TryBuildCost(long baseCost, int existingTowers, double factor, out long cost)
    {
        double v = baseCost;
        for (int i = 0; i < existingTowers; i++) v *= factor;
        return TryCeil(v, out cost);
    }

    /// <summary><c>ceil((base × upgradeFactor) × growth^(level−1))</c> para melhorar de <paramref name="level"/> para +1.</summary>
    public static bool TryUpgradeCost(long baseCost, int level, double upgradeFactor, double growth, out long cost)
    {
        double v = baseCost * upgradeFactor;
        for (int i = 1; i < level; i++) v *= growth;
        return TryCeil(v, out cost);
    }

    private static bool TryCeil(double v, out long cost)
    {
        if (double.IsNaN(v) || v >= Unreachable) { cost = long.MaxValue; return false; }
        cost = (long)Math.Ceiling(v * (1.0 - 1e-12));
        return true;
    }
}
