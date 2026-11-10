using System.Text;
using System.Text.RegularExpressions;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Taskbar;

namespace SideWar.Platform.Windows.Taskbar;

/// <summary>
/// SDD-21 RF-21.9 `--dump-taskbar [--name N] [--bar I] [--out PATH]`. No windows, no game. Ignores useUia.
/// Exit codes: 0 ok, 1 usage/IO error, 2 UIA failed (the file is still written with what was obtained).
/// </summary>
public static partial class TaskbarDumpCommand
{
    public const string Flag = "--dump-taskbar";
    public static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

    public static bool IsRequested(string[] args) => args.Contains(Flag, StringComparer.Ordinal);

    /// <summary>WinExe has no console; attach to the parent's so the dump path is printed. Failure is harmless.</summary>
    public static void AttachParentConsole() => global::Windows.Win32.PInvoke.AttachConsole(0xFFFFFFFF);

    public static int Run(string[] args, TextWriter output)
    {
        try
        {
            var name = Option(args, "--name") ?? "atual";
            if (!SafeName().IsMatch(name)) { output.WriteLine("Invalid --name (use letters, digits, '-' or '_')."); return 1; }
            var barIndex = int.TryParse(Option(args, "--bar"), out var b) ? b : 0;
            var path = Option(args, "--out") ?? DefaultPath(name);
            if (path is null) { output.WriteLine("Repository root (SideWar.slnx) not found; pass --out."); return 1; }

            var bars = TaskbarLocator.FindBars();
            if (barIndex < 0 || barIndex >= bars.Count) { output.WriteLine($"No taskbar at index {barIndex} ({bars.Count} found)."); return 2; }
            var bar = bars[barIndex];
            var target = TaskbarLocator.Describe(bar, bar.Monitor);

            DumpNode[] nodes = [];
            var uiaOk = true;
            using var probe = new TaskbarProbe();
            try
            {
                var task = probe.DumpAsync(bar.Hwnd);
                if (task.Wait(QueryTimeout)) nodes = task.Result;
                else { uiaOk = false; output.WriteLine("UIA timed out."); }
            }
            catch (AggregateException ex)
            {
                uiaOk = false;
                output.WriteLine("UIA failed: " + ex.GetBaseException().GetType().Name);
            }

            var dump = TaskbarDump.Create(Environment.OSVersion.Version.Build, bar.Monitor, target.Context.Edge,
                target.Context.AutoHide, target.Context.TaskbarRectPx, nodes);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, dump.ToJson(), new UTF8Encoding(false));
            output.WriteLine(Path.GetFullPath(path));
            return uiaOk ? 0 : 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            output.WriteLine("I/O error: " + ex.Message);
            return 1;
        }
    }

    private static string? Option(string[] args, string key)
    {
        var i = Array.IndexOf(args, key);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static string? DefaultPath(string name)
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SideWar.slnx")))
                return Path.Combine(dir.FullName, "tests", "SideWar.Runtime.Tests", "Fixtures", "windows", $"taskbar-{name}.json");
        return null;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex SafeName();
}
