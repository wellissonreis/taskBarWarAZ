using System.Security.Cryptography;
using System.Text.Json;

namespace SideWar.AssetPipeline;

/// <summary>
/// Confere <c>assets/source/design/MANIFEST.json</c> (SDD-22 RF-22.9 passo 3; T4.1) quando existir:
/// raiz como array ou objeto com <c>entries</c>; cada item com <c>file</c>, <c>sha256</c> e <c>placeholder</c> opcional.
/// </summary>
public static class ManifestVerifier
{
    public static IReadOnlyList<string> Verify(string manifestPath, string designDir)
    {
        var errors = new List<string>();
        using var doc = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var root = doc.RootElement;
        var entries = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("entries");
        foreach (var e in entries.EnumerateArray())
        {
            var file = e.GetProperty("file").GetString() ?? string.Empty;
            if (e.TryGetProperty("placeholder", out var ph) && ph.GetBoolean())
            {
                continue;
            }

            var path = Path.Combine(designDir, file);
            if (!File.Exists(path))
            {
                errors.Add($"MANIFEST: arquivo ausente '{file}'.");
                continue;
            }

            var expected = e.GetProperty("sha256").GetString() ?? string.Empty;
            var actual = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"MANIFEST: sha256 de '{file}' não confere.");
            }
        }

        return errors;
    }
}
