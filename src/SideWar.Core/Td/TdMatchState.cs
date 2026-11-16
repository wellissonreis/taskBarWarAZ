using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;

namespace SideWar.Core.Td;

/// <summary>
/// Estado determinístico de uma partida TD (SDD-28 RF-28.1 a RF-28.4, RF-28.16). Coexiste com <see cref="SimState"/> (idle),
/// que não é tocado. Máquina: Preparo → Onda → (Preparo | Vitória | Derrota). <see cref="Tick"/> é o relógio do mundo e só
/// avança em Onda; <see cref="Frame"/> conta todas as chamadas de <see cref="Step"/> (referência do log de replay).
/// Sem relógio real, sem <c>Random</c>, sem <c>Math.Pow</c>: o <see cref="Rng"/> é semeado pela seed da partida.
/// Combate: dano direto por alvo (mira First) como mínimo; ganchos para a TD.3 em <c>TdMatchState.Tick.cs</c>.
/// </summary>
public sealed partial class TdMatchState
{
    private readonly TdContent _content;
    private readonly TdStageDef _stage;
    private readonly TdWaveDef[] _waves;
    private readonly TdEnemyStats[] _enemyStats;
    private readonly List<TdTower> _towers = [];
    private readonly List<TdEnemy> _enemies = [];
    private readonly List<TdActiveWave> _active = [];
    private readonly List<TdEvent> _events = [];
    private Rng _rng;
    private int _nextTowerId = 1;
    private int _nextEnemyId = 1;
    private bool _inTick;
    private int _countdownTicks;
    private bool _countdownActive;

    /// <summary>Teto de eventos retidos até <see cref="ClearEvents"/> (excedente é descartado).</summary>
    public const int MaxEvents = 4096;

    private TdMatchState(TdContent content, TdStageDef stage, TdWaveDef[] waves, Rng rng)
    {
        _content = content;
        _stage = stage;
        _waves = waves;
        _rng = rng;
        _enemyStats = TdEnemyStats.Build(content);
        Integrity = content.Stages.Rules.CoreIntegrity;
        IntegrityMax = Integrity;
        Credits = stage.StartingCredits;
        AutoStart = content.Stages.Rules.AutoStart.DefaultMode == "countdown" ? AutoStartMode.Countdown : AutoStartMode.Off;
        AutoStartSeconds = content.Stages.Rules.AutoStart.DefaultCountdownSeconds;
        RestartCountdown();
    }

    /// <summary>Cria a partida da fase <paramref name="stageId"/> (1 a 6) em Preparo, com créditos e integridade iniciais.</summary>
    public static TdMatchState Create(TdContent content, int stageId, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(content);
        var stage = content.Stages.Stages.FirstOrDefault(s => s.Id == stageId)
            ?? throw new ArgumentOutOfRangeException(nameof(stageId), stageId, "Fase inexistente no td-stages.json.");
        var waves = content.Waves.Stages.Single(s => s.Stage == stageId).Waves;
        return new TdMatchState(content, stage, waves, rng);
    }

    public TdContent Content => _content;
    public int StageId => _stage.Id;
    public MatchPhase Phase { get; private set; } = MatchPhase.Preparo;

    /// <summary>Relógio do mundo (ticks de 30 Hz); só avança em Onda.</summary>
    public long Tick { get; private set; }

    /// <summary>Total de chamadas de <see cref="Step"/> (inclusive em Preparo); é a referência do log de replay.</summary>
    public long Frame { get; private set; }

    /// <summary>Número da última onda iniciada (0 antes da primeira); HUD "Onda n/<see cref="TotalWaves"/>".</summary>
    public int WaveNumber { get; private set; }

    public int TotalWaves => _waves.Length;
    public int Integrity { get; private set; }
    public int IntegrityMax { get; }
    public int IntegrityLost => IntegrityMax - Integrity;
    public long Credits { get; private set; }
    public int MaxTowers => _stage.MaxTowers;

    /// <summary>Estrelas ganhas (0 até a Vitória; 0 na Derrota). ★1 concluir, ★2 perder ≤ 6, ★3 perder ≤ 2 (DP-28.8).</summary>
    public int Stars { get; private set; }

    public AutoStartMode AutoStart { get; private set; }
    public double AutoStartSeconds { get; private set; }

    /// <summary>Segundos restantes da contagem de auto-início (0 se inativa).</summary>
    public double CountdownRemainingSeconds => _countdownActive ? _countdownTicks / (double)TdWorld.TicksPerSecond : 0.0;

    /// <summary>Redução acumulada de recarga de habilidades pelas chamadas antecipadas, em ticks (gancho da TD.3: aplicar às recargas).</summary>
    public long CooldownReductionTicks { get; private set; }

    public IReadOnlyList<TdTower> Towers => _towers;
    public IReadOnlyList<TdEnemy> Enemies => _enemies;
    public IReadOnlyList<TdActiveWave> ActiveWaves => _active;
    public IReadOnlyList<TdEvent> Events => _events;
    public TdMatchResult Result => new(Phase, Stars, IntegrityLost);
    public ulong RngState => _rng.State;

    public void ClearEvents() => _events.Clear();

    private void Emit(TdEventType type, long a = 0, long b = 0)
    {
        if (_events.Count < MaxEvents) _events.Add(new TdEvent(type, Tick, a, b));
    }

    /// <summary>Próxima onda a iniciar (<see cref="WaveNumber"/> + 1) ou 0 se todas já foram iniciadas.</summary>
    public int NextWaveNumber => WaveNumber < _waves.Length ? WaveNumber + 1 : 0;

    /// <summary>Valor do bônus de onda (RF-28.4): <c>(base + perWave × w) × bonusFactor</c>, arredondado para cima.</summary>
    public long WaveBonusFor(int wave)
    {
        var r = _content.Stages.Rules;
        return CeilEps((r.WaveBonusBase + r.WaveBonusPerWave * wave) * _waves[wave - 1].BonusFactor);
    }

    /// <summary>ETA estimado (s) para todos os inimigos vivos chegarem ao núcleo: <c>max(X / Speed)</c> (RF-28.3).</summary>
    public double EtaSeconds()
    {
        double eta = 0;
        for (int i = 0; i < _enemies.Count; i++)
        {
            var e = _enemies[i];
            if (e.Speed <= 0) continue;
            double t = e.X / e.Speed;
            if (t > eta) eta = t;
        }
        return eta;
    }

    /// <summary>ceil com desconto relativo de 1e-12 (não sobe inteiro exato por erro de representação; padrão de <c>TowerCosts</c>).</summary>
    internal static long CeilEps(double v) => v <= 0 ? 0 : (long)Math.Ceiling(v * (1.0 - 1e-12));

    private double CallBonusFactor(int wave)
    {
        var c = _content.Stages.Rules.CallNext;
        return c.BonusBase + c.BonusPerWave * wave;
    }

    private void RestartCountdown()
    {
        if (AutoStart == AutoStartMode.Countdown && Phase == MatchPhase.Preparo)
        {
            _countdownActive = true;
            _countdownTicks = (int)Math.Round(AutoStartSeconds * TdWorld.TicksPerSecond, MidpointRounding.ToEven);
        }
        else
        {
            _countdownActive = false;
            _countdownTicks = 0;
        }
    }

    private static CommandResult Reject(CommandReason reason, long missing = 0) => CommandResult.Reject(reason, missing);

    /// <summary>Aplica um comando <b>entre ticks</b>; rejeição não altera o estado (nem o <c>Rng</c>).</summary>
    public CommandResult Apply(IGameCommand? command)
    {
        if (_inTick) throw new InvalidOperationException("Comandos só podem ser aplicados entre ticks.");
        return command switch
        {
            StartWave => DoStartWave(),
            CallNextWave => DoCallNextWave(),
            BuildTower b => DoBuild(b),
            SellTower s => DoSell(s),
            UpgradeTower u => DoUpgrade(u),
            SetAutoStart a => DoSetAutoStart(a),
            _ => Reject(CommandReason.UnknownCommand),
        };
    }

    private bool Running => Phase is MatchPhase.Preparo or MatchPhase.Onda;

    private CommandResult DoSetAutoStart(SetAutoStart cmd)
    {
        var rules = _content.Stages.Rules.AutoStart;
        double s = double.IsNaN(cmd.CountdownSeconds) ? rules.DefaultCountdownSeconds : cmd.CountdownSeconds;
        AutoStartSeconds = Math.Clamp(s, rules.MinCountdownSeconds, rules.MaxCountdownSeconds);
        AutoStart = cmd.Mode == AutoStartMode.Countdown ? AutoStartMode.Countdown : AutoStartMode.Off;
        RestartCountdown();
        return CommandResult.Ok;
    }

    private CommandResult DoStartWave()
    {
        if (Phase != MatchPhase.Preparo) return Reject(CommandReason.InvalidPhase);
        if (NextWaveNumber == 0) return Reject(CommandReason.NotAvailable);
        int w = NextWaveNumber;
        if (_countdownActive && _countdownTicks > 0)
        {
            double remaining = _countdownTicks / (double)TdWorld.TicksPerSecond;
            long bonus = CeilEps(remaining * CallBonusFactor(w));
            if (bonus > 0)
            {
                Credits = Sat.Add(Credits, bonus);
                Emit(TdEventType.CallNextBonus, w, bonus);
            }
        }
        BeginWave(w);
        return CommandResult.Ok;
    }

    private CommandResult DoCallNextWave()
    {
        if (Phase != MatchPhase.Onda) return Reject(CommandReason.InvalidPhase);
        int w = NextWaveNumber;
        if (w == 0) return Reject(CommandReason.NotAvailable);
        foreach (var a in _active)
            if (!a.SpawnsDone) return Reject(CommandReason.NotAvailable);
        var rules = _content.Stages.Rules;
        double eta = EtaSeconds();
        long bonus = CeilEps(eta * CallBonusFactor(w));
        long cap = CeilEps(rules.CallNext.BonusCapFactor * WaveBonusFor(w));
        if (bonus > cap) bonus = cap;
        if (bonus > 0) Credits = Sat.Add(Credits, bonus);
        CooldownReductionTicks += (long)Math.Floor(eta * rules.CallNext.AbilityCooldownReduction * TdWorld.TicksPerSecond);
        Emit(TdEventType.CallNextBonus, w, bonus);
        BeginWave(w);
        return CommandResult.Ok;
    }

    private int TowerDefIndex(string id)
    {
        for (int i = 0; i < _content.Towers.Length; i++)
            if (_content.Towers[i].Id == id) return i;
        return -1;
    }

    private int FindTower(int id)
    {
        for (int i = 0; i < _towers.Count; i++)
            if (_towers[i].Id == id) return i;
        return -1;
    }

    private CommandResult DoBuild(BuildTower cmd)
    {
        if (!Running) return Reject(CommandReason.InvalidPhase);
        int di = cmd.TowerId is null ? -1 : TowerDefIndex(cmd.TowerId);
        if (di < 0) return Reject(CommandReason.TowerNotFound);
        var def = _content.Towers[di];
        if (_stage.Id < def.UnlockStage) return Reject(CommandReason.TowerLocked);
        if (cmd.X < 0 || cmd.X > TdWorld.PathLength) return Reject(CommandReason.OutOfZone);
        if (_towers.Count >= _stage.MaxTowers) return Reject(CommandReason.TowerLimit);
        if (Credits < def.Cost) return Reject(CommandReason.InsufficientCredits, def.Cost - Credits);
        Credits -= def.Cost;
        var t = new TdTower { Id = _nextTowerId++, DefIndex = di, X = cmd.X, Level = 1, Invested = def.Cost };
        _towers.Add(t);
        Emit(TdEventType.TowerBuilt, t.Id, def.Cost);
        return CommandResult.Ok;
    }

    private CommandResult DoSell(SellTower cmd)
    {
        if (!Running) return Reject(CommandReason.InvalidPhase);
        int i = FindTower(cmd.TowerId);
        if (i < 0) return Reject(CommandReason.TowerNotFound);
        long refund = (long)Math.Floor(_towers[i].Invested * _content.Stages.Rules.SellRefund + 1e-9);
        Credits = Sat.Add(Credits, refund);
        Emit(TdEventType.TowerSold, _towers[i].Id, refund);
        _towers.RemoveAt(i);
        return CommandResult.Ok;
    }

    private CommandResult DoUpgrade(UpgradeTower cmd)
    {
        if (!Running) return Reject(CommandReason.InvalidPhase);
        int i = FindTower(cmd.TowerId);
        if (i < 0) return Reject(CommandReason.TowerNotFound);
        var t = _towers[i];
        var def = _content.Towers[t.DefIndex];
        // Níveis lineares 1..N (N = def.Levels.Length = 3); ramo (4) e capstone (5) são da TD.3 (ChooseBranch).
        if (t.Level >= def.Levels.Length) return Reject(CommandReason.MaxLevel);
        long cost = CeilEps(def.Cost * def.Levels[t.Level].CostFactor);
        if (Credits < cost) return Reject(CommandReason.InsufficientCredits, cost - Credits);
        Credits -= cost;
        t.Level++;
        t.Invested += cost;
        _towers[i] = t;
        Emit(TdEventType.TowerUpgraded, t.Id, cost);
        return CommandResult.Ok;
    }

    private void BeginWave(int w)
    {
        var def = _waves[w - 1];
        var spawns = new List<(long Due, int Seq, int Index, bool Boss)>();
        double jitter = _content.Stages.Rules.SpawnJitter;
        int seq = 0;
        foreach (var g in def.Groups)
        {
            int idx = Array.FindIndex(_enemyStats, s => s.Id == g.Enemy);
            for (int i = 0; i < g.Count; i++)
            {
                double r = _rng.NextUInt() / 4294967296.0;
                double sec = g.Offset + i * g.Interval + (2.0 * r - 1.0) * jitter * g.Interval;
                long due = Math.Max(0L, (long)Math.Round(sec * TdWorld.TicksPerSecond, MidpointRounding.ToEven));
                spawns.Add((due, seq++, idx, _enemyStats[idx].IsBoss));
            }
        }
        var ordered = spawns.OrderBy(s => s.Due).ThenBy(s => s.Seq).Select(s => new TdSpawn(s.Due, s.Index, s.Boss)).ToArray();
        _active.Add(new TdActiveWave(w, Tick, ordered, def.BonusFactor));
        WaveNumber = w;
        Phase = MatchPhase.Onda;
        _countdownActive = false;
        _countdownTicks = 0;
        Emit(TdEventType.WaveStarted, w);
    }

    /// <summary>Atualiza a fase após uma onda concluir: Vitória na última, senão volta a Preparo.</summary>
    private void SettlePhase()
    {
        if (_active.Count > 0 || _enemies.Count > 0 || Phase != MatchPhase.Onda) return;
        if (WaveNumber >= _waves.Length)
        {
            Phase = MatchPhase.Vitoria;
            var stars = _content.Stages.Rules.Stars;
            Stars = IntegrityLost <= stars.ThreeStarMaxLoss ? 3 : IntegrityLost <= stars.TwoStarMaxLoss ? 2 : 1;
            Emit(TdEventType.Victory, Stars, IntegrityLost);
        }
        else
        {
            Phase = MatchPhase.Preparo;
            RestartCountdown();
        }
    }
}

/// <summary>Atributos de um inimigo/chefe pré-calculados do conteúdo (índice igual em <c>TdSpawn.EnemyIndex</c>).</summary>
internal readonly record struct TdEnemyStats(
    string Id, bool IsBoss, double Hp, double Speed, double Armor, long Gold, int CoreDamage, bool Flying)
{
    public static TdEnemyStats[] Build(TdContent c)
    {
        var list = new List<TdEnemyStats>();
        foreach (var e in c.Enemies.Enemies)
            list.Add(new(e.Id, false, e.Hp, e.Speed, e.Armor, e.Gold, e.CoreDamage, e.Traits.Contains("flying")));
        foreach (var b in c.Enemies.Bosses)
            list.Add(new(b.Id, true, b.Hp, b.Speed, b.Armor, b.Gold, b.CoreDamage, b.Traits.Contains("flying")));
        return [.. list];
    }
}
