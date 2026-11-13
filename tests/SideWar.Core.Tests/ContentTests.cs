using System.Text;
using System.Text.RegularExpressions;
using SideWar.Core.Content;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class ContentTests
{
    private static Dictionary<string, byte[]> Files() => ContentLoader.ReadEmbedded();

    private static ContentLoadResult With(string file, string find, string replace)
    {
        var files = Files();
        var text = Encoding.UTF8.GetString(files[file]);
        Assert.Contains(find, text);
        files[file] = Encoding.UTF8.GetBytes(text.Replace(find, replace, StringComparison.Ordinal));
        return ContentLoader.Load(files);
    }

    private static void AssertError(ContentLoadResult r, string fragment)
    {
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, x => x.Contains(fragment, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "T-12.1 / CA-12.1 Embedded content passes the validator")]
    public void Embedded_Content_IsValid()
    {
        var c = ContentLoader.LoadEmbedded();
        Assert.Equal(4, c.Towers.Length);
        Assert.Equal(6, c.Enemies.Length);
        Assert.Equal(10, c.Stages.Length);
        Assert.Equal(3, c.Regions.Length);
        Assert.Equal(6, c.Missions.Missions.Length);
        Assert.Equal(60, c.Stages[0].Waves);
        Assert.Equal(80, c.Stages[6].Waves);
        Assert.Equal(100, c.Stages[9].Waves);
        Assert.Equal(9, c.Towers.Single(t => t.Id == "scaleset").UnlockStage);
        Assert.Equal(25, c.Enemies.Single(e => e.Id == "boss").Speed);
        Assert.Equal(1.09, c.Economy.HpGrowth);
        Assert.Equal(48, c.Rules.SlotRangeDip);
    }

    [Fact(DisplayName = "D-14 Stage titles 08-10 are provisional")]
    public void Stages_08To10_AreProvisional()
    {
        var c = ContentLoader.LoadEmbedded();
        Assert.All(c.Stages.Take(7), s => Assert.False(s.TitleProvisional));
        Assert.All(c.Stages.Skip(7), s => Assert.True(s.TitleProvisional));
        Assert.Equal("Computação em nuvem", c.Stages[0].StageTopic);
        Assert.Equal("Elasticidade", c.Stages[9].StageTopic);
    }

    [Fact(DisplayName = "T-12.4 ContentHash is stable, 64 hex chars and follows alphabetical order")]
    public void ContentHash_IsStable()
    {
        var a = ContentLoader.LoadEmbedded().ContentHash;
        Assert.Equal(a, ContentLoader.LoadEmbedded().ContentHash);
        Assert.Matches("^[0-9a-f]{64}$", a);
        var files = Files();
        var concat = ContentLoader.FileNames.SelectMany(n => files[n]).ToArray();
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(concat)), a);
        Assert.Equal(ContentLoader.FileNames.Order(StringComparer.Ordinal), ContentLoader.FileNames);
        files["waves.json"] = [.. files["waves.json"], (byte)' '];
        Assert.NotEqual(a, ContentLoader.ComputeHash(files));
    }

    [Fact(DisplayName = "T-12.1 Embedded JSON files use LF line endings (hash portable across OS)")]
    public void EmbeddedFiles_UseLf() =>
        Assert.All(Files(), f => Assert.DoesNotContain((byte)'\r', f.Value));

    [Fact(DisplayName = "T-12.1 Missing file and malformed JSON are reported")]
    public void Load_MissingAndMalformed()
    {
        var files = Files();
        files.Remove("towers.json");
        files["stages.json"] = "[{"u8.ToArray();
        var r = ContentLoader.Load(files);
        AssertError(r, "towers.json: arquivo ausente");
        AssertError(r, "stages.json");
    }

    [Fact(DisplayName = "T-12.1 Unknown or missing JSON members are reported")]
    public void Load_UnknownAndMissingMembers()
    {
        AssertError(With("rules.json", "\"slotRangeDip\": 48,", "\"slotRangeDip\": 48, \"extra\": 1,"), "rules.json");
        AssertError(With("rules.json", "\"slotRangeDip\": 48,", ""), "rules.json");
    }

    [Fact(DisplayName = "T-12.1 Null array element does not crash the loader")]
    public void Load_NullElement()
    {
        var files = Files();
        files["towers.json"] = "[null]"u8.ToArray();
        var r = ContentLoader.Load(files);
        Assert.False(r.IsValid);
    }

    [Fact(DisplayName = "T-12.1 Validator reports each error class")]
    public void Validator_ReportsEveryError()
    {
        AssertError(With("towers.json", "\"cost\": 45", "\"cost\": 0"), "towers.bolt.cost");
        AssertError(With("towers.json", "\"rangeSlots\": 3, \"damage\": 18", "\"rangeSlots\": 0, \"damage\": 18"), "towers.bolt.rangeSlots");
        AssertError(With("towers.json", "\"interval\": 0.9", "\"interval\": 9"), "towers.frost.interval");
        AssertError(With("towers.json", "\"unlockStage\": 9", "\"unlockStage\": 11"), "unlockStage");
        AssertError(With("towers.json", "\"weak\": [\"swarm\"]", "\"weak\": [\"nope\"]"), "kind 'nope'");
        AssertError(With("towers.json", "\"id\": \"frost\"", "\"id\": \"bolt\""), "id duplicado 'bolt'");
        AssertError(With("towers.json", "\"damageBonusPerBolt\": 0.15", "\"damageBonusPerBolt\": null"), "scaleset.damageBonusPerBolt");
        AssertError(With("enemies.json", "\"speed\": 45", "\"speed\": 0"), "enemies.grunt.speed");
        AssertError(With("enemies.json", "\"id\": \"boss\"", "\"id\": \"chief\""), "falta o inimigo 'boss'");
        AssertError(With("waves.json", "\"kind\": \"runner\"", "\"kind\": \"ghost\""), "kind 'ghost'");
        AssertError(With("waves.json", "\"kind\": \"runner\"", "\"kind\": \"tank\""), "duplicado");
        AssertError(With("waves.json", "\"cap\": 0.30 },\n    { \"kind\": \"tank\"", "\"cap\": 0.60 },\n    { \"kind\": \"tank\""), "soma dos cap");
        AssertError(With("waves.json", "\"bossEvery\": 20", "\"bossEvery\": 0"), "bossEvery");
        AssertError(With("waves.json", "\"bossEvery\": 20", "\"bossEvery\": 30"), "múltiplo de bossEvery");
        AssertError(With("waves.json", "\"floor\": 0.28", "\"floor\": 2"), "waves.interval");
        AssertError(With("stages.json", "\"id\": 2,", "\"id\": 5,"), "id da posição 1");
        AssertError(With("stages.json", "\"waves\": 100, \"stageTopic\": \"Elasticidade\"", "\"waves\": 90, \"stageTopic\": \"Elasticidade\""), "múltiplo");
        AssertError(With("regions.json", "\"unlockReserves\": 30", "\"unlockReserves\": null"), "unlockReserves");
        AssertError(With("regions.json", "\"firstMigration\"", "\"sometimes\""), "unlock inválido");
        AssertError(With("missions.json", "\"chestRare\"", "\"gold\""), "reward.kind");
        AssertError(With("missions.json", "\"dailyCount\": 4", "\"dailyCount\": 9"), "dailyCount");
        AssertError(With("missions.json", "\"target\": 200", "\"target\": 0"), "target");
        AssertError(With("economy.json", "\"hpGrowth\": 1.09", "\"hpGrowth\": 1.1"), "economy.hpGrowth");
        AssertError(With("economy.json", "\"buildCostFactor\": 1.5", "\"buildCostFactor\": 2"), "economy.buildCostFactor");
        AssertError(With("economy.json", "\"xpPerLevel\": 45", "\"xpPerLevel\": 50"), "xpPerLevel");
        AssertError(With("rules.json", "\"frostSlowFactor\": 0.65", "\"frostSlowFactor\": 1.5"), "frostSlowFactor");
    }

    [Fact(DisplayName = "T-12.1 All errors are accumulated, not just the first")]
    public void Validator_AccumulatesErrors()
    {
        var files = Files();
        files["enemies.json"] = Encoding.UTF8.GetBytes(Regex.Replace(Encoding.UTF8.GetString(files["enemies.json"]), "\"hp\": \\d+", "\"hp\": 0"));
        var r = ContentLoader.Load(files);
        Assert.Equal(6, r.Errors.Count(x => x.Contains(".hp deve", StringComparison.Ordinal)));
    }

    [Fact(DisplayName = "T-12.1 LoadEmbedded throws ContentException carrying the errors")]
    public void ContentException_CarriesErrors()
    {
        var ex = new ContentException(["a", "b"]);
        Assert.Equal(["a", "b"], ex.Errors);
        Assert.Contains("a; b", ex.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "RNF-12.1 Embedded content is loaded in under 20 ms (warm)")]
    public void Load_IsFast()
    {
        // Robusto a carga (SDD-17: desempenho estrito vive em benchmark): o limite vale para a
        // MELHOR de várias cargas aquecidas; preempção de CPU só infla amostras isoladas.
        ContentLoader.LoadEmbedded();
        long best = long.MaxValue;
        for (int i = 0; i < 25 && best >= 20; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ContentLoader.LoadEmbedded();
            best = Math.Min(best, sw.ElapsedMilliseconds);
        }
        Assert.True(best < 20, $"{best} ms (melhor de até 25)");
    }
}
