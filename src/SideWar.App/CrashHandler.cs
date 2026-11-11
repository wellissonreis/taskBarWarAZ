using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SideWar.Persistence.IO;
using SideWar.Runtime.Lifecycle;

namespace SideWar.App;

/// <summary>Wires the SDD-07 RF-07.12 handlers to <see cref="CrashReporter"/>. Composition hooks are set by the host.</summary>
public static class CrashHandler
{
    private static CrashReporter? reporter;

    public static CrashReporter Reporter => reporter ?? throw new InvalidOperationException("CrashHandler.Install not called");

    /// <summary>Serialized session for the emergency save (set by the host composition; null = no session yet).</summary>
    public static Func<byte[]?>? CaptureSession { get; set; }

    /// <summary>Removes the tray icon on fatal exit (set by the host composition).</summary>
    public static Action? RemoveTrayIcon { get; set; }

    public static string DefaultDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SideWar");

    public static void Install(Application? app = null, string? dataDirectory = null)
    {
        var data = dataDirectory ?? DefaultDataDirectory;
        var writer = new AtomicFileWriter(RealFileSystem.Instance);
        reporter = new CrashReporter(new CrashReporterOptions
        {
            LogsDirectory = Path.Combine(data, "logs"),
            DataDirectory = data,
            CaptureSession = () => CaptureSession?.Invoke(),
            WriteAtomic = (path, bytes) => writer.Write(path, bytes),
            RemoveTrayIcon = () => RemoveTrayIcon?.Invoke(),
            Exit = code => Environment.Exit(code),
        });
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            reporter.HandleFatal("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            reporter.LogOnly("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
        if (app is not null)
            app.DispatcherUnhandledException += OnDispatcherUnhandled;
    }

    /// <summary>Reports a fatal failure from a thread of the host (for example the GameThread reportError callback).</summary>
    public static void Fatal(string source, Exception ex) => Reporter.HandleFatal(source, ex);

    private static void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Reporter.HandleFatal("Application.DispatcherUnhandledException", e.Exception);
    }
}
