using SideWar.Core.Content.Td;

namespace SideWar.Core.Td;

/// <summary>
/// Bônus das receitas contidas num conjunto de torres: o melhor valor de cada efeito (o fator de lentidão menor é o melhor).
/// <c>Primary</c> é a maior receita contida (empate: a primeira do conteúdo) e dá o nome.
/// </summary>
public sealed record TdFusionBonus(
    TdFusionRecipeDef Primary, double HitDamagePct, double SlowFactor, double SlowSeconds, int ChainJumps, double ChainPct,
    double SplashRadius, double SplashPct, double CorrodePct, double CorrodeSeconds, double DamageBp, double AuraBp, long WaveCredits)
{
    public bool HasHitEffects => HitDamagePct > 0 || SlowFactor > 0 || ChainJumps > 0 || SplashPct > 0 || CorrodePct > 0;

    /// <summary>Descrição curta, pt-BR, do que a receita faz.</summary>
    public string Text
    {
        get
        {
            var parts = new List<string>(4);
            static string Pct(double v) => $"{Math.Round(v * 100):0}%";
            if (DamageBp > 0) parts.Add($"+{Pct(DamageBp / 10000)} de dano");
            if (HitDamagePct > 0) parts.Add($"golpe extra {Pct(HitDamagePct)}");
            if (SlowFactor > 0) parts.Add($"lentidão {Pct(1 - SlowFactor)}");
            if (ChainJumps > 0) parts.Add($"salta {ChainJumps}x");
            if (SplashPct > 0) parts.Add("explosão em área");
            if (CorrodePct > 0) parts.Add($"corrói {Pct(CorrodePct)} da armadura");
            if (AuraBp > 0) parts.Add($"aura +{Pct(AuraBp / 10000)}");
            if (WaveCredits > 0) parts.Add($"+${WaveCredits} por onda");
            return string.Join(", ", parts);
        }
    }

    /// <summary>Junta os efeitos das receitas (a primeira dá o nome).</summary>
    public static TdFusionBonus Merge(IReadOnlyList<TdFusionRecipeDef> recipes)
    {
        double Max(string effect, string key)
        {
            double best = 0;
            foreach (var r in recipes)
                if (r.Effects.TryGetValue(effect, out var p)) best = Math.Max(best, p.GetValueOrDefault(key));
            return best;
        }
        double slow = 0;
        foreach (var r in recipes)
            if (r.Effects.TryGetValue("hitSlow", out var p) && (slow == 0 || p["factor"] < slow)) slow = p["factor"];
        return new(recipes[0], Max("hitDamage", "damagePct"), slow, Max("hitSlow", "seconds"), (int)Max("hitChain", "jumps"), Max("hitChain", "damagePct"),
            Max("hitSplash", "radius"), Max("hitSplash", "damagePct"), Max("hitCorrode", "armorPct"), Max("hitCorrode", "seconds"),
            Max("damageBonus", "bp"), Max("damageAura", "bp"), (long)Max("waveCredits", "credits"));
    }
}

public sealed partial class TdMatchState
{
    private readonly Dictionary<ulong, TdFusionBonus?> _recipeCache = [];

    /// <summary>Bônus da torre (nulo com menos de 3 componentes). Chamado por tick: não aloca quando já em cache.</summary>
    public TdFusionBonus? RecipeBonus(in TdTower t)
    {
        if (t.ComponentTotal < 3) return null;
        Span<int> components = stackalloc int[t.ComponentTotal];
        components[0] = t.DefIndex;
        for (int k = 0; k < t.ModuleCount; k++) components[k + 1] = t.ModuleDef(k);
        return Cached(components);
    }

    /// <summary>Bônus do conjunto (a ordem não importa); nulo se nenhuma receita cabe nele.</summary>
    public TdFusionBonus? RecipeBonus(IReadOnlyList<int> components)
    {
        if (components.Count < 3) return null;
        Span<int> copy = stackalloc int[components.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = components[i];
        return Cached(copy);
    }

    private TdFusionBonus? Cached(Span<int> components)
    {
        if (_content.Fusion.Recipes is not { Length: > 0 }) return null;
        components.Sort();
        ulong key = 0;
        foreach (int d in components) key = key * 64 + (ulong)d + 1;
        if (_recipeCache.TryGetValue(key, out var cached)) return cached;
        var found = RecipesIn(components.ToArray());
        var bonus = found.Count == 0 ? null : TdFusionBonus.Merge(found);
        _recipeCache[key] = bonus;
        return bonus;
    }

    /// <summary>Receitas contidas no conjunto, maiores primeiro (empate: ordem do conteúdo).</summary>
    public IReadOnlyList<TdFusionRecipeDef> RecipesIn(IReadOnlyList<int> components)
    {
        var have = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (int d in components) have[_content.Towers[d].Id] = have.GetValueOrDefault(_content.Towers[d].Id) + 1;
        return [.. (_content.Fusion.Recipes ?? [])
            .Where(r => r.Parts.GroupBy(p => p).All(g => have.GetValueOrDefault(g.Key) >= g.Count()))
            .OrderByDescending(r => r.Parts.Length)];
    }

    /// <summary>Patente mínima de uma receita: a que permite essa quantidade e a de cada torre.</summary>
    public int RecipeMinRank(TdFusionRecipeDef recipe)
    {
        int rank = FusionRankFor(recipe.Parts.Length);
        foreach (var id in recipe.Parts) rank = Math.Max(rank, _content.Towers[TowerDefIndex(id)].MinRank);
        return rank;
    }
}
