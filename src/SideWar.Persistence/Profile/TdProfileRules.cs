using SideWar.Core.Content.Td;
using SideWar.Core.Td;

namespace SideWar.Persistence.Profile;

/// <summary>Regras de selos do perfil v3 (RF-28.13), derivadas do conteúdo <c>td-meta.json</c>.</summary>
public static class TdProfileRules
{
    /// <summary>Aplica cada encontro emitido pelo Core, incluindo variações independentes.</summary>
    public static TdProfileData RecordEncounters(TdProfileData profile, TdMatchState match, IReadOnlyList<TdEvent> events)
    {
        var encounters = profile.Encounters.ToDictionary(e => e.Id, e => e.Count, StringComparer.Ordinal);
        bool changed = false;
        foreach (var e in events)
        {
            if (e.Type != TdEventType.EnemyEncountered) continue;
            var id = match.EnemyDefId((int)e.A) + (e.B == 1 ? ":elite" : e.B == 2 ? ":enraged" : "");
            encounters.TryGetValue(id, out var count);
            encounters[id] = (int)Math.Min(TdProfileSerializer.MaxScore, (long)count + 1);
            changed = true;
        }
        return !changed ? profile : profile with
        {
            Encounters = encounters.Select(e => new TdEncounterData { Id = e.Key, Count = e.Value }).ToArray(),
        };
    }

    public static TdProfileData RecordLegacy(TdProfileData profile, string runId, int waves)
    {
        ArgumentException.ThrowIfNullOrEmpty(runId);
        if (profile.LegacyLastRun == runId) return profile;
        return profile with
        {
            LegacyTotal = (int)Math.Min(TdLegacy.MaxTotal, (long)Math.Max(0, profile.LegacyTotal) + TdLegacy.Fragments(waves)),
            BestRunWaves = Math.Max(profile.BestRunWaves, waves),
            LegacyLastRun = runId,
            RunsPlayed = profile.RunsPlayed == int.MaxValue ? int.MaxValue : Math.Max(0, profile.RunsPlayed) + 1,
        };
    }

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

    /// <summary>Selos por fase concluída nos ciclos 2, 3, … (a primeira volta paga por estrelas/coroas/medalhas; depois cada fase de ciclo paga 1 selo).</summary>
    public const int SealsPerCycleStage = 1;

    /// <summary>
    /// Corrida infinita: recorde (fase absoluta, onda) só sobe. Comparação lexicográfica: fase maior vence; na mesma fase, onda maior.
    /// </summary>
    public static TdProfileData RecordBest(TdProfileData p, int stageNumber, int wave)
    {
        stageNumber = Math.Clamp(stageNumber, 0, TdProfileSerializer.MaxWave);
        wave = Math.Clamp(wave, 0, TdProfileSerializer.MaxWave);
        if (stageNumber < p.BestStageNumber || (stageNumber == p.BestStageNumber && wave <= p.BestStageWave)) return p;
        return p with { BestStageNumber = stageNumber, BestStageWave = wave };
    }

    /// <summary>Corrida infinita: guarda a fase absoluta em que a corrida está (a próxima abertura recomeça nela). 0 apaga.</summary>
    public static TdProfileData SetRunStage(TdProfileData p, int stageNumber)
    {
        stageNumber = Math.Clamp(stageNumber, 0, TdProfileSerializer.MaxWave);
        return stageNumber == p.RunStageNumber ? p : p with { RunStageNumber = stageNumber };
    }

    /// <summary>Guarda as torres da corrida (null/vazio = nenhuma). Devolve o mesmo perfil se nada mudou.</summary>
    public static TdProfileData SetRunTowers(TdProfileData p, IReadOnlyList<TdRunTowerData> towers) =>
        p.RunTowers.SequenceEqual(towers) ? p : p with { RunTowers = towers };

    /// <summary>Em sombra: o selo de fase de ciclo vale só <c>TdShadow.RewardPercent</c>%; o resto acumula (0 a 99). Devolve os selos inteiros ganhos.</summary>
    public static TdProfileData RecordShadowCycleStage(TdProfileData p, int cycle, out int seals)
    {
        seals = 0;
        if (cycle < 1) return p;
        int cents = p.ShadowSealCents + (int)TdShadow.Scale(100L * SealsPerCycleStage);
        seals = cents / 100;
        return p with { ShadowSealCents = cents % 100, SealsEarned = (int)Math.Min(int.MaxValue, (long)p.SealsEarned + seals) };
    }

    /// <summary>Fase de conteúdo concluída em ciclo 2+ (<paramref name="cycle"/> &gt;= 1): +<see cref="SealsPerCycleStage"/> selo (a recompensa continua depois da fase 6).</summary>
    public static TdProfileData RecordCycleStage(TdProfileData p, int cycle) =>
        cycle < 1 ? p : p with { SealsEarned = (int)Math.Min(int.MaxValue, (long)p.SealsEarned + SealsPerCycleStage) };

    /// <summary>Selos bônus por coroa (primeira vez, RF-28.21).</summary>
    public const int SealsPerCrown = 2;
    /// <summary>Selos bônus por medalha de feito (primeira vez). A medalha Desafio paga <c>SealsPerChallenge</c>.</summary>
    public const int SealsPerMedal = 1;

    /// <summary>Soma dos recordes de pontuação por fase: define a patente (<see cref="TdRank"/>).</summary>
    public static int TotalScore(TdProfileData p) => (int)Math.Min(int.MaxValue, p.Stages.Sum(s => (long)Math.Max(0, s.BestScore)));

    /// <summary>A patente vem da reputação (XP persistido), não mais da soma dos recordes: sobe e desce.</summary>
    public static TdRankProgress Rank(TdProfileData p) => TdRank.Progress(p.Reputation);

    /// <summary>Soma <paramref name="delta"/> (positivo ganha, negativo perde) à reputação, limitada a [0, <see cref="TdProfileSerializer.MaxScore"/>].</summary>
    public static TdProfileData ApplyReputation(TdProfileData p, long delta)
    {
        long next = Math.Clamp(Math.Max(0, (long)p.Reputation) + delta, 0, TdProfileSerializer.MaxScore);
        return next == p.Reputation ? p : p with { Reputation = (int)next };
    }

    /// <summary>
    /// Registra o resultado completo de uma partida (RF-28.21): estrelas/selos como em <see cref="RecordStageResult"/>, mais coroa
    /// (+<see cref="SealsPerCrown"/>) e cada medalha nova (+<see cref="SealsPerMedal"/>, Desafio +<c>SealsPerChallenge</c>), só na primeira vez,
    /// e o recorde de pontuação (só sobe). Derrota não altera coroa, medalhas nem recorde.
    /// </summary>
    public static TdProfileData RecordMatch(TdProfileData p, TdMetaDef meta, int stageId, TdMatchResult r, int stageCount, out TdRecordOutcome outcome)
    {
        bool win = r.Phase == MatchPhase.Vitoria;
        var medals = win ? r.Medals : TdMedals.None;
        bool challenge = medals.HasFlag(TdMedals.Desafio);
        var before = p.Stages.FirstOrDefault(s => s.StageId == stageId) ?? new TdStageProgressData { StageId = stageId, Unlocked = true };
        var next = RecordStageResult(p, meta, stageId, win ? r.Stars : 0, challenge, stageCount);
        int i = next.Stages.ToList().FindIndex(s => s.StageId == stageId);
        var cur = next.Stages[i];

        bool newCrown = win && r.Crown && !before.Crown;
        var newMedals = medals & ~(TdMedals)before.Medals;
        bool newRecord = win && r.Score > before.BestScore;
        int bonus = (newCrown ? SealsPerCrown : 0);
        foreach (var m in new[] { TdMedals.SemVender, TdMedals.SemHabilidades, TdMedals.Pressa })
            if (newMedals.HasFlag(m)) bonus += SealsPerMedal;
        // O selo do Desafio já foi pago por RecordStageResult (challenge && !ChallengeCompleted), sem duplicar aqui.
        var stages = next.Stages.ToList();
        stages[i] = cur with
        {
            Crown = cur.Crown || newCrown,
            Medals = cur.Medals | (int)medals,
            BestScore = newRecord ? r.Score : cur.BestScore,
        };
        outcome = new(newRecord, newCrown, newMedals, bonus);
        return next with { Stages = stages, SealsEarned = next.SealsEarned + bonus };
    }

    /// <summary>Compra em ordem: o nó só abre se o anterior do mesmo ramo já foi comprado (o primeiro de cada ramo está sempre aberto).</summary>
    public static bool PrerequisiteOwned(TdProfileData p, TdMetaDef meta, string nodeId)
    {
        foreach (var b in meta.Branches)
        {
            int i = Array.FindIndex(b.Nodes, n => n.Id == nodeId);
            if (i < 0) continue;
            return i == 0 || p.PurchasedNodes.Contains(b.Nodes[i - 1].Id, StringComparer.Ordinal);
        }
        return false;
    }

    /// <summary>Compra um nó se houver selos, a ordem do ramo permitir e ainda não foi comprado; devolve o mesmo perfil se não puder.</summary>
    public static TdProfileData TryPurchase(TdProfileData p, TdMetaDef meta, string nodeId)
    {
        var node = meta.Branches.SelectMany(b => b.Nodes).FirstOrDefault(n => n.Id == nodeId);
        if (node is null || p.PurchasedNodes.Contains(nodeId, StringComparer.Ordinal)) return p;
        if (!PrerequisiteOwned(p, meta, nodeId)) return p;
        if (SealsAvailable(p, meta) < node.Cost) return p;
        return p with { PurchasedNodes = [.. p.PurchasedNodes, nodeId] };
    }
}
