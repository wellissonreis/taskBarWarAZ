using SideWar.Core.Content;
using SideWar.Core.Waves;

namespace SideWar.Core.Simulation;

/// <summary>Parâmetros resolvidos de uma torre (towers.json) para o tick: sem busca por string no tick.</summary>
public readonly record struct TowerParams(double Damage, float RangeDip, float Interval, double DamageBonusPerBolt, long Cost);

/// <summary>
/// Conteúdo resolvido uma vez (fora do tick) e somente leitura: regras, torres por <see cref="TowerType"/>,
/// inimigos por <see cref="EnemyKind"/>, construtor de ondas e tabela de dano por nível (<c>dano₁ × 1,12^(L−1)</c>,
/// multiplicação iterativa, sem <c>Math.Pow</c>; SDD-11 RF-11.1/RF-11.6, SDD-27 RF-27.5).
/// </summary>
public sealed class SimContext
{
    public const float Dt = 1f / 30f;

    private readonly TowerParams[] _towers = new TowerParams[5];
    private readonly EnemyDef[] _enemies = new EnemyDef[6];
    private readonly double[][] _damageTable = new double[5][];

    public SimContext(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
        Rules = content.Rules;
        Waves = new IdleWaveBuilder(content);
        foreach (var t in content.Towers)
        {
            if (!SimIds.TryTowerType(t.Id, out var type)) continue;
            _towers[(int)type] = new TowerParams(t.Damage, (float)(t.RangeSlots * content.Rules.SlotRangeDip),
                (float)t.Interval, t.DamageBonusPerBolt ?? 0.0, t.Cost);
            var table = new double[content.Economy.MaxTowerLevel + 1];
            double d = t.Damage;
            for (int l = 1; l < table.Length; l++) { table[l] = d; d *= content.Economy.DamageGrowth; }
            _damageTable[(int)type] = table;
        }
        foreach (var e in content.Enemies)
            if (SimIds.TryEnemyKind(e.Id, out var kind)) _enemies[(int)kind] = e;
    }

    public GameContent Content { get; }
    public RulesDef Rules { get; }
    public IdleWaveBuilder Waves { get; }

    public TowerParams Tower(TowerType type) => _towers[(int)type];
    public EnemyDef Enemy(EnemyKind kind) => _enemies[(int)kind];

    /// <summary>Dano base do nível (1..MaxTowerLevel) lido da tabela; sem arredondamento cumulativo.</summary>
    public double BaseDamage(TowerType type, int level)
    {
        var table = _damageTable[(int)type];
        return table[Math.Clamp(level, 1, table.Length - 1)];
    }
}
