using System.Text.Json;
using SideWar.Core.Content;

namespace SideWar.Core.Events;

public sealed record QuestionBankLoadResult(QuestionBank? Bank, IReadOnlyList<string> Errors)
{
    public bool IsValid => Bank is not null;
}

public static class QuestionBankLoader
{
    public const string ResourceName = "SideWar.Core.Content.questions.json";

    public static byte[] ReadEmbedded()
    {
        using var stream = typeof(QuestionBankLoader).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new ContentException(["questions.json: recurso embutido ausente"]);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public static QuestionBank LoadEmbedded(GameContent content)
    {
        var r = Load(ReadEmbedded(), content);
        return r.Bank ?? throw new ContentException(r.Errors);
    }

    public static QuestionBankLoadResult Load(byte[] json, GameContent content)
    {
        QuestionBank? bank;
        try
        {
            bank = JsonSerializer.Deserialize(json, QuestionJsonContext.Default.QuestionBank);
        }
        catch (JsonException ex)
        {
            return new QuestionBankLoadResult(null, [$"questions.json: {ex.Message}"]);
        }
        if (bank is null)
            return new QuestionBankLoadResult(null, ["questions.json: conteúdo nulo"]);
        try
        {
            var errors = QuestionBankValidator.Validate(bank, content.Stages, content.Waves.BossEvery);
            return errors.Count > 0 ? new QuestionBankLoadResult(null, errors) : new QuestionBankLoadResult(bank, errors);
        }
        catch (NullReferenceException)
        {
            return new QuestionBankLoadResult(null, ["questions.json: valor nulo onde objeto, texto ou lista é obrigatório"]);
        }
    }
}
