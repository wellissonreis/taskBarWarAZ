using System.Text.Json;

namespace SideWar.AssetPipeline;

public enum RunOutcome
{
    Written,
    UpToDate,
}

/// <summary>Lê <c>--in</c> e grava <c>--out</c>; não regrava se nada mudou (SDD-09 RF-09.3 passo 5).</summary>
public static class PipelineRunner
{
    public static RunOutcome Run(string inDir, string outDir, double masterScale = AtlasBuilder.DefaultMasterScale, Action<string>? log = null)
    {
        log ??= _ => { };
        var defs = SpritesFile.Parse(File.ReadAllBytes(Path.Combine(inDir, "sprites.json")));

        var manifest = Path.Combine(inDir, "design", "MANIFEST.json");
        if (File.Exists(manifest))
        {
            var errors = ManifestVerifier.Verify(manifest, Path.Combine(inDir, "design"));
            if (errors.Count > 0)
            {
                throw new InvalidDataException(string.Join(Environment.NewLine, errors));
            }
        }

        var sources = new List<SpriteSource>();
        foreach (var d in defs)
        {
            var path = Path.Combine(inDir, d.File);
            if (!File.Exists(path))
            {
                if (d.Placeholder)
                {
                    log($"placeholder sem PNG ignorado: {d.Id}");
                    continue;
                }

                throw new FileNotFoundException($"PNG ausente para '{d.Id}'.", path);
            }

            sources.Add(new SpriteSource(d, File.ReadAllBytes(path)));
        }

        var jsonPath = Path.Combine(outDir, "atlas.json");
        var pngPath = Path.Combine(outDir, "atlas.png");
        var hash = AtlasBuilder.ComputeInputsHash(sources, masterScale);
        if (File.Exists(jsonPath) && File.Exists(pngPath) && ReadInputsHash(jsonPath) == hash)
        {
            return RunOutcome.UpToDate;
        }

        var result = AtlasBuilder.Build(sources, masterScale);
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(pngPath, result.Png);
        File.WriteAllBytes(jsonPath, result.Json);
        return RunOutcome.Written;
    }

    private static string? ReadInputsHash(string jsonPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(jsonPath));
            return doc.RootElement.GetProperty("inputsHash").GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
