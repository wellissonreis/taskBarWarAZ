using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Persistence.IO;
using SideWar.Persistence.Profile;
using SideWar.Persistence.Settings;
using SideWar.Runtime;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Td;

namespace SideWar.App.Composition;

/// <summary>
/// Monta o host da partida TD (TD.6) atrás de <c>gameMode = td</c>: conteúdo embutido, perfil TD (com importação única do idle),
/// partida na fase sugerida e <see cref="TdGameHost"/> usando a mesma <see cref="AppStateMachine"/> da janela (ocultar/fullscreen/
/// sessão bloqueada pausam a simulação). O render TD chega na TD.7; até lá o idle continua sendo o jogo padrão.
/// </summary>
internal sealed class TdComposition
{
    private readonly TdContent _content;
    private TdMatchOptions _options = TdMatchOptions.Default;
    private ulong _runSeed;

    private TdComposition(TdGameHost host, TdProfileRecorder recorder, TdContent content)
    {
        _content = content;
        Host = host;
        Recorder = recorder;
    }

    public TdGameHost Host { get; }
    public TdProfileRecorder Recorder { get; }

    /// <summary>TD.28b: fase em que a corrida atual começou (Reiniciar volta para ela; o Mapa troca).</summary>
    public int RunStartStage { get; private set; } = 1;

    /// <summary>Regras das próximas corridas (<c>td.continuousWaves</c>: ligado = <see cref="TdMatchOptions.Default"/>, desligado = <see cref="TdMatchOptions.Classic"/>).</summary>
    public TdMatchOptions Options => _options;

    /// <summary>TD.28b: script de replay da corrida atual com as <c>Options</c> persistidas (fase inicial, seed, log de comandos e frame atual).</summary>
    public TdReplayScript BuildReplay() => Host.BuildReplay(RunStartStage, _runSeed, 54UL);

    public static TdComposition Create(string dataDirectory, string version, AppStateMachine app, IClock clock, AppSettings settings,
        Action<string> log, IFileSystem? fileSystem = null, ulong? seed = null, ITdPresence? presence = null)
    {
        var fs = fileSystem ?? RealFileSystem.Instance;
        var content = TdContentLoader.LoadEmbedded();
        var tdStore = new TdProfileStore(fs, dataDirectory, version, log: log);
        var idle = new ProfileStore(fs, dataDirectory, version, log: log);
        var recorder = TdProfileRecorder.Load(tdStore, () => idle.Load(), content.Meta, content.Stages.Stages.Length, log);
        ulong runSeed = seed ?? (ulong)DateTime.UtcNow.Ticks;
        var rng = Rng.Seed(runSeed, 54UL);
        var options = settings.Td.ContinuousWaves ? TdMatchOptions.Default : TdMatchOptions.Classic;
        int stage = recorder.ResumeStage();
        options = recorder.WithRunes(options) with { HighestCompletedStage = recorder.Profile.Stages.Where(s => s.Stars > 0).Select(s => s.StageId).DefaultIfEmpty(0).Max() };
        var match = TdMatchState.Create(content, stage, rng, recorder.Profile.PurchasedNodes, options);
        bool restored = match.ImportTowers(recorder.RunTowers) > 0; // reabrir o app nunca apaga as torres
        var host = new TdGameHost(clock, app, match, recorder, presence);
        if (restored) host.AutoPlaceOnNextLayout();
        var td = new TdComposition(host, recorder, content) { _options = options, _runSeed = runSeed, RunStartStage = stage };
        td.ApplySettings(settings);
        return td;
    }

    public int StageCount => _content.Stages.Stages.Length;

    /// <summary>Cria uma corrida nova começando na fase (nós comprados incluídos) e a entrega ao host (reaplica o layout da barra).</summary>
    public void StartStage(int stageId, bool keepTowers = true)
    {
        stageId = Math.Clamp(stageId, 1, TdProfileSerializer.MaxWave); // número absoluto: > StageCount = ciclos do infinito
        int index = (stageId - 1) % StageCount;
        if (!TdStageGate.Allowed(_content.Stages.Stages[index], Recorder.Rank.Current))
            stageId = (stageId - 1) / StageCount * StageCount + TdStageGate.LastAllowed(_content.Stages.Stages, Recorder.Rank.Current);
        Recorder.SetRunStage(stageId);
        _runSeed = (ulong)DateTime.UtcNow.Ticks;
        RunStartStage = stageId;
        _options = Recorder.WithRunes(_options) with { HighestCompletedStage = Recorder.Profile.Stages.Where(s => s.Stars > 0).Select(s => s.StageId).DefaultIfEmpty(0).Max() };
        var next = TdMatchState.Create(_content, stageId, Rng.Seed(_runSeed, 54UL), Recorder.Profile.PurchasedNodes, _options);
        // Trocar de fase, reiniciar a corrida e voltar por trava NUNCA apagam as torres: elas viajam com todo o estado (só "Recomeçar do zero" as limpa).
        bool carried = keepTowers && next.ImportTowers(Host.Match.ExportTowers()) > 0;
        Host.Restart(next, carried);
        Recorder.SaveTowers(next);
    }

    /// <summary>"Recomeçar do zero": perfil zerado e gravado, depois corrida nova na fase 1 (runas e nós já vêm do perfil vazio).</summary>
    public void ResetProfile()
    {
        Recorder.ResetProgress();
        StartStage(1, keepTowers: false);
    }

    /// <summary>Auto-início (padrão desligado; 5 a 60 s) e ondas contínuas (valem a partir da próxima corrida).</summary>
    public void ApplySettings(AppSettings settings)
    {
        _options = settings.Td.ContinuousWaves ? TdMatchOptions.Default : TdMatchOptions.Classic;
        Host.ExtraSpeeds = settings.Td.ExtraSpeeds;
        Host.GameSpeed = settings.Td.GameSpeed;
        Host.Alerts.Settings.Enabled = settings.Td.AlertsEnabled;
        Host.Alerts.Settings.AutoPause = settings.Td.AlertsAutoPause;
        Host.Alerts.Settings.Sound = settings.Td.AlertsSound;
        Host.Alerts.Settings.MutedMask = settings.Td.AlertsMutedMask;
        Host.ApplyAutoStart(settings.Td.AutoStartEnabled, settings.Td.AutoStartSeconds);
    }
}
