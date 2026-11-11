using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace SideWar.Core.Content;

/// <summary>Resultado da carga: <c>Content</c> só é não nulo quando <c>Errors</c> está vazio.</summary>
public sealed record ContentLoadResult(GameContent? Content, IReadOnlyList<string> Errors)
{
    public bool IsValid => Content is not null;
}

/// <summary>Falha fatal de conteúdo (SDD-12 RF-12.9): carrega a lista completa de erros.</summary>
public sealed class ContentException(IReadOnlyList<string> errors)
    : Exception("Conteúdo inválido: " + string.Join("; ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}

public static class ContentLoader
{
    /// <summary>Arquivos obrigatórios, em ordem alfabética ordinal (ordem do ContentHash, RF-12.1).</summary>
    public static readonly IReadOnlyList<string> FileNames =
        ["economy.json", "enemies.json", "missions.json", "questions.json", "regions.json", "rules.json", "stages.json", "towers.json", "waves.json"];

    /// <summary>Carrega e valida o conteúdo embutido em <c>SideWar.Core</c>; lança <see cref="ContentException"/> se inválido.</summary>
    public static GameContent LoadEmbedded()
    {
        var result = Load(ReadEmbedded());
        return result.Content ?? throw new ContentException(result.Errors);
    }

    /// <summary>Lê os bytes dos JSON embutidos (útil para testes que alteram um arquivo).</summary>
    public static Dictionary<string, byte[]> ReadEmbedded()
    {
        var assembly = typeof(ContentLoader).Assembly;
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

    /// <summary>SHA-256 (hex minúsculo) dos bytes concatenados em ordem alfabética de nome (RF-12.1).</summary>
    public static string ComputeHash(IReadOnlyDictionary<string, byte[]> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var name in FileNames)
            if (files.TryGetValue(name, out var bytes))
                hash.AppendData(bytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Desserializa e valida; acumula todos os erros em vez de parar no primeiro.</summary>
    public static ContentLoadResult Load(IReadOnlyDictionary<string, byte[]> files)
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

        var ctx = ContentJsonContext.Default;
        var rules = Read("rules.json", ctx.RulesDef);
        var towers = Read("towers.json", ctx.TowerDefArray);
        var enemies = Read("enemies.json", ctx.EnemyDefArray);
        var waves = Read("waves.json", ctx.WavesDef);
        var stages = Read("stages.json", ctx.StageDefArray);
        var regions = Read("regions.json", ctx.RegionDefArray);
        var missions = Read("missions.json", ctx.MissionsDef);
        var economy = Read("economy.json", ctx.EconomyDef);
        var questions = Read("questions.json", Events.QuestionJsonContext.Default.QuestionBank);
        if (errors.Count > 0)
            return new ContentLoadResult(null, errors);

        var content = new GameContent(rules!, towers!, enemies!, waves!, stages!, regions!, missions!, economy!,
            questions!, ComputeHash(files));
        try
        {
            errors.AddRange(ContentValidator.Validate(content));
            if (errors.Count == 0)
                errors.AddRange(Events.QuestionBankValidator.Validate(content.Questions, content.Stages, content.Waves.BossEvery));
        }
        catch (NullReferenceException)
        {
            // JSON com `null` dentro de listas/objetos que o desserializador aceita.
            errors.Add("conteúdo contém valor nulo onde um objeto, texto ou lista é obrigatório");
        }
        return errors.Count > 0 ? new ContentLoadResult(null, errors) : new ContentLoadResult(content, errors);
    }
}
