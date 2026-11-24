using System.Text.Json;
using SideWar.Rendering.Scene;
using Xunit;

namespace SideWar.Rendering.Tests.Scene;

public sealed class TdSpecialBossArtTests
{
    [Fact]
    public void AllSpecialSheetsAreMappedEmbeddedAndInManifest()
    {
        string[] ids = ["botnet", "kraken", "golem", "ronin", "trojan", "hydra"];
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SideWar.slnx"))) dir = dir.Parent;
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "assets/source/anim/MANIFEST.json")));
        var files = doc.RootElement.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("file").GetString()).ToHashSet();
        foreach (var id in ids)
        {
            Assert.True(TdArtMap.TryEnemy("chefe_" + id, out _));
            Assert.True(TdArtMap.TryEnemy("aliado_" + id, out _));
            Assert.True(TdAnimArt.TryEnemyFps("chefe_" + id, out _));
            Assert.True(TdAnimArt.TryEnemyFps("aliado_" + id, out _));
            Assert.True(TdAnimArt.TryEnragedFps("chefe_" + id, out _));
            foreach (string name in new[] { "chefe_" + id, "chefe_" + id + "_enf", "aliado_" + id })
            {
                string file = name + "-walk.png";
                Assert.Contains(file, files);
                using var stream = typeof(TdAnimArt).Assembly.GetManifestResourceStream(TdAnimArt.Resource(file));
                Assert.NotNull(stream);
            }
        }
    }
}
