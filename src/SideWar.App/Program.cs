using System;
using System.IO;
using System.Reflection;
using System.Windows;
using SideWar.App.Bench;
using SideWar.Platform.Windows.Diagnostics;
using SideWar.Platform.Windows.Instance;
using SideWar.Platform.Windows.Taskbar;
using SideWar.Runtime.Diagnostics;

namespace SideWar.App;

/// <summary>Explicit entry point so command-line diagnostics run without creating WPF or any window (SDD-21 RF-21.9).</summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (TaskbarDumpCommand.IsRequested(args))
        {
            TaskbarDumpCommand.AttachParentConsole();
            using var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            return TaskbarDumpCommand.Run(args, stdout);
        }
        if (BenchOptions.IsVersionRequested(args) || BenchOptions.IsDiagRequested(args))
            return RunCli(args);
        if (BenchOptions.IsRequested(args))
            return RunBench(args);
        using var instance = SingleInstance.TryAcquire(); // SDD-07 RF-07.8: second instance signals the first and exits 0.
        if (instance is null) return 0;
        var app = new App();
        CrashHandler.Install(app);
#if DEBUG
        if (Array.IndexOf(args, "--debug-crash") >= 0) CrashHandler.Fatal("--debug-crash", new InvalidOperationException("forced crash (--debug-crash)"));
#endif
        app.InitializeComponent();
        return app.Run();
    }

    /// <summary>`--version` / `--diag` (RF-16.2): no WPF, no windows.</summary>
    private static int RunCli(string[] args)
    {
        TaskbarDumpCommand.AttachParentConsole();
        using var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        var assembly = typeof(Program).Assembly;
        if (BenchOptions.IsVersionRequested(args)) stdout.WriteLine(DiagCommand.Version(assembly));
        else DiagCommand.WriteDiag(stdout, assembly, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SideWar"));
        return 0;
    }

    /// <summary>
    /// `--bench` (RF-16.2). WPF is loaded and pumping but owns no window, so every scenario shares the P-00 base
    /// (`baseline-wpf` = this and nothing else).
    /// </summary>
    private static int RunBench(string[] args)
    {
        TaskbarDumpCommand.AttachParentConsole();
        using var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        var (options, error) = BenchOptions.Parse(args);
        if (options is null) { stdout.WriteLine(error); return 1; }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exit = 0;
        app.Dispatcher.InvokeAsync(async () =>
        {
            try { exit = await BenchRunner.RunAsync(options, stdout, withTray: true); }
            catch (Exception e) { stdout.WriteLine(e); exit = 3; }
            finally { app.Shutdown(); }
        });
        app.Run();
        return exit;
    }
}
