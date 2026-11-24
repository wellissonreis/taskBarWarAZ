using SideWar.Core.Td;

namespace SideWar.Runtime.Td;

public sealed partial class TdUiController
{
    /// <summary>Ciclo atual da corrida infinita, 1-based (1 = primeira volta pelas 6 fases; "Ciclo 2" em diante repete as regiões mais forte). Para a UI do Mapa/Selos.</summary>
    public int CycleNumber => _match().Cycle + 1;

    /// <summary>Fase de conteúdo (1 a 6, a região) da fase atual.</summary>
    public int CycleStageId => _match().StageId;

    /// <summary>Recorde da corrida infinita como (ciclo 1-based, fase 1 a 6, onda); (0, 0, 0) sem recorde.</summary>
    public (int Cycle, int Stage, int Wave) BestRun
    {
        get
        {
            int n = _host.BestStageNumber;
            if (n < 1) return (0, 0, 0);
            int count = Math.Max(1, _match().Content.Stages.Stages.Length);
            return ((n - 1) / count + 1, (n - 1) % count + 1, _host.BestStageWave);
        }
    }

    /// <summary>Sequência de ondas sem vazar ao lado do XP no HUD: " x3" após três ondas limpas, zerada ao vazar.</summary>
    internal static string StreakSuffix(TdMatchState m) =>
        m.Streak < 2 ? "" : " x" + m.Streak.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Rótulo da fase no HUD: "Fase 3" na primeira volta; "Ciclo 2 · Fase 3" a partir do ciclo 2 (<paramref name="stageNumber"/> absoluto).</summary>
    internal static string StageLabel(int stageNumber, int stageCount)
    {
        int count = Math.Max(1, stageCount);
        int cycle = (Math.Max(1, stageNumber) - 1) / count;
        return cycle == 0 ? $"Fase {stageNumber}" : $"Ciclo {cycle + 1} · Fase {(stageNumber - 1) % count + 1}";
    }
}
