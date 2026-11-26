using System.Globalization;
using System.Text;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using FuseTower = SideWar.Core.Td.FuseTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using StartWave = SideWar.Core.Td.StartWave;

namespace SideWar.Core.Tests;

/// <summary>
/// BAL-2: simulação de balanceamento com a economia real (créditos iniciais, bônus de onda, ouro e runas do jogo; nada é injetado).
/// Fica fora da suíte normal (<c>Explicit</c>); para rodar:
/// <c>dotnet test tests/SideWar.Core.Tests -c Release --filter Category=Balance -- xUnit.Explicit=on</c>.
/// <c>BALANCE_OUT</c> grava a tabela num arquivo; <c>BALANCE_ONLY</c> limita os cenários pelo prefixo do nome.
/// </summary>
[Trait("Category", "Balance")]
public sealed class TdBalanceHarnessTests
{
    private static readonly TdContent Content = TdContentLoader.LoadEmbedded();
    private static readonly ulong[] Seeds = [1, 2, 3];

    /// <summary>
    /// <c>Towers</c>: compras em ordem, por região. <c>Fusions</c>: componentes que o excedente funde, uma volta por torre de cada vez
    /// (vazio = formação simples). A chave é o id do conteúdo da região (1 a 6).
    /// </summary>
    private sealed record Formation(string Name, Func<int, string[]> Towers, Func<int, string, string[]> Fusions, bool Focus = false);

    private static string[] Repeat(int count, params string[] cycle) => [.. Enumerable.Range(0, count).Select(i => cycle[i % cycle.Length])];

    private static readonly Formation Simple = new("simples", _ => Repeat(20, "bolt", "cannon"), (_, _) => []);

    private static readonly string[] Base2 = ["bolt", "bolt", "balancer", "cannon", "cannon", "frost", "dns", "cannon", "dns", "frost", "bolt", "dns", "bolt", "bolt", "bolt", "cannon", "cannon", "dns"];

    private static readonly Formation Strategic = new("estrategica", stage => stage switch
    {
        // fase 1: Raio contra o chefe (durável); DNS contra rápidos e enxames; Gelo contra rápidos
        1 => ["bolt", "dns", "bolt", "bolt", "bolt", "bolt", "frost", "bolt", "frost", "bolt", "bolt", "frost", "dns"],
        // fase 2: Canhão contra blindados, Raio contra o chefe, DNS/Gelo/Balanceador contra rápidos
        2 => Base2,
        // fases 3 a 6: mantêm a base e acrescentam o contra de cada região (voadores: CDN/Gelo; chefes: Pipeline/Sentinel)
        3 => [.. Base2, "policy", "cannon", "scaleset"],
        4 => [.. Base2, "policy", "cannon", "pipeline", "cdn", "pipeline", "frost", "scaleset"],
        _ => [.. Base2, "policy", "cannon", "pipeline", "cdn", "pipeline", "frost", "scaleset", "sentinel", "pipeline"],
    }, (_, tower) => tower switch
    {
        "bolt" => ["bolt", "bolt", "frost", "bolt", "cannon"],
        "cannon" => ["bolt", "cannon", "bolt", "cannon", "bolt"],
        "dns" => ["frost", "bolt", "dns", "bolt", "bolt"],
        _ => ["bolt", "bolt", "bolt", "cannon", "bolt"],
    }, Focus: true);

    private sealed record Outcome(bool Won, int StagesCleared, int Region, int Wave, int Leak, long XpLost, int Rewinds, bool BossDefeat,
        int Towers, int Components, long Credits, long Frames);

    private static UInt128 Web(int count) => count >= 128 ? UInt128.MaxValue : (UInt128.One << count) - 1;

    /// <summary>
    /// Joga da região <paramref name="from"/> até concluir <paramref name="to"/> (torres e créditos mantidos entre fases). Derrota: o chefe
    /// chega à base, a última onda volta 3 vezes ou a corrida estoura o tempo. A teia fica fixa (os fragmentos não são gastos).
    /// </summary>
    private static Outcome Play(TdContent content, Formation f, int from, int to, int runes, TdDifficulty difficulty, ulong seed)
    {
        var rank = (TdRankLevel)content.Stages.Stages[from - 1].MinRank;
        var m = TdMatchState.Create(content, from, Rng.Seed(seed, 54), null, TdMatchOptions.Default with
        {
            InitialReputation = TdRank.Threshold(rank), InitialAwakened = Web(runes), Difficulty = difficulty,
        });
        m.Apply(new LayoutChanged(TdSanityBot.TypicalLayout()));
        m.Apply(new SetPresence(true));
        int goal = to - from + 1, region = 0, next = 0, leak = 0, lastLeak = 0;
        string[] plan = [];
        long max = 30L * 3600 * 4 * (goal + 3); // a trava de patente pode repetir regiões
        bool Done() => m.StageResults.Any(r => (r.StageNumber - 1) % content.Stages.Stages.Length + 1 == to);
        while (m.Frame < max && !Done() && m.BossDefeats == 0 && m.BossRewinds < 3)
        {
            if (m.StageId != region)
            {
                region = m.StageId; // torres e fusões da fase anterior ficam: o plano novo só acrescenta o que falta
                plan = f.Towers(region);
                next = 0;
                lastLeak = 0;
            }
            if (m.Paused) m.Apply(new Resume());
            if (m.Phase == MatchPhase.Preparo)
            {
                Spend(m, f, region, plan, ref next);
                m.Apply(new StartWave());
            }
            else if (m.Frame % 15 == 0)
            {
                Spend(m, f, region, plan, ref next);
                Abilities(m);
                if (f.Focus) Focus(m);
            }
            m.Step();
            if (m.IntegrityLost > lastLeak) leak += m.IntegrityLost - lastLeak;
            lastLeak = m.IntegrityLost;
            if (m.Phase is not (MatchPhase.Preparo or MatchPhase.Onda)) break;
        }
        bool won = Done() && m.BossDefeats == 0;
        return new Outcome(won, m.StagesCleared, m.StageId, m.WaveNumber, leak, m.ReputationLostTotal, m.BossRewinds, m.BossDefeats > 0,
            m.Towers.Count, m.Towers.Sum(t => t.ComponentTotal), m.Credits, m.Frame);
    }

    private static int Def(string id) => Array.FindIndex(Content.Towers, t => t.Id == id);

    private static readonly int[] Spots = [900, 800, 700, 600, 1000, 500, 450, 1100, 960, 750, 650, 550, 400, 300, 850, 1050, 350, 250, 200, 100];

    private static void Spend(TdMatchState m, Formation f, int region, string[] plan, ref int next)
    {
        while (next < plan.Length)
        {
            string id = plan[next];
            // já existe (veio da fase anterior)?
            int wanted = plan.Take(next + 1).Count(x => x == id);
            int def = Def(id);
            if (m.Towers.Count(t => t.DefIndex == def) >= wanted || Content.Towers[def].MinRank > m.CurrentRankId) { next++; continue; }
            if (m.Credits < m.NextTowerCost(def)) return;
            Build(m, id, Spots[next % Spots.Length]);
            next++;
        }
        while (Fuse(m, f, region)) { }
    }

    private static void Build(TdMatchState m, string id, int pref)
    {
        for (int d = 0; d <= 1200; d += 11)
            foreach (int x in new[] { pref + d, pref - d })
                if (x is >= 0 and <= 1200 && m.Apply(new BuildTower(id, x)).IsOk) return;
    }

    /// <summary>Funde na torre com menos componentes cuja próxima peça caiba no bolso.</summary>
    private static bool Fuse(TdMatchState m, Formation f, int region)
    {
        if (!m.FusionUnlocked) return false;
        foreach (var t in m.Towers.OrderBy(t => t.ComponentTotal).ThenBy(t => t.Id).ToArray())
        {
            if (t.ComponentTotal >= m.FusionMaxComponents) continue;
            var list = f.Fusions(region, Content.Towers[t.DefIndex].Id);
            if (t.ComponentTotal - 1 >= list.Length) continue;
            var check = m.CheckFusion(t.Id, list[t.ComponentTotal - 1]);
            if (check.Reason == CommandReason.None) return m.Apply(new FuseTower(t.Id, list[t.ComponentTotal - 1])).IsOk;
        }
        return false;
    }

    private static void Abilities(TdMatchState m)
    {
        int near = 0;
        bool boss = false;
        foreach (var e in m.Enemies)
        {
            if (e.X < 700) near++;
            if (e.IsBoss) boss = true;
        }
        foreach (var a in m.AbilityStatuses)
            if (a.Unlocked && a.CooldownTicks == 0 && a.ActiveTicks == 0 && a.UsesLeft != 0 && a.Id is "scale" or "firewall" && (near >= 5 || boss))
                m.Apply(new UseAbility(a.Id));
    }

    /// <summary>Mira da formação estratégica: quem tem vantagem contra chefe foca o mais forte quando há chefe em campo.</summary>
    private static void Focus(TdMatchState m)
    {
        bool boss = m.Enemies.Any(e => e.IsBoss);
        var mode = boss ? TdTargeting.Strong : TdTargeting.First;
        foreach (var t in m.Towers.ToArray())
        {
            var def = Content.Towers[t.DefIndex];
            if (def.Kind == "attack" && def.Strong.Contains("durable") && t.Targeting != (byte)mode) m.Apply(new SideWar.Core.Td.SetTargeting(t.Id, mode));
        }
    }

    private sealed record Scenario(string Name, Formation Formation, int From, int To, int Runes, TdDifficulty Difficulty);

    private static readonly Scenario[] Scenarios =
    [
        new("f1-casual-59-simples", Simple, 1, 1, 59, TdDifficulty.Casual),
        new("f1-casual-59-estrategica", Strategic, 1, 1, 59, TdDifficulty.Casual),
        new("f2-casual-59-simples", Simple, 2, 2, 59, TdDifficulty.Casual),
        new("f2-casual-59-estrategica", Strategic, 2, 2, 59, TdDifficulty.Casual),
        new("f1-extremo-0-estrategica", Strategic, 1, 1, 0, TdDifficulty.Extreme),
        new("f6-casual-128-estrategica", Strategic, 1, 6, 128, TdDifficulty.Casual),
        new("f6-casual-127-estrategica", Strategic, 1, 6, 127, TdDifficulty.Casual),
    ];

    [Fact(Explicit = true)]
    public void Campaign_balance_with_real_economy()
    {
        string only = Environment.GetEnvironmentVariable("BALANCE_ONLY") ?? "";
        var sb = new StringBuilder("cenario                      seed vence fases regiao onda  vaza  xp_perdido voltas chefe torres comp creditos\n");
        var results = new Dictionary<string, List<Outcome>>();
        foreach (var s in Scenarios.Where(s => s.Name.StartsWith(only, StringComparison.Ordinal)))
        {
            var list = results[s.Name] = [];
            foreach (var seed in Seeds)
            {
                var r = Play(Content, s.Formation, s.From, s.To, s.Runes, s.Difficulty, seed);
                list.Add(r);
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"{s.Name,-28} {seed,4} {(r.Won ? "sim" : "nao"),5} {r.StagesCleared,5} {r.Region,6} {r.Wave,4} {r.Leak,5} {r.XpLost,11} {r.Rewinds,6} {(r.BossDefeat ? "sim" : "nao"),5} {r.Towers,6} {r.Components,4} {r.Credits,8}"));
                if (Environment.GetEnvironmentVariable("BALANCE_OUT") is { Length: > 0 } partial) File.WriteAllText(partial, sb.ToString());
            }
        }
        TestContext.Current.TestOutputHelper?.WriteLine(sb.ToString());
        if (only.Length > 0) return;

        foreach (var name in new[] { "f1-casual-59-simples", "f2-casual-59-simples" })
            Assert.All(results[name], r => Assert.True(!r.Won || r.Leak > 0, $"{name} deveria perder ou vazar"));
        foreach (var name in new[] { "f1-casual-59-estrategica", "f2-casual-59-estrategica", "f1-extremo-0-estrategica", "f6-casual-128-estrategica" })
            Assert.All(results[name], r => Assert.True(r.Won, $"{name} deveria vencer"));
        Assert.All(results["f6-casual-127-estrategica"], r => Assert.False(r.Won, "teia incompleta não vence a região final"));
    }
}
