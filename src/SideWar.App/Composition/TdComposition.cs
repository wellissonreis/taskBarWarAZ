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
    private TdComposition(TdGameHost host, TdProfileRecorder recorder)
    {
        Host = host;
        Recorder = recorder;
    }

    public TdGameHost Host { get; }
    public TdProfileRecorder Recorder { get; }

    public static TdComposition Create(string dataDirectory, string version, AppStateMachine app, IClock clock, AppSettings settings,
        Action<string> log, IFileSystem? fileSystem = null, ulong? seed = null, ITdInputSource? input = null)
    {
        var fs = fileSystem ?? RealFileSystem.Instance;
        var content = TdContentLoader.LoadEmbedded();
        var tdStore = new TdProfileStore(fs, dataDirectory, version, log: log);
        var idle = new ProfileStore(fs, dataDirectory, version, log: log);
        var recorder = TdProfileRecorder.Load(tdStore, () => idle.Load(), content.Meta, content.Stages.Stages.Length, log);
        var rng = Rng.Seed(seed ?? (ulong)DateTime.UtcNow.Ticks, 54UL);
        var match = TdMatchState.Create(content, recorder.SuggestedStage(), rng, recorder.Profile.PurchasedNodes);
        var host = new TdGameHost(clock, app, match, recorder, input ?? new TdLastInputSource());
        var td = new TdComposition(host, recorder);
        td.ApplySettings(settings);
        return td;
    }

    /// <summary>Auto-início: padrão desligado; segundos 5 a 60.</summary>
    public void ApplySettings(AppSettings settings) => Host.ApplyAutoStart(settings.Td.AutoStartEnabled, settings.Td.AutoStartSeconds);
}
