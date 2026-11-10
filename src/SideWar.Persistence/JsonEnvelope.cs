using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SideWar.Persistence;

public enum EnvelopeStatus { Ok, Corrupt }

public readonly record struct EnvelopeReadResult(EnvelopeStatus Status, string Reason, JsonDocument? Payload);

/// <summary>Envelope comum (SDD-14 RF-14.3). contentHash = SHA-256 hex do payload re-serializado de forma compacta.</summary>
public static class JsonEnvelope
{
    public const int MaxReadBytes = 1024 * 1024;

    public static byte[] Write(string schema, int schemaVersion, string appVersion, DateTime savedAtUtc, Action<Utf8JsonWriter> writePayload)
    {
        using var payloadBuf = new MemoryStream();
        using (var pw = new Utf8JsonWriter(payloadBuf)) writePayload(pw);
        var payloadBytes = payloadBuf.ToArray();

        using var buf = new MemoryStream();
        using (var w = new Utf8JsonWriter(buf, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("schema", schema);
            w.WriteNumber("schemaVersion", schemaVersion);
            w.WriteString("appVersion", appVersion);
            w.WriteString("savedAtUtc", savedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture));
            w.WriteString("contentHash", Convert.ToHexStringLower(SHA256.HashData(payloadBytes)));
            w.WritePropertyName("payload");
            w.WriteRawValue(payloadBytes, skipInputValidation: true);
            w.WriteEndObject();
        }
        return buf.ToArray();
    }

    /// <summary>Valida tamanho, JSON, schema, versão e hash. O chamador é dono do JsonDocument retornado.</summary>
    public static EnvelopeReadResult Read(ReadOnlySpan<byte> utf8, string expectedSchema, int supportedVersion)
    {
        if (utf8.Length > MaxReadBytes) return Fail("tamanho acima de 1 MB");
        JsonDocument doc;
        try { doc = JsonDocument.Parse(utf8.ToArray()); }
        catch (JsonException) { return Fail("JSON inválido"); }

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return Dispose(doc, "raiz não é objeto");
        if (!root.TryGetProperty("schema", out var s) || s.ValueKind != JsonValueKind.String || s.GetString() != expectedSchema)
            return Dispose(doc, "schema desconhecido");
        if (!root.TryGetProperty("schemaVersion", out var v) || !v.TryGetInt32(out var version))
            return Dispose(doc, "schemaVersion ausente");
        if (version > supportedVersion) return Dispose(doc, "schemaVersion futura");
        if (version < 1) return Dispose(doc, "schemaVersion sem migração registrada");
        if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            return Dispose(doc, "payload ausente");
        if (!root.TryGetProperty("contentHash", out var h) || h.ValueKind != JsonValueKind.String
            || !string.Equals(h.GetString(), ComputeHash(payload), StringComparison.OrdinalIgnoreCase))
            return Dispose(doc, "contentHash divergente");
        return new EnvelopeReadResult(EnvelopeStatus.Ok, "", doc);

        static EnvelopeReadResult Fail(string r) => new(EnvelopeStatus.Corrupt, r, null);
        static EnvelopeReadResult Dispose(JsonDocument d, string r) { d.Dispose(); return Fail(r); }
    }

    public static string ComputeHash(JsonElement payload)
    {
        using var buf = new MemoryStream();
        using (var w = new Utf8JsonWriter(buf)) payload.WriteTo(w);
        return Convert.ToHexStringLower(SHA256.HashData(buf.ToArray()));
    }

    internal static string Utf8(ReadOnlySpan<byte> b) => Encoding.UTF8.GetString(b);
}
