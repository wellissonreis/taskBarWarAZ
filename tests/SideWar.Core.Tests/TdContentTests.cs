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
        Assert.Equal(22, c.Towers.Length);
        Assert.Equal(23, c.Enemies.Enemies.Length);
        Assert.Equal(9, c.Enemies.Bosses.Length);
        Assert.Equal(6, c.Stages.Stages.Length);
        Assert.Equal(3, c.Abilities.Length);
        Assert.Equal(3, c.Meta.Branches.Length);
        Assert.All(c.Meta.Branches, b => Assert.Equal(4, b.Nodes.Length));
        Assert.Equal([60, 65, 80, 85, 70, 62], c.Waves.Stages.Select(s => s.Waves.Length));
        Assert.Equal(20, c.Stages.Rules.CoreIntegrity);
        Assert.Equal(0.7, c.Stages.Rules.SellRefund);
        Assert.Equal(3, c.Towers.Single(t => t.Id == "scaleset").UnlockStage);
        Assert.Equal(2, c.Towers.Single(t => t.Id == "balancer").UnlockStage);
    }

    [Fact(DisplayName = "TD.20 / CA-29.1 Towers have no levels, branches or capstones, and every tower declares a weakness")]
    public void Towers_HaveNoLevelsBranchesAndDeclareWeakness()
    {
        var c = TdContentLoader.LoadEmbedded();
        var text = Encoding.UTF8.GetString(TdContentLoader.ReadEmbedded()["td-towers.json"]);
        Assert.DoesNotContain("\"levels\"", text);
        Assert.DoesNotContain("\"branches\"", text);
        Assert.DoesNotContain("\"capstone\"", text);
        Assert.DoesNotContain("\"maxTowers\"", Encoding.UTF8.GetString(TdContentLoader.ReadEmbedded()["td-stages.json"]).Split("\"challenges\"")[0]);
        foreach (var t in c.Towers)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.WeaknessText));
            Assert.NotEmpty(t.Weak);
        }
    }

    [Fact(DisplayName = "TD.1 / CA-28.5 Every enemy and boss has a counter and at least one tower or branch that suffers against it")]
    public void Enemies_HaveCountersAndSufferers()
    {
        var c = TdContentLoader.LoadEmbedded();
        var suffered = c.Towers.SelectMany(t => t.Weak).ToHashSet();
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
        Assert.DoesNotContain(c.Waves.Stages[0].Waves.SelectMany(w => w.Groups), g => g.Enemy is not ("grunt" or "runner" or "swarm" or "synflood" or "chefe_botnet" or "aliado_botnet"));
        Assert.DoesNotContain(c.Waves.Stages[2].Waves.SelectMany(w => w.Groups), g => g.Enemy is "flyer" or "regen" or "leech");
        Assert.All(c.Waves.Stages, s => Assert.Contains(s.Waves, w => w.Rest));
    }

    [Fact(DisplayName = "TD.39a estreias e contra-ataques respeitam as fronteiras de fase")]
    public void ApprovedExistingCurve()
    {
        var c = TdContentLoader.LoadEmbedded();
        Assert.Equal(2, c.Fusion.MinStage);
        foreach (var (id, stage) in new[] { ("functions", 1), ("policy", 2), ("scaleset", 3) })
            Assert.Equal(stage, c.Towers.Single(t => t.Id == id).UnlockStage);
        foreach (var (id, stage) in new[] { ("swarm", 1), ("flyer", 2), ("trojan", 3), ("regen", 4), ("synflood", 1), ("scanner", 2), ("slowloris", 3), ("fragment", 4), ("dnstunnel", 5), ("encrypted", 6) })
        {
            var enemy = c.Enemies.Enemies.Single(e => e.Id == id);
            Assert.Equal(stage, enemy.IntroStage);
            var first = c.Waves.Stages.First(s => s.Waves.Any(w => w.Groups.Any(g => g.Enemy == id)));
            Assert.Equal(stage, first.Stage);
            Assert.Contains(enemy.Counters, counter => c.Towers.Any(t => t.Id == counter && t.UnlockStage <= stage));
        }
    }

    [Fact(DisplayName = "TD.39d parâmetros dos novos traços são validados")]
    public void NewEnemyMechanicsAreValidated()
    {
        AssertError(With("td-enemies.json", "\"copyHpFraction\": 0.35", "\"copyHpFraction\": 0"), "copyHpFraction");
        AssertError(With("td-enemies.json", "\"revealRange\": 55", "\"revealRange\": 0"), "revealRange");
        AssertError(With("td-enemies.json", "\"shieldHp\": 80", "\"shieldHp\": 0"), "shieldHp");
    }

    [Fact(DisplayName = "TD.1 Validator rejects a tower without declared weakness or with an unknown trait")]
    public void Validator_RejectsTowerWithoutWeakness()
    {
        AssertError(With("td-towers.json", "\"weak\": [\"swarm\", \"replicator\", \"armored\"], \"weaknessText\"", "\"weak\": [], \"weaknessText\""), "td-towers.bolt.weak");
        AssertError(With("td-towers.json", "\"weak\": [\"flying\"], \"weaknessText\": \"Não segura", "\"weak\": [\"fogo\"], \"weaknessText\": \"Não segura"), "traço desconhecido 'fogo'");
        AssertError(With("td-towers.json", "\"weaknessText\": \"Dano quase nulo: não mata sozinha; chefes resistem ao controle.\"", "\"weaknessText\": \"\""), "td-towers.frost.weaknessText");
    }

    [Fact(DisplayName = "TD.1 Validator rejects an enemy without counter or with an unknown counter reference")]
    public void Validator_RejectsEnemyWithoutCounter()
    {
        AssertError(With("td-enemies.json", "\"counters\": [\"bolt\"]", "\"counters\": []"), "td-enemies.leech.counters");
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
        AssertError(With("td-waves.json", "{\"enemy\": \"grunt\", \"count\": 3, \"offset\": 0.0, \"interval\": 4.0}", "{\"enemy\": \"flyer\", \"count\": 4, \"offset\": 0.0, \"interval\": 2.53}"), "só é introduzido na fase 2");
        AssertError(With("td-waves.json", "{\"enemy\": \"grunt\", \"count\": 3, \"offset\": 0.0, \"interval\": 4.0}", "{\"enemy\": \"fantasma\", \"count\": 4, \"offset\": 0.0, \"interval\": 2.53}"), "'fantasma' não existe");
        AssertError(With("td-waves.json", "{\"enemy\": \"grunt\", \"count\": 3, \"offset\": 0.0, \"interval\": 4.0}", "{\"enemy\": \"grunt\", \"count\": 1, \"offset\": 0.0, \"interval\": 2.53}"), "fora de [3, 32]");
        AssertError(With("td-stages.json", "\"waves\": 60,", "\"waves\": 61,"), "td-waves.1");
        AssertError(With("td-waves.json", "\"number\": 5, \"rest\": true, \"bonusFactor\": 1.7, \"groups\": [{\"enemy\": \"grunt\", \"count\": 3,", "\"number\": 5, \"rest\": false, \"bonusFactor\": 1.0, \"groups\": [{\"enemy\": \"grunt\", \"count\": 3,"), "sem respiro");
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
        Assert.Contains("td-runes.json", TdContentLoader.FileNames); // a página de runas entra no ContentHash (SDD-29 §15)
        var runesChanged = new Dictionary<string, byte[]>(files) { ["td-runes.json"] = Encoding.UTF8.GetBytes(" " + Encoding.UTF8.GetString(files["td-runes.json"])) };
        Assert.NotEqual(a, TdContentLoader.ComputeHash(runesChanged));
        Assert.DoesNotContain(ContentLoader.FileNames, n => n.StartsWith("td-", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "TD.29a Validator rejects a broken rune web (unknown type/effect, duplicate slot, bad id, missing fragments rule)")]
    public void Runes_ValidatorRejects()
    {
        AssertError(With("td-runes.json", "\"type\": \"shield\"", "\"type\": \"laser\""), "tipo inexistente 'laser'");
        AssertError(With("td-runes.json", "\"effect\": \"firewallTenths\"", "\"effect\": \"laser\""), "effect desconhecido 'laser'");
        AssertError(With("td-runes.json", "\"id\": \"1-3\", \"ring\": 1, \"sector\": 3", "\"id\": \"1-3\", \"ring\": 1, \"sector\": 2"), "id deve ser 'anel-setor'");
        AssertError(With("td-runes.json", "\"ring\": 3, \"sector\": 7", "\"ring\": 8, \"sector\": 7"), "fora da teia");
        AssertError(With("td-stages.json", "\"fragments\": { \"perWave\": 1,", "\"fragments\": { \"perWave\": -1,"), "fragments: valores devem ser >= 0");
        var files = TdContentLoader.ReadEmbedded();
        files.Remove("td-runes.json");
        AssertError(TdContentLoader.Load(files), "td-runes.json: arquivo ausente");
    }

    [Fact(DisplayName = "TD.22 / CA-29.2 Tower cost tables accelerate after the fourth tower (n = 0..47)")]
    public void TowerCostTables_AreIntegerCurves()
    {
        var r = TdContentLoader.LoadEmbedded().Stages.Rules;
        Assert.Equal(48, r.TowerCostTable.Length);
        Assert.Equal(48, r.TowerCostTableLow.Length);
        Assert.Equal([100, 125, 156, 195, 268, 369, 507, 698, 960, 1320, 1814, 2495, 3431], r.TowerCostTable.Take(13));
        Assert.Equal([100, 120, 144, 173, 228, 301, 398, 524, 693, 914, 1206, 1593, 2103], r.TowerCostTableLow.Take(13));
        Assert.All(Enumerable.Range(0, 48), n => Assert.True(r.TowerCostTableLow[n] <= r.TowerCostTable[n]));
        AssertError(With("td-stages.json", "\"towerCostTable\": [100, 125,", "\"towerCostTable\": [100, 99,"), "towerCostTable deve ser estritamente crescente");
        AssertError(With("td-stages.json", "\"towerCostTableLow\": [100, 120, 144, 173,", "\"towerCostTableLow\": [100, 120, 144,"), "towerCostTableLow deve ter 48 entradas");
    }

    [Fact(DisplayName = "Créditos: fontes calibradas para decisões espaçadas")]
    public void CreditSources_AreStored()
    {
        var c = TdContentLoader.LoadEmbedded().Runes.CreditSources;
        Assert.Equal((80, 50, 3), (c.StartingCreditsBonus, c.WaveBonusBase, c.WaveBonusPerWave));
        Assert.Equal((15, 1), (c.NoLeakBase, c.NoLeakPerWave));
        Assert.Equal((35, 2, 90), (c.BonusQuestionBase, c.BonusQuestionPerWave, c.BossCredits));
    }
}
