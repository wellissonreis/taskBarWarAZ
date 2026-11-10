using SideWar.AssetPipeline;

// Uso: dotnet run --project tools/SideWar.AssetPipeline -- --in assets/source --out src/SideWar.Rendering/Assets [--master-scale 3.25]
string? inDir = null, outDir = null;
var scale = AtlasBuilder.DefaultMasterScale;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--in" when i + 1 < args.Length: inDir = args[++i]; break;
        case "--out" when i + 1 < args.Length: outDir = args[++i]; break;
        case "--master-scale" when i + 1 < args.Length:
            scale = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        default:
            Console.Error.WriteLine($"Argumento inválido: {args[i]}");
            return 2;
    }
}

if (inDir is null || outDir is null)
{
    Console.Error.WriteLine("Uso: --in <assets/source> --out <pasta de saída> [--master-scale N]");
    return 2;
}

try
{
    var outcome = PipelineRunner.Run(inDir, outDir, scale, Console.WriteLine);
    Console.WriteLine(outcome == RunOutcome.Written ? "atlas gravado." : "atlas já está atualizado.");
    return 0;
}
catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
