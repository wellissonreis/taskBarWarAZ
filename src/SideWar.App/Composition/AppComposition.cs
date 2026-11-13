using System.IO;
using System.Windows;
using SideWar.App.Windows;
using SideWar.Persistence.IO;
using SideWar.Persistence.Settings;
using SideWar.Platform.Windows.Diagnostics;
using SideWar.Platform.Windows.Instance;
using SideWar.Platform.Windows.Menus;
using SideWar.Platform.Windows.Threading;
using SideWar.Platform.Windows.Tray;
using SideWar.Runtime.Composition;
using SideWar.Runtime.Lifecycle;

namespace SideWar.App.Composition;

/// <summary>
/// Raiz de composição do App de produção (T4.10; T7.6 D1). Única no processo e dona da ordem de vida (SDD-07):
/// <list type="number">
/// <item>T-UI: configurações → persistência (restaura a partida) → runtime puro (<see cref="GameRuntime"/>) → laço
/// composto → T-Game (<see cref="GameThread"/>) → serviços da T-Game (<see cref="GameComposition.Setup"/>) → bandeja →
/// ganchos do crash → escuta da instância única.</item>
/// <item>Encerramento pelo <see cref="ShutdownCoordinator"/> (RF-07.10): marca, salva síncrono na T-Game (2 s), para a
/// T-Game (o host encerra os recursos em ordem reversa), fecha a T-UI (janelas WPF, bandeja, mutex) e sai.</item>
/// </list>
/// Todos os membros públicos rodam na T-UI, exceto os callbacks marcados.
/// </summary>
internal sealed class AppComposition : IDisposable
{
    private readonly Application app;
    private readonly SingleInstance? instance;
    private readonly string dataDirectory;
    private readonly Action<string> log;
    private readonly LifecycleSequence uiResources = new();
    private SettingsStore settingsStore = null!;
    private AppSettings settings = AppSettings.Default;
    private SessionPersistence persistence = null!;
    private GameRuntime runtime = null!;
    private CompositeLoopHost loop = null!;
    private GameComposition gameSide = null!;
    private GameThread? game;
    private TrayIcon? tray;
    private ShutdownCoordinator? shutdown;
    private SettingsWindow? settingsWindow;
    private AboutWindow? aboutWindow;
    private CancellationTokenSource? tooltipWait;

    public AppComposition(Application app, SingleInstance? instance, string dataDirectory, Action<string> log)
    {
        this.app = app;
        this.instance = instance;
        this.dataDirectory = dataDirectory;
        this.log = log;
    }

    public bool IsShuttingDown => shutdown?.IsShuttingDown == true;

    /// <summary>Monta e inicia tudo. Em falha, desfaz o que subiu e relança (o App sai com erro).</summary>
    public async Task StartAsync()
    {
        string version = DiagCommand.Version(typeof(AppComposition).Assembly);
        Directory.CreateDirectory(dataDirectory);

        settingsStore = new SettingsStore(RealFileSystem.Instance, dataDirectory, version);
        settings = settingsStore.Load().Settings;
        persistence = new SessionPersistence(dataDirectory, version, log);

        var sink = new SceneFrameSink(() => gameSide.Animation, () => gameSide.Edge, () => gameSide.BarHeightDip);
        runtime = new GameRuntime(new GameRuntimeOptions
        {
            Clock = new Bench.StopwatchClock(),
            Restore = persistence.Restore,
            ActiveFps = settings.Performance.PresentFps,
            AbsentFps = settings.Performance.AbsentFps,
            ReducedMotion = settings.Accessibility.ReducedMotion,
            AutoScale = settings.AutoScale,
            FrameSink = sink,
        });
        loop = new CompositeLoopHost(runtime.Host, ReportGameError);
        gameSide = new GameComposition(runtime, loop, sink, persistence, settings, log, ReportGameError, PostUi);

        game = new GameThread(loop, ReportGameError, log);
        await game.StartAsync();
        // RF-03.8/RF-07.12: falha de item é registrada e o laço segue; falha fatal (laço morto) vira crash.
        _ = game.Completion.ContinueWith(t =>
        {
            if (!IsShuttingDown && t.Exception is { } fatal) CrashHandler.Fatal("GameThread", fatal.GetBaseException());
        }, TaskScheduler.Default);
        await InvokeOnGame(() => gameSide.Setup(game));

        tray = new TrayIcon("SideWar");
        tray.MenuStateProvider = TrayMenuState;
        tray.CommandSelected += OnTrayCommand;
        tray.ToggleVisibilityRequested += () => PostGame(new AppCommand(AppCommandKind.ToggleVisibility));
        uiResources.Started("tray", () => tray.Dispose()); // NIM_DELETE
        // T7.6 D5: o tooltip retido pelo limite de 1 Hz é aplicado por uma espera one-shot da T-UI (sem timer periódico).
        tray.TooltipDeadlineChanged += ArmTooltip;
        uiResources.Started("tray-tooltip", () =>
        {
            tray.TooltipDeadlineChanged -= ArmTooltip;
            tooltipWait?.Cancel();
            tooltipWait?.Dispose();
            tooltipWait = null;
        });

        CrashHandler.CaptureSession = () => persistence.LastSessionBytes;
        CrashHandler.RemoveTrayIcon = () => tray?.Dispose();
        uiResources.Started("crash-hooks", () => { CrashHandler.CaptureSession = null; CrashHandler.RemoveTrayIcon = null; });

        // RF-07.8: a segunda instância sinaliza; a primeira mostra e entra em Interativo.
        instance?.StartListening(() => PostGame(new AppCommand(AppCommandKind.ShowAndInteract)));

        app.SessionEnding += OnSessionEnding;
        uiResources.Started("session-ending", () => app.SessionEnding -= OnSessionEnding);
        log("startup ok");
    }

    /// <summary>"Sair" (menu/bandeja): encerramento normal RF-07.10 fora da T-UI (o watchdog pode forçar a saída).</summary>
    public void RequestExit()
    {
        if (IsShuttingDown) return;
        shutdown ??= new ShutdownCoordinator(new ShutdownSteps
        {
            MarkShuttingDown = () => { },
            SaveSession = ct => SaveOnGame(ct),
            StopGameThread = () => game?.StopAsync() ?? Task.CompletedTask,
            CloseUi = () => app.Dispatcher.Invoke(CloseUi),
            Log = log,
            Exit = code => Environment.Exit(code),
        });
        var coordinator = shutdown;
        _ = Task.Run(() =>
        {
            var result = coordinator.Run();
            if (result.Failures.Count > 0) log("shutdown failures: " + string.Join("; ", result.Failures));
        });
    }

    /// <summary>Para os recursos da T-UI (bandeja, ganchos) em ordem reversa; a T-Game para pelo <see cref="RequestExit"/>.</summary>
    public void Dispose()
    {
        foreach (var failure in uiResources.StopAll()) log(failure.ToString());
    }

    // ---------------------------------------------------------------- T-UI

    private void CloseUi()
    {
        settingsWindow?.Close();
        aboutWindow?.Close();
        foreach (var failure in uiResources.StopAll()) log(failure.ToString());
        instance?.Dispose(); // libera o mutex antes do Shutdown (RF-07.10 passo 4)
        app.Shutdown(0);
    }

    private void OnSessionEnding(object? sender, SessionEndingCancelEventArgs e)
    {
        // RF-07.11: logoff/desligamento — salva (até 1,5 s) sem cancelar o fim de sessão.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
        try { SaveOnGame(cts.Token); }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException) { log("session ending save: " + ex.Message); }
    }

    private MenuState TrayMenuState()
    {
        var m = gameSide.Menu;
        return new MenuState(m.Interactive, m.Paused, m.Hidden, m.Monitors, m.SelectedMonitor);
    }

    private void OnTrayCommand(MenuCommand command, nint previousForeground)
    {
        switch (command.Kind)
        {
            case MenuCommandKind.ToggleInteractive: PostGame(new AppCommand(AppCommandKind.ToggleInteractive)); break;
            case MenuCommandKind.TogglePause: PostGame(new AppCommand(AppCommandKind.TogglePause)); break;
            case MenuCommandKind.ToggleVisibility: PostGame(new AppCommand(AppCommandKind.ToggleVisibility)); break;
            case MenuCommandKind.SelectMonitor: PostGame(new AppCommand(AppCommandKind.SelectMonitor, command.MonitorIndex)); break;
            case MenuCommandKind.OpenSettings: OpenSettings(previousForeground); break;
            case MenuCommandKind.OpenAbout: OpenAbout(previousForeground); break;
            case MenuCommandKind.Exit: RequestExit(); break;
        }
    }

    /// <summary>Pedido da T-Game (menu do widget): executa na T-UI.</summary>
    private void PostUi(UiRequest request) => app.Dispatcher.BeginInvoke(() =>
    {
        nint previous = ForegroundRestorer.Capture();
        switch (request)
        {
            case UiRequest.OpenSettings: OpenSettings(previous); break;
            case UiRequest.OpenAbout: OpenAbout(previous); break;
            case UiRequest.Exit: RequestExit(); break;
        }
    });

    // T5.9: uma instância por vez, só por ação do usuário; ao fechar devolve o foco anterior.
    private void OpenSettings(nint previousForeground)
    {
        if (IsShuttingDown) return;
        if (settingsWindow is not null) { settingsWindow.Activate(); return; }
        var vm = new SettingsViewModel(settings, SaveSettings);
        settingsWindow = new SettingsWindow(vm, () =>
        {
            settingsWindow = null;
            ForegroundRestorer.Restore(previousForeground);
        });
        settingsWindow.Show();
    }

    private void OpenAbout(nint previousForeground)
    {
        if (IsShuttingDown) return;
        if (aboutWindow is not null) { aboutWindow.Activate(); return; }
        aboutWindow = new AboutWindow(AboutInfo.Create(dataDirectory), () =>
        {
            aboutWindow = null;
            ForegroundRestorer.Restore(previousForeground);
        });
        aboutWindow.Show();
    }

    private bool SaveSettings(AppSettings value)
    {
        if (!settingsStore.Save(value)) return false;
        settings = value;
        try { game?.Post(() => gameSide.ApplySettings(value)); }
        catch (InvalidOperationException) { } // encerrando
        return true;
    }

    // ---------------------------------------------------------------- pontes T-UI ⇄ T-Game

    /// <summary>Arma (ou cancela) a espera única até o deadline do tooltip; ao vencer, chama <c>FlushTooltip</c> na T-UI.</summary>
    private void ArmTooltip(long? deadline)
    {
        app.Dispatcher.BeginInvoke(() =>
        {
            tooltipWait?.Cancel();
            tooltipWait?.Dispose();
            tooltipWait = null;
            if (deadline is not { } due || tray is null) return;
            var cts = new CancellationTokenSource();
            tooltipWait = cts;
            var delay = TimeSpan.FromSeconds(Math.Max(0, (due - System.Diagnostics.Stopwatch.GetTimestamp()) / (double)System.Diagnostics.Stopwatch.Frequency));
            _ = Task.Delay(delay, cts.Token).ContinueWith(t =>
            {
                if (!t.IsCanceled) app.Dispatcher.BeginInvoke(() => tray?.FlushTooltip());
            }, TaskScheduler.Default);
        });
    }

    private void PostGame(AppCommand command)
    {
        try { game?.Post(command); }
        catch (InvalidOperationException) { } // T-Game encerrando
    }

    private bool SaveOnGame(CancellationToken ct)
    {
        if (game is null) return false;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        game.Post(() =>
        {
            try { done.TrySetResult(gameSide.SaveForShutdown()); }
            catch (Exception e) { done.TrySetException(e); }
        });
        return done.Task.Wait(Timeout.Infinite, ct) && done.Task.Result;
    }

    private Task InvokeOnGame(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        game!.Post(() =>
        {
            try { action(); done.SetResult(); }
            catch (Exception e) { done.SetException(e); }
        });
        return done.Task;
    }

    private void ReportGameError(Exception error) => log("T-Game: " + error);
}
