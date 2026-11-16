using SideWar.Core.Content.Td;

namespace SideWar.Persistence.Profile;

/// <summary>Regras de selos do perfil v2 (RF-28.13), derivadas do conteúdo <c>td-meta.json</c>.</summary>
public static class TdProfileRules
{
    /// <summary>Soma do custo dos nós comprados (id desconhecido não custa nada e é ignorado).</summary>
    public static int SealsSpent(TdProfileData p, TdMetaDef meta)
    {
        var cost = meta.Branches.SelectMany(b => b.Nodes).ToDictionary(n => n.Id, n => n.Cost, StringComparer.Ordinal);
        return p.PurchasedNodes.Distinct(StringComparer.Ordinal).Sum(id => cost.TryGetValue(id, out var c) ? c : 0);
    }

    public static int SealsAvailable(TdProfileData p, TdMetaDef meta) => Math.Max(0, p.SealsEarned - SealsSpent(p, meta));

    /// <summary>
    /// Registra o resultado de uma fase: estrelas só sobem; selos = <c>SealsPerStar</c> por estrela nova + <c>SealsPerChallenge</c>
    /// pelo desafio na primeira conclusão. Desbloqueia a fase seguinte (se existir em <paramref name="stageCount"/>).
    /// </summary>
    public static TdProfileData RecordStageResult(TdProfileData p, TdMetaDef meta, int stageId, int stars, bool challenge, int stageCount)
    {
        stars = Math.Clamp(stars, 0, 3);
        var stages = p.Stages.ToList();
        var i = stages.FindIndex(s => s.StageId == stageId);
        var cur = i >= 0 ? stages[i] : new TdStageProgressData { StageId = stageId, Unlocked = true };
        var newStars = Math.Max(0, stars - cur.Stars);
        var newChallenge = challenge && !cur.ChallengeCompleted;
        var upd = cur with { Unlocked = true, Stars = Math.Max(cur.Stars, stars), ChallengeCompleted = cur.ChallengeCompleted || challenge };
        if (i >= 0) stages[i] = upd; else stages.Add(upd);
        if (stars >= 1 && stageId + 1 <= stageCount && !stages.Exists(s => s.StageId == stageId + 1))
            stages.Add(new TdStageProgressData { StageId = stageId + 1, Unlocked = true });
        else if (stars >= 1)
        {
            var n = stages.FindIndex(s => s.StageId == stageId + 1);
            if (n >= 0) stages[n] = stages[n] with { Unlocked = true };
        }
        stages.Sort((a, b) => a.StageId.CompareTo(b.StageId));
        return p with
        {
            Stages = stages,
            SealsEarned = p.SealsEarned + newStars * meta.SealsPerStar + (newChallenge ? meta.SealsPerChallenge : 0),
        };
    }

    /// <summary>Compra um nó se houver selos e ainda não foi comprado; devolve o mesmo perfil se não puder.</summary>
    public static TdProfileData TryPurchase(TdProfileData p, TdMetaDef meta, string nodeId)
    {
        var node = meta.Branches.SelectMany(b => b.Nodes).FirstOrDefault(n => n.Id == nodeId);
        if (node is null || p.PurchasedNodes.Contains(nodeId, StringComparer.Ordinal)) return p;
        if (SealsAvailable(p, meta) < node.Cost) return p;
        return p with { PurchasedNodes = [.. p.PurchasedNodes, nodeId] };
    }
}
