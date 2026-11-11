using SideWar.Core.Bot;
using SideWar.Core.Content;
using SideWar.Core.Simulation;

namespace SideWar.Benchmarks;

/// <summary>
/// Estado realista da simulação 1D para os benchmarks (SDD-16 P-13, SDD-17 RF-17.5; ADR-027: sem projéteis).
/// O <see cref="SanityBot"/> (T3.10) monta e melhora as torres até o nível alvo; uma onda sintética "infinita"
/// (como em SDD-16 RF-16.2) completa a população-alvo de inimigos antes de cada tick. A base é invulnerável.
/// Só a API pública do Core é usada.
/// </summary>
public sealed class SimScenario
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private readonly SimContext _ctx = new(Content);
    private readonly int _targetEnemies;
    private int _fillCounter;

    public SimState State { get; }
    public int Towers { get; }
    public long TopUps { get; private set; }

    public SimScenario(int towers, int enemies, int towerLevel, int warmupTicks = 600)
    {
        Towers = towers;
        _targetEnemies = enemies;
        var slots = new float[towers];
        for (int i = 0; i < towers; i++) slots[i] = 662f + 48f * i; // até 662 + 48·11 = 1190 < portal 1220
        var layout = StripLayout.Create(1240f, StripLayout.DefaultLeakX, 10f, 1220f, slots);
        State = SimState.CreateInitial(Content, layout, Rng.Seed(1, 1));

        var bot = new SanityBot(_ctx);
        for (int guard = 0; guard < 10_000 && !TowersReady(towers, towerLevel); guard++)
        {
            State.Credits = long.MaxValue / 4; // montagem de estado: créditos ilimitados só no setup
            bot.Act(State);
        }
        if (!TowersReady(towers, towerLevel))
            throw new InvalidOperationException($"setup: {towers} torres LV{towerLevel} não alcançadas");

        for (int i = 0; i < warmupTicks; i++) Tick();
    }

    private bool TowersReady(int towers, int level)
    {
        int n = 0;
        for (int i = 0; i < State.Towers.Capacity; i++)
        {
            if (!State.Towers.IsAlive(i)) continue;
            if (State.Towers.At(i).Level < level) return false;
            n++;
        }
        return n == towers;
    }

    public int EnemyCount => State.Enemies.Count;

    /// <summary>Completa a população-alvo (onda infinita sintética) e mantém a base invulnerável.</summary>
    public void TopUp()
    {
        var s = State;
        s.BaseHp = s.MaxBaseHp;
        while (s.Enemies.Count < _targetEnemies)
        {
            // espalha os novos inimigos ao longo do caminho (determinístico, razão áurea)
            float u = (_fillCounter++ * 0.6180339887f) % 1f;
            float x = s.Layout.LeakX + 40f + u * (s.Layout.SpawnX - s.Layout.LeakX - 40f);
            var e = new Enemy
            {
                Id = s.TakeId(), Kind = EnemyKind.Grunt, X = x, Speed = 20f, SpawnSpeed = 20f,
                Hp = 1e15, MaxHp = 1e15, Armor = 0, LastHit = TowerType.None,
            };
            if (s.Enemies.Add(e).IsNone) break;
            TopUps++;
        }
    }

    /// <summary>Um tick completo do benchmark: complementa a onda e executa <see cref="SimState.Step"/>.</summary>
    public void Tick()
    {
        TopUp();
        State.Step(_ctx);
    }

    /// <summary>Só o <see cref="SimState.Step"/> (usado pelo cronômetro do p99; o TopUp fica fora da medição).</summary>
    public void Step() => State.Step(_ctx);

    public static SimScenario WaveHeavy() => new(towers: 12, enemies: 44, towerLevel: 5);
    public static SimScenario WaveMedium() => new(towers: 6, enemies: 20, towerLevel: 5);
}
