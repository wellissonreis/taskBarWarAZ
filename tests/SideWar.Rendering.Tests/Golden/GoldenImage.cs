using SkiaSharp;

namespace SideWar.Rendering.Tests.Golden;

/// <summary>Resultado da comparação de uma imagem com a golden (SDD-17 RF-17.3).</summary>
public sealed record GoldenResult(int Width, int Height, int DifferentPixels, double Fraction, bool SizeMismatch)
{
    public bool Passed(double maxFraction) => !SizeMismatch && Fraction <= maxFraction;
}

/// <summary>
/// Harness de golden images: pixels com ΔE76 &gt; 8 contra a referência; falha se &gt; 0,5%. Em falha grava
/// <c>&lt;cena&gt;.actual.png</c> e <c>&lt;cena&gt;.diff.png</c> em <c>artifacts/golden/</c>. Atualização só com
/// <c>SIDEWAR_UPDATE_GOLDEN=1</c> (revisão visual registrada na evidência).
/// </summary>
public static class GoldenImage
{
    public const double DeltaEThreshold = 8.0;
    public const double MaxFraction = 0.005;
    public const string UpdateVariable = "SIDEWAR_UPDATE_GOLDEN";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SideWar.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("SideWar.slnx não encontrado acima de " + AppContext.BaseDirectory);
    }

    /// <summary>Pasta <c>Golden/</c> do projeto de teste, no código-fonte (robusta a ContinuousIntegrationBuild).</summary>
    public static string GoldenDirectory() => Path.Combine(RepoRoot(), "tests", "SideWar.Rendering.Tests", "Golden");

    public static string ArtifactsDirectory() => Path.Combine(RepoRoot(), "artifacts", "golden");

    public static bool UpdateMode => Environment.GetEnvironmentVariable(UpdateVariable) == "1";

    /// <summary>ΔE76 entre duas cores sRGB opacas.</summary>
    public static double DeltaE76(SKColor a, SKColor b)
    {
        var (l1, a1, b1) = ToLab(a);
        var (l2, a2, b2) = ToLab(b);
        double dl = l1 - l2, da = a1 - a2, db = b1 - b2;
        return Math.Sqrt(dl * dl + da * da + db * db);
    }

    private static (double L, double A, double B) ToLab(SKColor c)
    {
        static double Lin(byte v)
        {
            double s = v / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        static double F(double t) => t > 216.0 / 24389.0 ? Math.Cbrt(t) : (24389.0 / 27.0 * t + 16.0) / 116.0;
        double r = Lin(c.Red), g = Lin(c.Green), bl = Lin(c.Blue);
        double x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * bl) / 0.95047;
        double y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * bl;
        double z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * bl) / 1.08883;
        double fx = F(x), fy = F(y), fz = F(z);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    /// <summary>Compara pixel a pixel (imagens opacas); <paramref name="diff"/> recebe o mapa de diferenças (vermelho = acima do limite).</summary>
    public static GoldenResult Compare(SKBitmap expected, SKBitmap actual, out SKBitmap diff)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            diff = new SKBitmap(actual.Width, actual.Height);
            return new GoldenResult(actual.Width, actual.Height, actual.Width * actual.Height, 1.0, true);
        }
        diff = new SKBitmap(actual.Width, actual.Height);
        int bad = 0;
        for (int y = 0; y < actual.Height; y++)
            for (int x = 0; x < actual.Width; x++)
            {
                var e = expected.GetPixel(x, y);
                var a = actual.GetPixel(x, y);
                if (DeltaE76(e, a) > DeltaEThreshold)
                {
                    bad++;
                    diff.SetPixel(x, y, new SKColor(255, 0, 0));
                }
                else
                {
                    diff.SetPixel(x, y, new SKColor((byte)(a.Red / 3), (byte)(a.Green / 3), (byte)(a.Blue / 3)));
                }
            }
        return new GoldenResult(actual.Width, actual.Height, bad, bad / (double)(actual.Width * actual.Height), false);
    }

    /// <summary>Desenha o bitmap em (x, y) sem reamostragem (1:1).</summary>
    public static void Blit(SKCanvas canvas, SKBitmap bmp, float x, float y, SKPaint? paint = null)
    {
        using var img = SKImage.FromBitmap(bmp);
        canvas.DrawImage(img, x, y, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None), paint);
    }

    public static void Save(SKBitmap bmp, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }

    /// <summary>
    /// Verifica <paramref name="actual"/> contra <c>Golden/&lt;name&gt;.png</c>. Em modo de atualização grava a referência.
    /// Retorna null se passou, ou a mensagem de falha (já com os arquivos gravados em artifacts/golden).
    /// </summary>
    public static string? Verify(string name, SKBitmap actual, string? goldenDirectory = null, string? artifactsDirectory = null)
    {
        string golden = Path.Combine(goldenDirectory ?? GoldenDirectory(), name + ".png");
        if (UpdateMode)
        {
            Save(actual, golden);
            return null;
        }
        string artifacts = artifactsDirectory ?? ArtifactsDirectory();
        if (!File.Exists(golden))
        {
            Save(actual, Path.Combine(artifacts, name + ".actual.png"));
            return $"golden ausente: {golden} (gere com {UpdateVariable}=1)";
        }
        using var expected = SKBitmap.Decode(golden);
        using var exp = Normalize(expected);
        var result = Compare(exp, actual, out var diff);
        using (diff)
        {
            if (result.Passed(MaxFraction)) return null;
            Save(actual, Path.Combine(artifacts, name + ".actual.png"));
            Save(diff, Path.Combine(artifacts, name + ".diff.png"));
            return result.SizeMismatch
                ? $"{name}: tamanho diferente (golden {exp.Width}x{exp.Height}, atual {actual.Width}x{actual.Height})"
                : $"{name}: {result.Fraction:P3} de pixels com ΔE > {DeltaEThreshold} (limite {MaxFraction:P1}); ver {artifacts}";
        }
    }

    private static SKBitmap Normalize(SKBitmap src)
    {
        var bmp = new SKBitmap(new SKImageInfo(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        Blit(c, src, 0, 0);
        return bmp;
    }
}
