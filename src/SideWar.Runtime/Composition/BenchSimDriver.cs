using SideWar.Core.Bot;
using SideWar.Core.Simulation;
using SideWar.Runtime.Diagnostics;
using SideWar.Runtime.Presence;

namespace SideWar.Runtime.Composition;

/// <summary>
/// Dirige a simulação do <c>--bench</c> (SDD-16 RF-16.2, T4.11), entre ticks, antes do <c>GameHost</c>: o
/// <see cref="SanityBot"/> monta as torres até a população-alvo; uma onda sintética <b>infinita</b> mantém os
/// inimigos no alvo; a base é invulnerável no modo bench. A presença é forçada: em <c>Present</c> uma interação
/// sintética a cada 60 s mantém Presente; em <c>Away</c> não há interação (o jogo começa Ausente, SDD-25).
/// Só na T-Game. Nunca chamado dentro do <c>Step</c>.
/// </summary>
public sealed class BenchSimDriver : ILoopParticipant
{
    public const double KeepPresentSeconds = 60;
    private const float SyntheticHp = 600f;
    private const float SyntheticSpeed = 36f;
    private const int MaxBuildRounds = 64;

    private readonly SimState _state;
    private readonly SimContext _ctx;
    private readonly BenchScenario _scenario;
    private readonly PresenceTracker _presence;
    private readonly Action _interact;
    private readonly SanityBot _bot;
    private long _lastInteraction = long.MinValue;
    private int _spawned;

    public BenchSimDriver(SimState state, SimContext ctx, BenchScenario scenario, PresenceTracker presence, Action interact)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
        _presence = presence ?? throw new ArgumentNullException(nameof(presence));
        _interact = interact ?? throw new ArgumentNullException(nameof(interact));
        _bot = new SanityBot(ctx);
    }

    /// <summary>Inimigos sintéticos criados até agora (diagnóstico e testes).</summary>
    public int Spawned => _spawned;

    /// <summary>Sem prazo próprio: roda a cada wake do laço (30 Hz de simulação).</summary>
    public long? NextDeadline => null;

    public void OnWake(IClock clock)
    {
        KeepPresence(clock);
        _state.BaseHp = _state.MaxBaseHp; // base invulnerável no bench
        BuildTowers();
        TopUpEnemies();
    }

    private void KeepPresence(IClock clock)
    {
        if (_scenario.Presence != BenchPresence.Present) return;
        long now = clock.NowTicks;
        bool stale = _lastInteraction == long.MinValue || now - _lastInteraction >= (long)(KeepPresentSeconds * clock.TicksPerSecond);
        // Antes do primeiro Show a interação é ignorada pela presença: repete a cada wake até virar Presente.
        if (!stale && _presence.State == PresenceState.Presente) return;
        _interact();
        if (_presence.State == PresenceState.Presente) _lastInteraction = now;
    }

    private void BuildTowers()
    {
        for (int round = 0; round < MaxBuildRounds && _state.Towers.Count < _scenario.TargetTowers; round++)
        {
            // Créditos para exatamente uma construção por rodada: o bot para sozinho sem saldo.
            long cost = MaxBuildCost();
            if (_state.Credits < cost) _state.AddCredits(cost - _state.Credits);
            if (_bot.Act(_state) == 0) break; // sem zona livre nem torre a melhorar
        }
    }

    private long MaxBuildCost()
    {
        long max = 1;
        var econ = _ctx.Content.Economy;
        foreach (var type in new[] { TowerType.Bolt, TowerType.Frost, TowerType.Cannon, TowerType.ScaleSet })
            if (TowerCosts.TryBuildCost(_ctx.Tower(type).Cost, _state.Towers.Count, econ.BuildCostFactor, out long c)) max = Math.Max(max, c);
        return max;
    }

    private void TopUpEnemies()
    {
        var layout = _state.Layout;
        while (_state.Enemies.Count < _scenario.TargetEnemies)
        {
            float x = Math.Max(0f, layout.SpawnX - (_spawned % 12) * 18f);
            var enemy = new Enemy
            {
                Id = _state.NextId,
                Kind = EnemyKind.Grunt,
                X = x,
                Speed = SyntheticSpeed,
                SpawnSpeed = SyntheticSpeed,
                Hp = SyntheticHp,
                MaxHp = SyntheticHp,
                Armor = 0,
                Flags = EnemyFlags.None,
                LastHit = TowerType.None,
            };
            if (_state.Enemies.Add(enemy).IsNone) break; // store cheio
            _state.TakeId();
            _spawned++;
        }
    }
}
