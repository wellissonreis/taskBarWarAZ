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
        Assert.DoesNotContain(c.Waves.Stages[0].Waves.SelectMany(w => w.Groups), g => g.Enemy is not ("grunt" or "runner"));
        Assert.DoesNotContain(c.Waves.Stages[2].Waves.SelectMany(w => w.Groups), g => g.Enemy is "flyer" or "regen" or "leech");
        Assert.All(c.Waves.Stages, s => Assert.Contains(s.Waves, w => w.Rest));
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
        Assert.Contains("td-runes.json", TdContentLoader.FileNames); // a página de runas entra no ContentHash (SDD-29 §15)
        var runesChanged = new Dictionary<string, byte[]>(files) { ["td-runes.json"] = Encoding.UTF8.GetBytes(" " + Encoding.UTF8.GetString(files["td-runes.json"])) };
        Assert.NotEqual(a, TdContentLoader.ComputeHash(runesChanged));
        Assert.DoesNotContain(ContentLoader.FileNames, n => n.StartsWith("td-", StringComparison.Ordinal));
    }

    private static readonly string[] SectorTowers = ["bolt", "frost", "cannon", "balancer", "scaleset"];

    private const string BoltS1 = "\"id\": \"bolt.s1\", \"sector\": \"bolt\", \"type\": \"small\", \"name\": \"Dano\", \"icon\": \"damage\", \"text\": \"+6%\", \"cost\": 40, \"links\": [\"core\"]";

    [Fact(DisplayName = "TD.19 / CA-29.4 Embedded rune page has 61 nodes, 6 sectors of 9 nodes, 6 borders and the SDD-29 costs")]
    public void Runes_HaveTheRadialShape()
    {
        var r = TdContentLoader.LoadEmbedded().Runes;
        Assert.Equal(61, r.Nodes.Length);
        Assert.Equal(["bolt", "frost", "cannon", "balancer", "scaleset", "general"], r.Sectors.Select(s => s.Id));
        Assert.Equal(1, r.Nodes.Count(n => n.Type == "center"));
        Assert.Equal(36, r.Nodes.Count(n => n.Type == "small"));
        Assert.Equal(12, r.Nodes.Count(n => n.Type == "notable"));
        Assert.Equal(6, r.Nodes.Count(n => n.Type == "keystone"));
        Assert.Equal(6, r.Nodes.Count(n => n.Type == "border"));
        Assert.All(r.Sectors, s => Assert.Equal(9, r.Nodes.Count(n => n.Sector == s.Id)));
        Assert.Equal((40, 120, 120, 200), (r.Costs.Small, r.Costs.Notable, r.Costs.Border, r.Costs.Keystone));
        Assert.All(r.Nodes.Where(n => n.Type != "center"), n => Assert.Equal(
            n.Type switch { "small" => 40, "notable" => 120, "keystone" => 200, _ => 120 }, n.Cost));
        Assert.Equal(680, r.Nodes.Where(n => n.Sector == "bolt").Sum(n => n.Cost)); // setor completo
        Assert.Equal(4800, r.Nodes.Sum(n => n.Cost)); // página inteira
        Assert.Equal(8500, r.SellRefundCapBp);
    }

    [Fact(DisplayName = "TD.19 / CA-29.4 Rune graph is connected from the centre, borders join two sectors, and rune text is short")]
    public void Runes_GraphIsConnectedAndShort()
    {
        var r = TdContentLoader.LoadEmbedded().Runes;
        var adj = r.Nodes.ToDictionary(n => n.Id, _ => new HashSet<string>());
        foreach (var n in r.Nodes)
            foreach (var l in n.Links) { adj[n.Id].Add(l); adj[l].Add(n.Id); }
        var seen = new HashSet<string> { r.CenterId };
        var q = new Queue<string>([r.CenterId]);
        while (q.Count > 0)
            foreach (var x in adj[q.Dequeue()]) if (seen.Add(x)) q.Enqueue(x);
        Assert.Equal(r.Nodes.Length, seen.Count);
        var byId = r.Nodes.ToDictionary(n => n.Id);
        Assert.All(r.Nodes.Where(n => n.Type == "border"), n => Assert.Equal(2, n.Links.Select(l => byId[l].Sector).Distinct().Count()));
        Assert.All(r.Nodes.Where(n => n.Type is "notable" or "keystone" or "border"),
            n => Assert.InRange(n.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, 1, 6));
        Assert.All(r.Nodes.Where(n => n.Type != "center"), n => Assert.NotEmpty(n.Effects));
        Assert.All(r.Nodes.SelectMany(n => n.Effects).Where(x => x.Target is not null), x => Assert.Contains(x.Target, SectorTowers));
    }

    [Fact(DisplayName = "TD.19 / CA-29.4 Validator rejects disconnected graph, wrong cost, unknown effect and the anti-exploit breach")]
    public void Runes_ValidatorRejects()
    {
        AssertError(With("td-runes.json", "\"links\": [\"bolt.s3\", \"bolt.s5\"]", "\"links\": [\"bolt.k\"]"), "td-runes.bolt.k: nó inalcançável a partir do centro");
        AssertError(With("td-runes.json", BoltS1, BoltS1.Replace("\"cost\": 40", "\"cost\": 41", StringComparison.Ordinal)), "td-runes.bolt.s1.cost deve ser 40");
        AssertError(With("td-runes.json", "\"kind\": \"superHitEvery\"", "\"kind\": \"laser\""), "efeito desconhecido 'laser'");
        AssertError(With("td-runes.json", "\"links\": [\"bolt.n2\", \"frost.n1\"]", "\"links\": [\"bolt.n2\", \"bolt.n1\"]"), "liga exatamente 2 setores diferentes");
        AssertError(With("td-runes.json", "\"links\": [\"bolt.n2\", \"frost.n1\"]", "\"links\": [\"bolt.n2\", \"nada\"]"), "nó inexistente 'nada'");
        AssertError(With("td-runes.json", "\"text\": \"Ignora 30% de armadura\"", "\"text\": \"um dois tres quatro cinco seis sete\""), "no máximo 6 palavras");
        AssertError(With("td-runes.json", "\"sellRefundCapBp\": 8500", "\"sellRefundCapBp\": 9500"), "anti-exploit");
        AssertError(With("td-runes.json", "\"sellRefundCapBp\": 8500", "\"sellRefundCapBp\": 9500"), "9500 bp");
        var files = TdContentLoader.ReadEmbedded();
        files.Remove("td-runes.json");
        AssertError(TdContentLoader.Load(files), "td-runes.json: arquivo ausente");
    }

    [Fact(DisplayName = "TD.19 / CA-29.2 Tower cost tables are the integer 1.25^n and 1.20^n curves (n = 0..47)")]
    public void TowerCostTables_AreIntegerCurves()
    {
        var r = TdContentLoader.LoadEmbedded().Stages.Rules;
        Assert.Equal(48, r.TowerCostTable.Length);
        Assert.Equal(48, r.TowerCostTableLow.Length);
        Assert.Equal([100, 125, 156, 195, 244, 305, 381, 477, 596, 745, 931, 1164, 1455], r.TowerCostTable.Take(13));
        Assert.Equal([100, 120, 144, 173, 207, 249, 299, 358, 430, 516, 619, 743, 892], r.TowerCostTableLow.Take(13));
        Assert.All(Enumerable.Range(0, 48), n => Assert.True(r.TowerCostTableLow[n] <= r.TowerCostTable[n]));
        AssertError(With("td-stages.json", "\"towerCostTable\": [100, 125,", "\"towerCostTable\": [100, 126,"), "towerCostTable[1] deve ser 125");
        AssertError(With("td-stages.json", "\"towerCostTableLow\": [100, 120, 144, 173,", "\"towerCostTableLow\": [100, 120, 144,"), "towerCostTableLow deve ter 48 entradas");
    }

    [Fact(DisplayName = "TD.19 / SDD-29 §6 Credit-source targets are stored in td-runes.json (not yet wired into the Core)")]
    public void CreditSources_AreStored()
    {
        var c = TdContentLoader.LoadEmbedded().Runes.CreditSources;
        Assert.Equal((80, 60, 5), (c.StartingCreditsBonus, c.WaveBonusBase, c.WaveBonusPerWave));
        Assert.Equal((40, 2, 40, 2), (c.NoLeakBase, c.NoLeakPerWave, c.CrateBase, c.CratePerWave));
        Assert.Equal((40, 3, 120), (c.BonusQuestionBase, c.BonusQuestionPerWave, c.BossCredits));
    }
}
