using SideWar.Core.Content;

namespace SideWar.Core.Waves;

/// <summary>Um spawn agendado: instante (s desde o início da onda), inimigo e valores já escalados (RF-27.6).</summary>
public readonly record struct WaveSpawn(double Time, EnemyDef Enemy, double Hp, double Gold, double Xp);

/// <summary>Agenda de uma onda (SDD-27 RF-27.7). <c>Spawns</c> está ordenada por tempo.</summary>
public sealed record WavePlan(int Wave, int Count, double Interval, bool IsBoss, WaveSpawn[] Spawns);

/// <summary>
/// Monta a agenda de cada onda (SDD-27 RF-27.6/27.7, SDD-12 RF-12.8). Todos os parâmetros vêm do conteúdo
/// (<see cref="GameContent"/>); potências por multiplicação iterativa, sem <c>Math.Pow</c>.
/// </summary>
public sealed class IdleWaveBuilder
{
    private const string GruntId = "grunt";
    private const string BossId = "boss";

    private readonly WavesDef _waves;
    private readonly EconomyDef _economy;
    private readonly Dictionary<string, EnemyDef> _enemies;

    public IdleWaveBuilder(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _waves = content.Waves;
        _economy = content.Economy;
        _enemies = content.Enemies.ToDictionary(e => e.Id, StringComparer.Ordinal);
    }

    /// <summary>N(w) = min(cap, base + floor(w / perWaves)); em onda de chefe, o total inclui o chefe (<see cref="Build"/>).</summary>
    public int BaseCount(int wave) => Math.Min(_waves.Count.Cap, _waves.Count.Base + wave / _waves.Count.PerWaves);

    public bool IsBossWave(int wave) => wave > 0 && wave % _waves.BossEvery == 0;

    /// <summary>Intervalo entre spawns: max(floor, start + slope × p), p = (w−1)/(max(2,total)−1) limitado a [0,1].</summary>
    public double Interval(int wave, int total)
        => Math.Max(_waves.Interval.Floor, _waves.Interval.Start + _waves.Interval.Slope * Progress(wave, total));

    /// <param name="wave">Onda 1..total.</param>
    /// <param name="total">Ondas da fase.</param>
    /// <param name="stageIndex">Índice da fase, 0..9.</param>
    /// <param name="regionHpMultiplier">Multiplicador de HP da região (RF-27.10).</param>
    public WavePlan Build(int wave, int total, int stageIndex, double regionHpMultiplier = 1.0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(wave, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(stageIndex, 0);

        var p = Progress(wave, total);
        var boss = IsBossWave(wave);
        var n = BaseCount(wave);
        var regular = boss ? Math.Max(_waves.BossRegularMin, Round(n * _waves.BossRegularFactor)) : n;

        // Grupos de mistura (ordem do JSON); o resto é grunt.
        var counts = new int[_waves.Mix.Length];
        var sum = 0;
        for (var m = 0; m < counts.Length; m++)
        {
            var mix = _waves.Mix[m];
            var share = Math.Min(mix.Cap, Math.Max(0.0, (p - mix.Start) * mix.Slope));
            counts[m] = Round(regular * share);
            sum += counts[m];
        }

        // Excesso: reduzir os grupos na ordem inversa até zerar.
        for (var m = counts.Length - 1; m >= 0 && sum > regular; m--)
        {
            var cut = Math.Min(counts[m], sum - regular);
            counts[m] -= cut;
            sum -= cut;
        }

        // Itens (pos, ordem do grupo): grunt = 0, mix = índice + 1.
        var items = new List<(double Pos, int Order, string Kind)>(regular + 1);
        AddGroup(items, 0, GruntId, regular - sum);
        for (var m = 0; m < counts.Length; m++)
            AddGroup(items, m + 1, _waves.Mix[m].Kind, counts[m]);
        items.Sort(static (a, b) => a.Pos != b.Pos ? a.Pos.CompareTo(b.Pos) : a.Order.CompareTo(b.Order));

        var kinds = items.Select(i => i.Kind).ToList();
        if (boss)
            kinds.Insert(kinds.Count / 2, BossId);

        var hpFactor = HpFactor(wave, stageIndex, regionHpMultiplier);
        var goldFactor = Pow(_economy.GoldGrowth, wave - 1);
        var xpFactor = Pow(_economy.XpGrowth, wave - 1);
        var interval = Interval(wave, total);

        var spawns = new WaveSpawn[kinds.Count];
        for (var i = 0; i < spawns.Length; i++)
        {
            var e = _enemies[kinds[i]];
            spawns[i] = new WaveSpawn(i * interval, e, e.Hp * hpFactor, e.Gold * goldFactor, e.Xp * xpFactor);
        }

        return new WavePlan(wave, spawns.Length, interval, boss, spawns);
    }

    /// <summary>Fator de HP da onda (RF-27.6): <c>hpGrowth^(w−1) × (1 + stageHpStep × fase) × região</c>. Usado também nos extras de pico.</summary>
    public double HpFactor(int wave, int stageIndex, double regionHpMultiplier = 1.0)
        => Pow(_economy.HpGrowth, wave - 1) * (1 + _economy.StageHpStep * stageIndex) * regionHpMultiplier;

    private static void AddGroup(List<(double, int, string)> items, int order, string kind, int count)
    {
        for (var j = 0; j < count; j++)
            items.Add(((j + 0.5) / count, order, kind));
    }

    private static double Progress(int wave, int total)
        => Math.Clamp((wave - 1) / (double)(Math.Max(2, total) - 1), 0.0, 1.0);

    private static int Round(double x) => (int)Math.Round(x, MidpointRounding.AwayFromZero);

    private static double Pow(double b, int e)
    {
        var r = 1.0;
        for (var i = 0; i < e; i++)
            r *= b;
        return r;
    }
}
