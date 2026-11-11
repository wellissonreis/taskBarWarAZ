using System;
using System.IO;
using SideWar.Platform.Windows.Instance;
using SideWar.Platform.Windows.Taskbar;

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
        using var instance = SingleInstance.TryAcquire(); // SDD-07 RF-07.8: second instance signals the first and exits 0.
        if (instance is null) return 0;
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
