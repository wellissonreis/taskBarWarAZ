using SideWar.Core.Content.Td;
using SideWar.Core.Td;
using SideWar.Persistence.Migrations;
using SideWar.Persistence.Profile;
using SideWar.Runtime.Td;

namespace SideWar.App.Composition;

/// <summary>
/// Carrega o perfil TD (com a importação única do save idle) e grava o resultado da fase ao vencer/perder (RF-28.10/28.13).
/// Observador do <see cref="TdGameHost"/>; só na T-Game. A gravação é síncrona: o arquivo é pequeno e o evento é raro.
/// </summary>
internal sealed class TdProfileRecorder : ITdMatchObserver
{
    /// <summary>Fórmula escolhida pelo Tech Lead (TD.5/DP-28.4): selos iniciais = min(12, ondaMax / 10).</summary>
    public static int SealsForMaxWave(int maxWave) => Math.Min(12, Math.Max(0, maxWave) / 10);

    private readonly TdProfileStore _store;
    private readonly TdMetaDef _meta;
    private readonly int _stageCount;
    private readonly Action<string> _log;

    public TdProfileRecorder(TdProfileStore store, TdProfileData profile, TdMetaDef meta, int stageCount, Action<string>? log = null)
    {
        _store = store;
        Profile = profile;
        _meta = meta;
        _stageCount = stageCount;
        _log = log ?? (_ => { });
    }

    public TdProfileData Profile { get; private set; }
    public int LegacyTotal => Profile.LegacyTotal;
    public int BestRunWaves => Profile.BestRunWaves;

    /// <summary>Teia de runas permanente do perfil (nós despertados) e fragmentos não gastos: carregados em toda partida/corrida nova.</summary>
    public UInt128 RuneBits => TdRuneWeb.Parse(Profile.RuneWeb);
    public long RuneFragments => Profile.RuneFragments;

    /// <summary>Opções da próxima partida com a teia permanente do perfil (só fora do modo clássico).</summary>
    public TdMatchOptions WithRunes(TdMatchOptions options) =>
        options.PersistentRun ? options with { InitialAwakened = RuneBits, InitialFragments = RuneFragments } : options with { InitialAwakened = default, InitialFragments = 0 };

    private bool _runesDirty;

    /// <summary>Copia a teia e os fragmentos da partida para o perfil (em memória); só em partidas persistentes. </summary>
    private void SyncRunes(TdMatchState match)
    {
        if (!match.Options.PersistentRun) return;
        var web = TdRuneWeb.ToText(match.AwakenedBits);
        long frag = Math.Clamp(match.Fragments, 0, TdRuneWeb.MaxFragments);
        if (web == Profile.RuneWeb && frag == Profile.RuneFragments) return;
        Profile = Profile with { RuneWeb = web, RuneFragments = frag };
        _runesDirty = true;
    }
    public int PreviousBestRunWaves { get; private set; }
    public int PreviousLegacyTotal { get; private set; }

    /// <summary>Quantas gravações falharam (diagnóstico; a partida segue).</summary>
    public int SaveFailures { get; private set; }

    /// <summary>Carrega <c>profile-td.json</c>; na primeira vez importa o <c>profile.json</c> idle (sem alterá-lo).</summary>
    public static TdProfileRecorder Load(TdProfileStore td, Func<ProfileLoadResult?> idleLoad, TdMetaDef meta, int stageCount, Action<string>? log = null)
    {
        var loaded = IdleProfileMigrator.LoadOrMigrate(td, idleLoad, SealsForMaxWave);
        if (loaded.RejectReason is { } reason) (log ?? (_ => { }))($"profile-td.json rejeitado: {reason}");
        return new TdProfileRecorder(td, loaded.Profile, meta, stageCount, log);
    }

    /// <summary>Maior fase desbloqueada ainda sem 3 estrelas (ou a última): a fase sugerida para a partida.</summary>
    public int SuggestedStage()
    {
        var open = Profile.Stages.Where(s => s.Unlocked).OrderBy(s => s.StageId).ToList();
        var next = open.FirstOrDefault(s => s.Stars < 3) ?? open.LastOrDefault();
        return Math.Clamp(next?.StageId ?? 1, 1, _stageCount);
    }

    public bool StageUnlocked(int id) => id == 1 || Profile.Stages.Any(s => s.StageId == id && s.Unlocked);

    public int StageStars(int id) => Profile.Stages.FirstOrDefault(s => s.StageId == id)?.Stars ?? 0;

    public bool StageCrown(int id) => Profile.Stages.FirstOrDefault(s => s.StageId == id)?.Crown ?? false;

    public int StageMedals(int id) => Profile.Stages.FirstOrDefault(s => s.StageId == id)?.Medals ?? 0;

    public int StageBestScore(int id) => Profile.Stages.FirstOrDefault(s => s.StageId == id)?.BestScore ?? 0;

    /// <summary>Patente local pela reputação (XP persistido; sobe e desce).</summary>
    public TdRankProgress Rank => TdProfileRules.Rank(Profile);

    /// <summary>Novidades do último resultado gravado (recorde, coroa, medalhas, selos bônus).</summary>
    public TdRecordOutcome LastOutcome { get; private set; }

    public int SealsAvailable => TdProfileRules.SealsAvailable(Profile, _meta);

    public bool NodePurchased(string id) => Profile.PurchasedNodes.Contains(id, StringComparer.Ordinal);

    /// <summary>Compra um nó da árvore de selos e grava; vale a partir da próxima partida criada.</summary>
    public bool Purchase(string nodeId)
    {
        var next = TdProfileRules.TryPurchase(Profile, _meta, nodeId);
        if (ReferenceEquals(next, Profile)) return false;
        Profile = next;
        if (!_store.Save(Profile)) { SaveFailures++; _log("falha ao gravar profile-td.json"); }
        return true;
    }

    /// <summary>
    /// TD.28b: fase concluída na corrida contínua (<see cref="TdEventType.StageCleared"/>) grava score, coroa, medalhas e recorde por <c>StageId</c>
    /// (1 a 6; o ciclo infinito reaproveita o mesmo registro). A derrota encerra a corrida e continua em <see cref="OnMatchEnded"/>.
    /// </summary>
    public void OnEvents(TdMatchState match, IReadOnlyList<TdEvent> events)
    {
        SyncRunes(match);
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].Type == TdEventType.RuneAwakened && _runesDirty) { Save(); continue; } // despertar: grava já (raro)
            if (events[i].Type == TdEventType.BaseAttacked) { Profile = TdProfileRules.ApplyReputation(Profile, -events[i].A); _repDirty = true; continue; }
            if (events[i].Type == TdEventType.ReputationGained)
            {
                Profile = TdProfileRules.ApplyReputation(Profile, events[i].A);
                _repDirty = true;
                if (events[i].B > 0) Save(); // onda vencida: grava (inclui o XP perdido durante a onda)
                continue;
            }
            if (events[i].Type != TdEventType.StageCleared) continue;
            TdStageSummary? found = null;
            for (int j = match.StageResults.Count - 1; j >= 0; j--)
                if (match.StageResults[j].StageNumber == events[i].A) { found = match.StageResults[j]; break; }
            if (found is not { } sr) continue;
            var result = new TdMatchResult(MatchPhase.Vitoria, sr.Stars, sr.IntegrityLost, sr.Crown, sr.Medals, sr.Score);
            Profile = TdProfileRules.RecordMatch(Profile, _meta, sr.StageId, result, _stageCount, out var outcome);
            LastOutcome = outcome;
            Save();
        }
    }

    private bool _repDirty;

    /// <summary>Grava a reputação pendente (XP perdido sem onda vencida depois); chamado ao fim/abandono da partida.</summary>
    public void FlushReputation()
    {
        if (_repDirty || _runesDirty) Save();
    }

    private void Save()
    {
        _repDirty = false;
        _runesDirty = false;
        if (_store.Save(Profile)) return;
        SaveFailures++;
        _log("falha ao gravar profile-td.json");
    }

    /// <summary>
    /// "Recomeçar do zero": apaga todo o progresso do perfil e grava já. Zera também os pendentes (reputação/runas sujas) e os
    /// valores "anteriores" do card de fim; a partida nova deve ser criada depois, com o perfil zerado.
    /// </summary>
    public void ResetProgress()
    {
        Profile = TdProfileData.ResetToNew();
        PreviousBestRunWaves = 0;
        PreviousLegacyTotal = 0;
        LastOutcome = default;
        Save();
    }

    public void OnMatchEnded(TdMatchState match, TdMatchResult result)
    {
        SyncRunes(match);
        FlushReputation();
        Profile = TdProfileRules.RecordMatch(Profile, _meta, match.StageId, result, _stageCount, out var outcome);
        LastOutcome = outcome;
        Save();
    }

    public void OnMatchAbandoned(TdMatchState match)
    {
        SyncRunes(match);
        FlushReputation();
        if (!match.Options.PersistentRun || match.Phase is not (MatchPhase.Preparo or MatchPhase.Onda) || Profile.LegacyLastRun == match.RunId) return;
        Profile = TdProfileRules.RecordLegacy(Profile, match.RunId, match.WavesReached);
        Save();
    }
}
