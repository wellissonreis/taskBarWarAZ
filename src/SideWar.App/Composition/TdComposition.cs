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
        int stage = recorder.SuggestedStage();
        options = options with { LegacyRunes = options.PersistentRun ? recorder.StartingRunes : 0, HighestCompletedStage = recorder.Profile.Stages.Where(s => s.Stars > 0).Select(s => s.StageId).DefaultIfEmpty(0).Max() };
        var match = TdMatchState.Create(content, stage, rng, recorder.Profile.PurchasedNodes, options);
        var host = new TdGameHost(clock, app, match, recorder, presence);
        var td = new TdComposition(host, recorder, content) { _options = options, _runSeed = runSeed, RunStartStage = stage };
        td.ApplySettings(settings);
        return td;
    }

    public int StageCount => _content.Stages.Stages.Length;

    /// <summary>Cria uma corrida nova começando na fase (nós comprados incluídos) e a entrega ao host (reaplica o layout da barra).</summary>
    public void StartStage(int stageId)
    {
        stageId = Math.Clamp(stageId, 1, StageCount);
        _runSeed = (ulong)DateTime.UtcNow.Ticks;
        RunStartStage = stageId;
        _options = _options with { LegacyRunes = _options.PersistentRun ? Recorder.StartingRunes : 0, HighestCompletedStage = Recorder.Profile.Stages.Where(s => s.Stars > 0).Select(s => s.StageId).DefaultIfEmpty(0).Max() };
        Host.Restart(TdMatchState.Create(_content, stageId, Rng.Seed(_runSeed, 54UL), Recorder.Profile.PurchasedNodes, _options));
    }

    /// <summary>Auto-início (padrão desligado; 5 a 60 s) e ondas contínuas (valem a partir da próxima corrida).</summary>
    public void ApplySettings(AppSettings settings)
    {
        _options = settings.Td.ContinuousWaves ? TdMatchOptions.Default : TdMatchOptions.Classic;
        Host.GameSpeed = settings.Td.GameSpeed;
        Host.ApplyAutoStart(settings.Td.AutoStartEnabled, settings.Td.AutoStartSeconds);
    }
}
