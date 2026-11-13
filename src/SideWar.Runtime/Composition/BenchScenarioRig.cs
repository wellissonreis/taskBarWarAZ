using SideWar.Runtime.Diagnostics;
using SideWar.Runtime.Hosting;
using SideWar.Runtime.Timing;

namespace SideWar.Runtime.Composition;

/// <summary>
/// Parte pura da montagem de um cenário de jogo do <c>--bench</c> (SDD-16 RF-16.2, T4.11): <see cref="GameRuntime"/>
/// (partida nova com a faixa Band padrão, <c>towersInBar = false</c>, sem sessão do usuário), <see cref="BenchSimDriver"/>
/// (SanityBot, onda sintética infinita, presença forçada) e o <see cref="CompositeLoopHost"/>. A plataforma (janelas)
/// entra por <see cref="GameRuntimeOptions.FrameSink"/> e pelos participantes que o App acrescenta. Só na T-Game.
/// </summary>
public sealed class BenchScenarioRig
{
    public const ulong BenchSeed = 0xB3_4C_11UL;

    private BenchScenarioRig(BenchScenario scenario, GameRuntime runtime, BenchSimDriver driver, CompositeLoopHost loop)
    {
        Scenario = scenario;
        Runtime = runtime;
        Driver = driver;
        Loop = loop;
    }

    public BenchScenario Scenario { get; }
    public GameRuntime Runtime { get; }
    public BenchSimDriver Driver { get; }
    public CompositeLoopHost Loop { get; }

    /// <summary>Monta o grafo lógico; nada é iniciado (chamar <see cref="Start"/> depois de ligar os participantes).</summary>
    public static BenchScenarioRig Create(BenchScenario scenario, IClock clock, IGameFrameSink? frameSink, Action<Exception>? report = null,
        int activeFps = LoopConstants.DefaultActiveFps, int absentFps = LoopConstants.AmbientFps)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(clock);
        var runtime = new GameRuntime(new GameRuntimeOptions
        {
            Clock = clock,
            Seed = BenchSeed,
            ActiveFps = activeFps,
            AbsentFps = absentFps,
            AutoScale = false, // o bench mede a simulação, não o gasto automático
            FrameSink = frameSink,
            UtcNow = () => DateTime.UtcNow,
        });
        var driver = new BenchSimDriver(runtime.State, runtime.Context, scenario, runtime.Presence, runtime.NotifyInteraction);
        var loop = new CompositeLoopHost(runtime.Host, report);
        loop.AddBefore(driver);
        loop.AddBefore(runtime); // ReleaseCaches (nunca dispara no bench: nada oculta)
        return new BenchScenarioRig(scenario, runtime, driver, loop);
    }

    /// <summary>Primeiro frame pronto: remove <c>Starting</c> (Show). Presente/Ausente é forçado pelo driver no laço.</summary>
    public void Start() => Runtime.CompleteStartup();
}
