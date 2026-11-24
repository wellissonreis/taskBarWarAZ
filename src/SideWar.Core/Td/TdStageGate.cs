using SideWar.Core.Content.Td;

namespace SideWar.Core.Td;

/// <summary>Trava de patente aplicada à região de conteúdo em qualquer ciclo.</summary>
public static class TdStageGate
{
    public static bool Allowed(TdStageDef stage, TdRankLevel rank) => (int)rank >= stage.MinRank;

    public static int LastAllowed(TdStageDef[] stages, TdRankLevel rank) =>
        stages.Last(s => Allowed(s, rank)).Id;

    public static int Next(TdStageDef[] stages, int current, TdRankLevel rank)
    {
        int count = stages.Length, cycleStart = (current - 1) / count * count;
        int currentIndex = (current - 1) % count;
        if (!Allowed(stages[currentIndex], rank)) return cycleStart + LastAllowed(stages, rank);
        int next = current + 1;
        return Allowed(stages[(next - 1) % count], rank) ? next : current;
    }

    public static string RegionWithArticle(string region) => region switch
    {
        "brazilsouth" => "ao Brazil South",
        "eastus" => "ao East US",
        "westeurope" => "à West Europe",
        "japaneast" => "ao Japan East",
        "uksouth" => "ao UK South",
        "australiaeast" => "à Australia East",
        _ => $"a {region}",
    };
}
