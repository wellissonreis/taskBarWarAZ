using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SideWar.AssetPipeline;

public sealed record SpriteSource(SpriteDefinition Definition, byte[] PngBytes);

public sealed record AtlasResult(byte[] Png, byte[] Json, string InputsHash, string PixelsHash, IReadOnlyList<PackedRect> Rects);

/// <summary>Gera <c>atlas.png</c> e <c>atlas.json</c> (SDD-09 RF-09.3). Puro e determinístico.</summary>
public static class AtlasBuilder
{
    public const byte AlphaThreshold = 8;
    public const double DefaultMasterScale = 3.25;
    private const string HashSchema = "SideWar.AssetPipeline/atlas-v1";

    public static string ComputeInputsHash(IEnumerable<SpriteSource> sources, double masterScale)
    {
        var sb = new StringBuilder();
        sb.Append(HashSchema).Append('\n').Append(masterScale.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        foreach (var s in sources.OrderBy(s => s.Definition.Id, StringComparer.Ordinal))
        {
            var d = s.Definition;
            sb.Append(d.Id).Append('|').Append(d.File).Append('|')
              .Append(SpritesFile.Fmt(d.WorldWidth ?? -1)).Append('|').Append(SpritesFile.Fmt(d.WorldHeight ?? -1)).Append('|')
              .Append(SpritesFile.Fmt(d.PivotX)).Append('|').Append(SpritesFile.Fmt(d.PivotY)).Append('|')
              .Append(d.Trim ? '1' : '0').Append('|')
              .Append(Convert.ToHexStringLower(SHA256.HashData(s.PngBytes))).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    public static AtlasResult Build(IReadOnlyList<SpriteSource> sources, double masterScale = DefaultMasterScale)
    {
        if (sources.Count == 0)
        {
            throw new InvalidOperationException("Nenhum sprite para empacotar.");
        }

        var processed = new Dictionary<string, (SpriteDefinition Def, Rgba Img, double WorldW, double WorldH)>(StringComparer.Ordinal);
        foreach (var s in sources.OrderBy(s => s.Definition.Id, StringComparer.Ordinal))
        {
            var d = s.Definition;
            var img = Rgba.Decode(s.PngBytes);
            img.ZeroLowAlpha(AlphaThreshold);
            if (d.Trim)
            {
                var b = img.OpaqueBounds(AlphaThreshold) ?? throw new InvalidOperationException($"Sprite '{d.Id}' totalmente transparente.");
                img = img.Crop(b.X, b.Y, b.W, b.H);
            }

            double worldW, worldH;
            if (d.WorldWidth is { } ww)
            {
                worldW = ww;
                worldH = Math.Round(ww * img.Height / img.Width, 4);
            }
            else
            {
                worldH = d.WorldHeight!.Value;
                worldW = Math.Round(worldH * img.Width / img.Height, 4);
            }

            // Mestre = min(nativo, ceil(dip * masterScale)) no eixo definido; nunca amplia.
            var native = d.WorldWidth is not null ? img.Width : img.Height;
            var target = (int)Math.Ceiling((d.WorldWidth ?? d.WorldHeight!.Value) * masterScale);
            var master = Math.Min(native, target);
            if (master < native)
            {
                int tw, th;
                if (d.WorldWidth is not null)
                {
                    tw = master;
                    th = Math.Max(1, (int)Math.Round((double)img.Height * master / img.Width, MidpointRounding.AwayFromZero));
                }
                else
                {
                    th = master;
                    tw = Math.Max(1, (int)Math.Round((double)img.Width * master / img.Height, MidpointRounding.AwayFromZero));
                }

                img = img.ResizeMitchell(tw, th);
                img.ZeroLowAlpha(AlphaThreshold);
            }

            processed.Add(d.Id, (d, img, worldW, worldH));
        }

        var (rects, width, height) = ShelfPacker.Pack(processed.Select(p => new PackItem(p.Key, p.Value.Img.Width, p.Value.Img.Height)));
        var atlas = new Rgba(width, height, new byte[width * height * 4]);
        foreach (var r in rects)
        {
            atlas.Blit(processed[r.Id].Img, r.X, r.Y);
        }

        var inputsHash = ComputeInputsHash(sources, masterScale);
        var pixelsHash = Convert.ToHexStringLower(SHA256.HashData(atlas.Data));
        var png = atlas.EncodePng();
        var json = WriteJson(masterScale, width, height, inputsHash, pixelsHash, rects, processed);
        return new AtlasResult(png, json, inputsHash, pixelsHash, rects);
    }

    private static byte[] WriteJson(
        double masterScale,
        int width,
        int height,
        string inputsHash,
        string pixelsHash,
        IReadOnlyList<PackedRect> rects,
        Dictionary<string, (SpriteDefinition Def, Rgba Img, double WorldW, double WorldH)> processed)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", 1);
            w.WriteNumber("masterScale", masterScale);
            w.WriteNumber("width", width);
            w.WriteNumber("height", height);
            w.WriteString("inputsHash", inputsHash);
            w.WriteString("pixelsHash", pixelsHash);
            w.WriteStartObject("sprites");
            foreach (var r in rects.OrderBy(r => r.Id, StringComparer.Ordinal))
            {
                var p = processed[r.Id];
                w.WriteStartObject(r.Id);
                w.WriteNumber("x", r.X);
                w.WriteNumber("y", r.Y);
                w.WriteNumber("w", r.W);
                w.WriteNumber("h", r.H);
                w.WriteNumber("pivotX", p.Def.PivotX);
                w.WriteNumber("pivotY", p.Def.PivotY);
                w.WriteNumber("worldW", p.WorldW);
                w.WriteNumber("worldH", p.WorldH);
                if (p.Def.Placeholder)
                {
                    w.WriteBoolean("placeholder", true);
                }

                w.WriteEndObject();
            }

            w.WriteEndObject();
            w.WriteEndObject();
        }

        ms.WriteByte((byte)'\n');
        return ms.ToArray();
    }
}
