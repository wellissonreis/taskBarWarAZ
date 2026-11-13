namespace SideWar.Runtime.Localization;

/// <summary>Textos visíveis em pt-BR (SDD-15 RF-15.11). Parcial: cada ficha acrescenta o seu bloco. Aqui, aba Arsenal (SDD-24 RF-24.5, ADR-034).</summary>
public static partial class Strings
{
    public const string ArsenalAutoScaleTitle = "AUTO-ESCALA (AUSENTE)";
    public const string ArsenalAutoScaleDescription = "Enquanto você trabalha em outra janela, usa 50% dos créditos para melhorar a torre mais barata a cada 15 min.";
    public const string ArsenalToggleOn = "ON";
    public const string ArsenalToggleOff = "OFF";

    /// <summary>Cartão de ajuda 5.1b, transcrito do <c>Main.dc.html</c>.</summary>
    public const string ArsenalHelp = "Arraste uma torre para um espaço livre dentro da barra de tarefas, ao lado dos ícones. Ícones, pesquisa, widget e relógio ficam bloqueados; acima da barra é a rota dos inimigos.";

    public const string ArsenalUpgrade = "MELHORAR";
    public const string ArsenalMaxLevel = "NÍVEL MÁX.";
    public const string ArsenalLockedSeal = "BLOQ.";
    public const string ArsenalNoPositionSeal = "SEM POSIÇÃO";
    public const string ArsenalReposition = "REPOSICIONAR";
    /// <summary>Tipo desbloqueado ainda sem torre: o ícone arrastável constrói a primeira.</summary>
    public const string ArsenalNewSeal = "NOVA";
    public const string ArsenalNewButton = "ARRASTE";

    public static string ArsenalLevel(int level) => $"LV {level}";
    public static string ArsenalUnlockStage(int stage) => $"FASE {stage:00}";
    public static string ArsenalUnlocksAtStage(int stage) => $"Desbloqueia na fase {stage:00}";
    /// <summary>Título da dica do fantasma: nome + custo de construção (omitido no reposicionamento).</summary>
    public static string ArsenalBuildHint(string name, string? cost) => string.IsNullOrEmpty(cost) ? name : $"{name} · {cost}";
}
