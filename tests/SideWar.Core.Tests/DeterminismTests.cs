using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using SideWar.Core.Bot;
using SideWar.Core.Content;
using Xunit;

namespace SideWar.Core.Tests;

/// <summary>
/// SDD-17 RF-17.7 — grava artifacts/determinism/&lt;os&gt;.json com o StateHash final das 3 partidas scriptadas
/// (SDD-11 CA-11.2). scripts/wsl/compare-determinism.sh compara o arquivo do Linux com o do Windows.
/// </summary>
public sealed class DeterminismTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();

    private static string OsName() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
        : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "other";

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SideWar.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("SideWar.slnx not found above " + AppContext.BaseDirectory);
    }

    [Fact(DisplayName = "RF-17.7 Determinismo: grava artifacts/determinism/<os>.json com os hashes das 3 partidas")]
    public void Writes_Hash_File()
    {
        var scripts = new[] { ReplayTests.BotLong(), ReplayTests.Manual(), ReplayTests.BotShort(), ReplayTests.ManualMissions() };
        var sb = new StringBuilder("{\n");
        for (int i = 0; i < scripts.Length; i++)
        {
            var r = ReplayRunner.Run(Content, scripts[i]);
            sb.Append(CultureInfo.InvariantCulture, $"  \"{scripts[i].Name}\": \"{r.StateHash}\"");
            sb.Append(i < scripts.Length - 1 ? ",\n" : "\n");
        }
        sb.Append("}\n");
        var dir = Path.Combine(FindRoot(), "artifacts", "determinism");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, OsName() + ".json"), sb.ToString(), new UTF8Encoding(false));
        Assert.Contains("bot-200", sb.ToString());
    }
}
