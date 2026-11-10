using System.Globalization;
using System.Text.Json;

namespace SideWar.AssetPipeline;

/// <summary>Entrada de <c>sprites.json</c> (SDD-09 RF-09.2).</summary>
public sealed record SpriteDefinition(
    string Id,
    string File,
    double? WorldWidth,
    double? WorldHeight,
    double PivotX,
    double PivotY,
    bool Trim,
    bool Placeholder);

public static class SpritesFile
{
    public static IReadOnlyList<SpriteDefinition> Parse(ReadOnlySpan<byte> json)
    {
        using var doc = JsonDocument.Parse(json.ToArray());
        var root = doc.RootElement;
        if (root.GetProperty("version").GetInt32() != 1)
        {
            throw new InvalidDataException("sprites.json: version deve ser 1.");
        }

        var list = new List<SpriteDefinition>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in root.GetProperty("sprites").EnumerateArray())
        {
            var id = e.GetProperty("id").GetString() ?? throw new InvalidDataException("sprites.json: id ausente.");
            if (!ids.Add(id))
            {
                throw new InvalidDataException($"sprites.json: id duplicado '{id}'.");
            }

            double? w = e.TryGetProperty("worldWidth", out var we) ? we.GetDouble() : null;
            double? h = e.TryGetProperty("worldHeight", out var he) ? he.GetDouble() : null;
            if ((w is null) == (h is null))
            {
                throw new InvalidDataException($"sprites.json: '{id}' deve ter exatamente um entre worldWidth e worldHeight.");
            }

            if ((w ?? h) <= 0)
            {
                throw new InvalidDataException($"sprites.json: '{id}' com tamanho não positivo.");
            }

            double px = 0.5, py = 1.0;
            if (e.TryGetProperty("pivot", out var pv))
            {
                px = pv[0].GetDouble();
                py = pv[1].GetDouble();
            }

            var trim = !e.TryGetProperty("trim", out var te) || te.GetBoolean();
            var placeholder = e.TryGetProperty("placeholder", out var pe) && pe.GetBoolean();
            var file = e.GetProperty("file").GetString() ?? throw new InvalidDataException($"sprites.json: '{id}' sem file.");
            list.Add(new SpriteDefinition(id, file, w, h, px, py, trim, placeholder));
        }

        return list;
    }

    internal static string Fmt(double v) => v.ToString("R", CultureInfo.InvariantCulture);
}
