using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace SideWar.Core.Content.Td;

/// <summary>Resultado da carga TD: <c>Content</c> só é não nulo quando <c>Errors</c> está vazio.</summary>
public sealed record TdContentLoadResult(TdContent? Content, IReadOnlyList<string> Errors)
{
    public bool IsValid => Content is not null;
}

/// <summary>Carrega, valida e calcula o hash do conteúdo TD v2 (arquivos <c>td-*.json</c>); lança <see cref="ContentException"/> se inválido.</summary>
public static class TdContentLoader
{
    /// <summary>Arquivos obrigatórios, em ordem alfabética ordinal (ordem do hash).</summary>
    public static readonly IReadOnlyList<string> FileNames =
        ["td-abilities.json", "td-enemies.json", "td-meta.json", "td-stages.json", "td-towers.json", "td-waves.json"];

    public static TdContent LoadEmbedded()
    {
        var result = Load(ReadEmbedded());
        return result.Content ?? throw new ContentException(result.Errors);
    }

    public static Dictionary<string, byte[]> ReadEmbedded()
    {
        var assembly = typeof(TdContentLoader).Assembly;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var name in FileNames)
        {
            using var stream = assembly.GetManifestResourceStream("SideWar.Core.Content." + name);
            if (stream is null)
                continue;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            files[name] = memory.ToArray();
        }
        return files;
    }

    /// <summary>SHA-256 (hex minúsculo) dos bytes concatenados em ordem alfabética de nome.</summary>
    public static string ComputeHash(IReadOnlyDictionary<string, byte[]> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var name in FileNames)
            if (files.TryGetValue(name, out var bytes))
                hash.AppendData(bytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static TdContentLoadResult Load(IReadOnlyDictionary<string, byte[]> files)
    {
        var errors = new List<string>();
        T? Read<T>(string name, JsonTypeInfo<T> info) where T : class
        {
            if (!files.TryGetValue(name, out var bytes))
            {
                errors.Add($"{name}: arquivo ausente");
                return null;
            }
            try
            {
                var value = JsonSerializer.Deserialize(bytes, info);
                if (value is null)
                    errors.Add($"{name}: conteúdo nulo");
                return value;
            }
            catch (JsonException ex)
            {
                errors.Add($"{name}: {ex.Message}");
                return null;
            }
        }

        var ctx = TdContentJsonContext.Default;
        var abilities = Read("td-abilities.json", ctx.TdAbilityDefArray);
        var enemies = Read("td-enemies.json", ctx.TdEnemiesDef);
        var meta = Read("td-meta.json", ctx.TdMetaDef);
        var stages = Read("td-stages.json", ctx.TdStagesDef);
        var towers = Read("td-towers.json", ctx.TdTowerDefArray);
        var waves = Read("td-waves.json", ctx.TdWavesDef);
        if (errors.Count > 0)
            return new TdContentLoadResult(null, errors);

        var content = new TdContent(towers!, enemies!, waves!, stages!, abilities!, meta!, ComputeHash(files));
        try
        {
            errors.AddRange(TdContentValidator.Validate(content));
        }
        catch (NullReferenceException)
        {
            errors.Add("conteúdo contém valor nulo onde um objeto, texto ou lista é obrigatório");
        }
        return errors.Count > 0 ? new TdContentLoadResult(null, errors) : new TdContentLoadResult(content, errors);
    }
}
