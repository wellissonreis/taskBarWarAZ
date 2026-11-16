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

    /// <summary>Patente local pela soma dos recordes (RF-28.21).</summary>
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

    public void OnEvents(TdMatchState match, IReadOnlyList<TdEvent> events) { }

    public void OnMatchEnded(TdMatchState match, TdMatchResult result)
    {
        Profile = TdProfileRules.RecordMatch(Profile, _meta, match.StageId, result, _stageCount, out var outcome);
        LastOutcome = outcome;
        if (!_store.Save(Profile))
        {
            SaveFailures++;
            _log("falha ao gravar profile-td.json");
        }
    }
}
