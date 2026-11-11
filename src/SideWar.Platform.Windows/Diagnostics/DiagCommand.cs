using System.Reflection;
using System.Text.Json;
using SideWar.Platform.Windows.Windows;
using Windows.Win32;

namespace SideWar.Platform.Windows.Diagnostics;

/// <summary>`--version` and `--diag` (SDD-16 RF-16.2). No windows, no game; exit 0.</summary>
public static class DiagCommand
{
    public static string Version(Assembly assembly)
    {
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return info ?? assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    public static void WriteDiag(TextWriter output, Assembly assembly, string dataFolder)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("version", Version(assembly));
            json.WriteString("osVersion", Environment.OSVersion.VersionString);
            json.WriteNumber("osBuild", Environment.OSVersion.Version.Build);
            json.WriteString("dpiAwareness", DpiAwareness());
            json.WriteString("dataFolder", dataFolder);
            json.WriteStartArray("monitors");
            foreach (var m in MonitorService.Enumerate())
            {
                json.WriteStartObject();
                json.WriteString("device", m.DeviceName);
                json.WriteBoolean("primary", m.IsPrimary);
                json.WriteNumber("dpi", m.Dpi);
                json.WriteString("bounds", $"{m.Bounds.Left},{m.Bounds.Top},{m.Bounds.Right},{m.Bounds.Bottom}");
                json.WriteString("workArea", $"{m.WorkArea.Left},{m.WorkArea.Top},{m.WorkArea.Right},{m.WorkArea.Bottom}");
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        output.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    /// <summary>Process-level awareness of the calling thread's context (the manifest requests PMv2).</summary>
    private static string DpiAwareness()
    {
        var current = PInvoke.GetThreadDpiAwarenessContext();
        return PInvoke.AreDpiAwarenessContextsEqual(current, MonitorService.PerMonitorV2) ? "PerMonitorV2" : "Other";
    }
}
