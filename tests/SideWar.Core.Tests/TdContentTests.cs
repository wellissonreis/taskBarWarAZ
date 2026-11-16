using System.Text;
using SideWar.Core.Content;
using SideWar.Core.Content.Td;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class TdContentTests
{
    private static TdContentLoadResult With(string file, string find, string replace)
    {
        var files = TdContentLoader.ReadEmbedded();
        var text = Encoding.UTF8.GetString(files[file]);
        Assert.Contains(find, text);
        files[file] = Encoding.UTF8.GetBytes(text.Replace(find, replace, StringComparison.Ordinal));
        return TdContentLoader.Load(files);
    }

    private static void AssertError(TdContentLoadResult r, string fragment)
    {
        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, x => x.Contains(fragment, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "TD.1 Embedded TD content passes the validator and has the expected catalog")]
    public void Embedded_IsValid()
    {
        var c = TdContentLoader.LoadEmbedded();
        Assert.Equal(5, c.Towers.Length);
        Assert.Equal(8, c.Enemies.Enemies.Length);
        Assert.Equal(3, c.Enemies.Bosses.Length);
        Assert.Equal(6, c.Stages.Stages.Length);
        Assert.Equal(3, c.Abilities.Length);
        Assert.Equal(3, c.Meta.Branches.Length);
        Assert.All(c.Meta.Branches, b => Assert.Equal(4, b.Nodes.Length));
        Assert.Equal([10, 12, 12, 14, 14, 15], c.Waves.Stages.Select(s => s.Waves.Length));
        Assert.Equal(20, c.Stages.Rules.CoreIntegrity);
        Assert.Equal(0.7, c.Stages.Rules.SellRefund);
        Assert.Equal(4, c.Towers.Single(t => t.Id == "scaleset").UnlockStage);
        Assert.Equal(2, c.Towers.Single(t => t.Id == "balancer").UnlockStage);
    }

    [Fact(DisplayName = "TD.1 / CA-28.4 Every tower has 3 linear levels, branches A/B and a capstone with declared weakness")]
    public void Towers_HaveBranchesCapstonesAndWeakness()
    {
        var c = TdContentLoader.LoadEmbedded();
        foreach (var t in c.Towers)
        {
            Assert.Equal(3, t.Levels.Length);
            Assert.Equal(["A", "B"], t.Branches.Select(b => b.Id));
            Assert.False(string.IsNullOrWhiteSpace(t.WeaknessText));
            Assert.NotEmpty(t.Weak);
            Assert.All(t.Branches, b =>
            {
                Assert.False(string.IsNullOrWhiteSpace(b.Weakness));
                Assert.NotEmpty(b.WeakAgainst);
                Assert.False(string.IsNullOrWhiteSpace(b.Capstone.Effect));
            });
        }
    }

    [Fact(DisplayName = "TD.1 / CA-28.5 Every enemy and boss has a counter and at least one tower or branch that suffers against it")]
    public void Enemies_HaveCountersAndSufferers()
    {
        var c = TdContentLoader.LoadEmbedded();
        var suffered = c.Towers.SelectMany(t => t.Weak.Concat(t.Branches.SelectMany(b => b.WeakAgainst))).ToHashSet();
        foreach (var x in c.Enemies.Enemies)
        {
            Assert.NotEmpty(x.Counters);
            Assert.True(x.Traits.Contains("basic") || x.Traits.Any(suffered.Contains), x.Id);
        }
        foreach (var b in c.Enemies.Bosses)
        {
            Assert.NotEmpty(b.Counters);
            Assert.True(b.Traits.Any(suffered.Contains), b.Id);
        }
    }

    [Fact(DisplayName = "TD.1 Authored waves: bosses close their stage and concepts are introduced in order")]
    public void Waves_AreAuthoredPerStage()
    {
        var c = TdContentLoader.LoadEmbedded();
        string[] LastWave(int stage) => c.Waves.Stages[stage - 1].Waves[^1].Groups.Select(g => g.Enemy).ToArray();
        Assert.Contains("ransomware", LastWave(3));
        Assert.Contains("botnet", LastWave(5));
        Assert.Contains("zeroday", LastWave(6));
        Assert.DoesNotContain(c.Waves.Stages[0].Waves.SelectMany(w => w.Groups), g => g.Enemy is not ("grunt" or "runner"));
        Assert.DoesNotContain(c.Waves.Stages[2].Waves.SelectMany(w => w.Groups), g => g.Enemy is "flyer" or "regen" or "leech");
        Assert.All(c.Waves.Stages, s => Assert.Contains(s.Waves, w => w.Rest));
    }

    [Fact(DisplayName = "TD.1 / CA-28.4 Validator rejects a branch without declared weakness")]
    public void Validator_RejectsBranchWithoutWeakness()
    {
        AssertError(With("td-towers.json", "\"weakness\": \"Chefes são imunes à paralisia.\"", "\"weakness\": \" \""), "td-towers.frost.A.weakness");
        AssertError(With("td-towers.json", "\"weakAgainst\": [\"armored\"]", "\"weakAgainst\": []"), "td-towers.bolt.B.weakAgainst");
    }

    [Fact(DisplayName = "TD.1 Validator rejects a tower without declared weakness or with an unknown trait")]
    public void Validator_RejectsTowerWithoutWeakness()
    {
        AssertError(With("td-towers.json", "\"weak\": [\"swarm\"], \"weaknessText\"", "\"weak\": [], \"weaknessText\""), "td-towers.bolt.weak");
        AssertError(With("td-towers.json", "\"weak\": [\"flying\"], \"weaknessText\": \"Não segura", "\"weak\": [\"fogo\"], \"weaknessText\": \"Não segura"), "traço desconhecido 'fogo'");
        AssertError(With("td-towers.json", "\"weaknessText\": \"Dano quase nulo: não mata sozinha; chefes resistem ao controle.\"", "\"weaknessText\": \"\""), "td-towers.frost.weaknessText");
    }

    [Fact(DisplayName = "TD.1 Validator rejects an enemy without counter or with an unknown counter reference")]
    public void Validator_RejectsEnemyWithoutCounter()
    {
        AssertError(With("td-enemies.json", "\"counters\": [\"bolt\", \"bolt.A\"]", "\"counters\": []"), "td-enemies.leech.counters");
        AssertError(With("td-enemies.json", "\"counters\": [\"bolt\", \"frost\"]", "\"counters\": [\"laser\"]"), "'laser' não existe");
    }

    [Fact(DisplayName = "TD.1 Validator rejects an enemy against which no tower or branch is weak")]
    public void Validator_RejectsEnemyWithoutSufferer()
    {
        // Remove "drain" de toda fraqueza declarada: o Parasita deixa de ter quem sofra contra ele.
        var files = TdContentLoader.ReadEmbedded();
        var text = Encoding.UTF8.GetString(files["td-towers.json"]).Replace("\"weak\": [\"drain\"]", "\"weak\": [\"boss\"]", StringComparison.Ordinal);
        files["td-towers.json"] = Encoding.UTF8.GetBytes(text);
        AssertError(TdContentLoader.Load(files), "td-enemies.leech: nenhuma torre ou ramo declara fraqueza");
    }

    [Fact(DisplayName = "TD.1 Validator rejects missing files, unknown properties and malformed JSON")]
    public void Validator_RejectsBadFiles()
    {
        var files = TdContentLoader.ReadEmbedded();
        files.Remove("td-meta.json");
        AssertError(TdContentLoader.Load(files), "td-meta.json: arquivo ausente");
        AssertError(With("td-abilities.json", "\"id\": \"scale\"", "\"id\": \"scale\", \"extra\": 1"), "td-abilities.json");
        files = TdContentLoader.ReadEmbedded();
        files["td-waves.json"] = Encoding.UTF8.GetBytes("{ nope");
        AssertError(TdContentLoader.Load(files), "td-waves.json");
    }

    [Fact(DisplayName = "TD.1 Validator checks wave authoring rules")]
    public void Validator_ChecksWaves()
    {
        AssertError(With("td-waves.json", "{\"enemy\": \"grunt\", \"count\": 4, \"offset\": 0.0, \"interval\": 2.53}", "{\"enemy\": \"flyer\", \"count\": 4, \"offset\": 0.0, \"interval\": 2.53}"), "só é introduzido na fase 4");
        AssertError(With("td-waves.json", "{\"enemy\": \"grunt\", \"count\": 4, \"offset\": 0.0, \"interval\": 2.53}", "{\"enemy\": \"fantasma\", \"count\": 4, \"offset\": 0.0, \"interval\": 2.53}"), "'fantasma' não existe");
        AssertError(With("td-waves.json", "{\"enemy\": \"grunt\", \"count\": 4, \"offset\": 0.0, \"interval\": 2.53}", "{\"enemy\": \"grunt\", \"count\": 1, \"offset\": 0.0, \"interval\": 2.53}"), "fora de [3, 32]");
        AssertError(With("td-stages.json", "\"waves\": 10,", "\"waves\": 11,"), "td-waves.1");
        AssertError(With("td-waves.json", "\"number\": 5, \"rest\": true, \"bonusFactor\": 1.5, \"groups\": [{\"enemy\": \"grunt\", \"count\": 3,", "\"number\": 5, \"rest\": false, \"bonusFactor\": 1.0, \"groups\": [{\"enemy\": \"grunt\", \"count\": 3,"), "sem respiro");
    }

    [Fact(DisplayName = "TD.1 Validator checks the meta tree and abilities")]
    public void Validator_ChecksMetaAndAbilities()
    {
        AssertError(With("td-meta.json", "\"cost\": 4, \"effects\": [ { \"kind\": \"autoStartBonusPercent\"", "\"cost\": 3, \"effects\": [ { \"kind\": \"autoStartBonusPercent\""), "td-meta.operation.autostart.cost");
        AssertError(With("td-meta.json", "\"kind\": \"unlockAbility\", \"value\": 1, \"target\": \"snapshot\"", "\"kind\": \"unlockAbility\", \"value\": 1, \"target\": \"nada\""), "habilidade inexistente 'nada'");
        AssertError(With("td-abilities.json", "\"unlockMetaNode\": \"operation.snapshot\"", "\"unlockMetaNode\": \"operation.nada\""), "nó inexistente");
    }

    [Fact(DisplayName = "TD.1 / T-12.4 TdContentHash is stable, 64 hex chars, ordered by name and independent of the idle hash")]
    public void Hash_IsStableAndOrdered()
    {
        var files = TdContentLoader.ReadEmbedded();
        var a = TdContentLoader.ComputeHash(files);
        Assert.Matches("^[0-9a-f]{64}$", a);
        Assert.Equal(a, TdContentLoader.LoadEmbedded().ContentHash);
        Assert.Equal(a, TdContentLoader.ComputeHash(files.Reverse().ToDictionary(x => x.Key, x => x.Value)));
        var changed = new Dictionary<string, byte[]>(files) { ["td-meta.json"] = Encoding.UTF8.GetBytes(" " + Encoding.UTF8.GetString(files["td-meta.json"])) };
        Assert.NotEqual(a, TdContentLoader.ComputeHash(changed));
        Assert.NotEqual(a, ContentLoader.LoadEmbedded().ContentHash);
        Assert.DoesNotContain(ContentLoader.FileNames, n => n.StartsWith("td-", StringComparison.Ordinal));
    }
}
