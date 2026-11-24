using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;

namespace SideWar.Core.Td;

/// <summary>
/// Estado determinístico de uma partida TD (SDD-28 RF-28.1 a RF-28.4, RF-28.16). Coexiste com <see cref="SimState"/> (idle),
/// que não é tocado. Máquina: Preparo → Onda → (Preparo | Vitória). <see cref="Tick"/> é o relógio do mundo e só
/// avança em Onda; <see cref="Frame"/> conta todas as chamadas de <see cref="Step"/> (referência do log de replay).
/// Sem relógio real, sem <c>Random</c>, sem <c>Math.Pow</c>: o <see cref="Rng"/> é semeado pela seed da partida.
/// Combate: dano direto por alvo (mira First) como mínimo; ganchos para a TD.3 em <c>TdMatchState.Tick.cs</c>.
/// </summary>
public sealed partial class TdMatchState
{
    private readonly TdContent _content;
    private TdStageDef _stage;
    private TdWaveDef[] _waves;
    private readonly TdMatchOptions _options;
    private int _stageNumber;
    private int _stageClears;
    private long _stageStartTick;
    private int _stageIntegrityLoss;
    private int _backupHealsThisStage;
    private int _repLostStage;
    private int _metaShield;
    private List<TdStageSummary> _stageResults = [];
    private readonly TdEnemyStats[] _enemyStats;
    private readonly HashSet<string> _metaNodes;
    private int[] _abilityCd;
    private int[] _abilityActive;
    private int[] _abilityUses;
    private List<TdTower> _towers = [];
    private List<TdEnemy> _enemies = [];
    private List<TdActiveWave> _active = [];
    private List<TdNode> _nodes = [];
    private List<TdZone> _zones = [];
    private HashSet<int> _held = [];
    private List<TdEvent> _events = [];
    private Rng _rng;
    // Mutações (SDD-37 §4): semente derivada (não consome o RNG principal), ondas iniciadas na corrida e estreia (onda absoluta) por traço.
    private readonly ulong _mutSeed;
    private int _runWaves;
    private int[] _mutDebut;
    private SideWar.Core.Simulation.StripLayout? _layout;
    private int _nextTowerId = 1;
    private int _nextEnemyId = 1;
    private int _nextNodeId = 1;
    private bool _inTick;
    private int _countdownTicks;
    private bool _countdownActive;
    private bool _bonusAnswered;

    /// <summary>Teto de eventos retidos até <see cref="ClearEvents"/> (excedente é descartado).</summary>
    public const int MaxEvents = 4096;

    private TdMatchState(TdContent content, TdStageDef stage, int stageNumber, TdWaveDef[] waves, Rng rng, IEnumerable<string>? metaNodes, TdMatchOptions options)
    {
        _content = content;
        _options = options;
        GateReputation = Math.Clamp(options.InitialReputation ?? 0, 0, 100_000_000);
        _shadow = options.InitialShadow;
        _stageNumber = stageNumber;
        _stage = stage;
        _waves = waves;
        _rng = rng;
        _mutSeed = MutationSeed(rng.State, rng.Inc);
        _mutDebut = new int[content.Mutations.Traits.Length];
        RunId = $"{rng.State:x}-{rng.Inc:x}";
        _enemyStats = TdEnemyStats.Build(content);
        _metaNodes = new HashSet<string>(metaNodes ?? [], StringComparer.Ordinal);
        _abilityCd = new int[content.Abilities.Length];
        _abilityActive = new int[content.Abilities.Length];
        _abilityUses = new int[content.Abilities.Length];
        Credits = stage.StartingCredits + content.Runes.CreditSources.StartingCreditsBonus + CycleCatchUpCredits(content, stageNumber);
        // Cada nó comprado conta uma vez; créditos de meta são aplicados somente na criação da partida.
        foreach (var node in content.Meta.Branches.SelectMany(b => b.Nodes))
            if (_metaNodes.Contains(node.Id))
                foreach (var effect in node.Effects)
                    {
                        if (effect.Kind == "startingCredits") Credits += (long)effect.Value;
                        else if (effect.Kind == "xpShield") _metaShield += (int)effect.Value;
                    }
        AutoStart = content.Stages.Rules.AutoStart.DefaultMode switch { "countdown" => AutoStartMode.Countdown, "immediate" => AutoStartMode.Immediate, _ => AutoStartMode.Off };
        AutoStartSeconds = content.Stages.Rules.AutoStart.DefaultCountdownSeconds;
        RestartCountdown();
    }

    /// <summary>
    /// Cria a partida da fase <paramref name="stageId"/> (número absoluto: 1 a 6 = primeira volta, 7+ = ciclos do infinito) em Preparo, com créditos e integridade iniciais.
    /// <paramref name="unlockedMetaNodes"/>: ids de nós da árvore já comprados (inclui créditos iniciais e desbloqueio de habilidades, ex.: <c>operation.snapshot</c>).
    /// </summary>
    public static TdMatchState Create(TdContent content, int stageId, Rng rng, IEnumerable<string>? unlockedMetaNodes = null) =>
        Create(content, stageId, rng, unlockedMetaNodes, TdMatchOptions.Classic);

    /// <summary>Cria a partida com as regras de <paramref name="options"/> (TD.28a): <see cref="TdMatchOptions.Default"/> é a corrida contínua.</summary>
    public static TdMatchState Create(TdContent content, int stageId, Rng rng, IEnumerable<string>? unlockedMetaNodes, TdMatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(content);
        // stageId é o número ABSOLUTO da fase (1 a 6 = primeira volta; 7+ = ciclos do infinito, que repetem o conteúdo das 6 regiões).
        if (stageId < 1) throw new ArgumentOutOfRangeException(nameof(stageId), stageId, "Fase inexistente no td-stages.json.");
        var defs = content.Stages.Stages;
        var stage = defs[(stageId - 1) % defs.Length];
        var waves = content.Waves.Stages.Single(s => s.Stage == stage.Id).Waves;
        var match = new TdMatchState(content, stage, stageId, waves, rng, unlockedMetaNodes, options);
        match.ApplyPersistedRunes();
        return match;
    }

    public TdContent Content => _content;
    public string RunId { get; }
    public int WavesReached
    {
        get
        {
            long total = 0;
            int count = _content.Stages.Stages.Length;
            int start = _stageNumber - _stageClears;
            for (int i = 0; i < _stageClears; i++)
                total += _content.Stages.Stages[(start - 1 + i) % count].Waves;
            total += Phase is MatchPhase.Preparo or MatchPhase.Vitoria ? WaveNumber : Math.Max(0, WaveNumber - 1);
            return (int)Math.Min(int.MaxValue, total);
        }
    }
    /// <summary>Id do conteúdo da fase atual (1 a 6; no ciclo infinito volta a 1).</summary>
    public int StageId => _stage.Id;
    /// <summary>Número absoluto da fase na corrida (1, 2, … 6, 7, …); sobe a cada <see cref="TdEventType.StageCleared"/>. Escala HP dos inimigos.</summary>
    public int StageNumber => _stageNumber;
    /// <summary>Patente atual da reputação, inclusive após rebaixamento.</summary>
    public int CurrentRankId => (int)TdRank.For(GateReputation);
    /// <summary>Volta completa pelas fases do conteúdo (0 até concluir a última).</summary>
    public int Cycle => (_stageNumber - 1) / _content.Stages.Stages.Length;
    public TdMatchOptions Options => _options;
    /// <summary>Fases concluídas na corrida (sempre 0 no modo clássico).</summary>
    public int StagesCleared => _stageClears;
    /// <summary>Resumos das fases concluídas, em ordem (evento <see cref="TdEventType.StageCleared"/> = último item).</summary>
    public IReadOnlyList<TdStageSummary> StageResults => _stageResults;
    public MatchPhase Phase { get; private set; } = MatchPhase.Preparo;

    /// <summary>Relógio do mundo (ticks de 30 Hz); só avança em Onda.</summary>
    public long Tick { get; private set; }

    /// <summary>Total de chamadas de <see cref="Step"/> (inclusive em Preparo); é a referência do log de replay.</summary>
    public long Frame { get; private set; }

    /// <summary>Número da última onda iniciada (0 antes da primeira); HUD "Onda n/<see cref="TotalWaves"/>".</summary>
    public int WaveNumber { get; private set; }

    public int TotalWaves => _waves.Length;
    public int IntegrityLost => _stageIntegrityLoss;
    public long Credits { get; private set; }
    /// <summary>Teto técnico de segurança (SDD-29 §5.3): 48 torres no total, contando as SEM POSIÇÃO. Não é teto de design: o custo crescente limita antes.</summary>
    public const int TechnicalTowerLimit = 48;

    /// <summary>Quantas torres do tipo <paramref name="defIndex"/> existem agora (inclui as SEM POSIÇÃO, que ainda ocupam a quota).</summary>
    public int TowerCount(int defIndex)
    {
        int n = 0;
        for (int i = 0; i < _towers.Count; i++)
        {
            if (_towers[i].DefIndex == defIndex) n++;
            if (_towers[i].ModuleSlot == defIndex + 1) n++; // componentes de híbridos contam por tipo (SDD-37 RF-37.3)
        }
        return n;
    }

    /// <summary>
    /// Preço da próxima torre do tipo (SDD-29 §5.1): <c>round(base × F[n] / 100)</c> com <c>n</c> = <see cref="TowerCount"/>.
    /// Arredondamento: <b>metade para o par</b> (half-to-even), em inteiros, o mesmo da tabela do SDD (90×1,25 = 112,5 → 112; 90×1,95 = 175,5 → 176).
    /// </summary>
    public long NextTowerCost(int defIndex) => TowerCostAt(defIndex, TowerCount(defIndex));

    /// <summary>Preço da torre do tipo quando já existem <paramref name="n"/> do mesmo tipo (n &gt;= 0; acima da tabela usa a última entrada).</summary>
    public long TowerCostAt(int defIndex, int n)
    {
        var table = _content.Stages.Rules.TowerCostTable;
        return DivRoundHalfEven(_content.Towers[defIndex].Cost * (long)table[Math.Clamp(n, 0, table.Length - 1)], 100);
    }

    /// <summary>Custo base sem modificadores de runas, para consumidores sem partida.</summary>
    internal static long TowerCost(TdContent content, int defIndex, int n)
    {
        var table = content.Stages.Rules.TowerCostTable;
        long f = table[Math.Clamp(n, 0, table.Length - 1)];
        return DivRoundHalfEven(content.Towers[defIndex].Cost * f, 100);
    }

    /// <summary>Divisão inteira com arredondamento metade-para-o-par (valores não negativos).</summary>
    internal static long DivRoundHalfEven(long num, long den)
    {
        long q = num / den, r = num % den, twice = r * 2;
        if (twice > den || (twice == den && (q & 1) == 1)) q++;
        return q;
    }

    /// <summary>
    /// Valor teórico de venda, antes do teto individual de PurchasePrice, para um tipo com <paramref name="countBefore"/> torres do tipo (SDD-29 §5.2):
    /// <c>floor(custo(tipo, n−1) × taxa)</c>, com a taxa em pontos-base limitada por <c>sellRefundCapBp</c> (85%). Piso inteiro; a venda efetiva aplica também o teto pelo preço pago (TD.22).
    /// </summary>
    public long SellValueAt(int defIndex, int countBefore)
    {
        var rules = _content.Stages.Rules;
        long bp = (long)Math.Round(rules.SellRefund * 10000.0, MidpointRounding.ToEven);
        return TowerCostAt(defIndex, Math.Max(0, countBefore - 1)) * bp / 10000;
    }

    /// <summary>Quanto a torre <paramref name="towerId"/> devolveria se vendida agora (0 se não existir).</summary>
    public long SellValue(int towerId)
    {
        int i = FindTower(towerId);
        return i < 0 ? 0 : HybridSellValue(_towers[i]);
    }

    /// <summary>Estrelas ganhas (0 até a Vitória; 0 na Derrota). ★1 concluir, ★2 perder ≤ 6, ★3 perder ≤ 2 (DP-28.8).</summary>
    public int Stars { get; private set; }

    public AutoStartMode AutoStart { get; private set; }
    public double AutoStartSeconds { get; private set; }

    /// <summary>Segundos restantes da contagem de auto-início (0 se inativa).</summary>
    public double CountdownRemainingSeconds => _countdownActive ? _countdownTicks / (double)TdWorld.TicksPerSecond : 0.0;

    /// <summary>Redução acumulada de recarga de habilidades pelas chamadas antecipadas, em ticks (gancho da TD.3: aplicar às recargas).</summary>
    public long CooldownReductionTicks { get; private set; }

    public IReadOnlyList<TdTower> Towers => _towers;

    /// <summary>Existe ao menos uma torre posicionada na faixa.</summary>
    public bool HasPlacedTower => _towers.Any(t => t.Placed);
    public IReadOnlyList<TdEnemy> Enemies => _enemies;
    public IReadOnlyList<TdActiveWave> ActiveWaves => _active;
    public IReadOnlyList<TdNode> Nodes => _nodes;
    public IReadOnlyList<TdZone> Zones => _zones;
    public IReadOnlyList<TdEvent> Events => _events;
    public TdMatchResult Result => ComputeResult();
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
        return CeilEps((_content.Runes.CreditSources.WaveBonusBase + _content.Runes.CreditSources.WaveBonusPerWave * wave) * _waves[wave - 1].BonusFactor);
    }

    /// <summary>ETA estimado (s) para todos os inimigos vivos chegarem ao núcleo: <c>max(X / Speed)</c> (RF-28.3).</summary>
    public double EtaSeconds()
    {
        double eta = 0;
        for (int i = 0; i < _enemies.Count; i++)
        {
            var e = _enemies[i];
            if (e.Speed <= 0) continue;
            double t = Math.Max(0.0, e.X - LeakX) / e.Speed;
            if (t > eta) eta = t;
        }
        return eta;
    }

    /// <summary>ceil com desconto relativo de 1e-12 (não sobe inteiro exato por erro de representação; padrão de <c>TowerCosts</c>).</summary>
    internal static long CeilEps(double v) => v <= 0 ? 0 : (long)Math.Ceiling(v * (1.0 - 1e-12));

    private double CallBonusFactor(int wave)
    {
        var c = _content.Stages.Rules.CallNext;
        return (c.BonusBase + c.BonusPerWave * wave);
    }

    private void RestartCountdown()
    {
        ApplyShadowRequest(); // nova fronteira entre ondas
        if ((AutoStart == AutoStartMode.Countdown || (_shadow && AutoStart != AutoStartMode.Immediate)) && Phase == MatchPhase.Preparo)
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
        if (Paused && command is StartWave or CallNextWave or UseAbility)
            return Reject(CommandReason.PausedCommand); // "nada de comando de tempo enquanto pausado" (SDD-29 §11)
        return command switch
        {
            AwakenRune a => DoAwakenRune(a.NodeId),
            Pause => DoPause(),
            Resume => DoResume(),
            StartWave => DoStartWave(),
            CallNextWave => DoCallNextWave(),
            BuildTower b => DoBuild(b),
            LayoutChanged l => DoLayoutChanged(l),
            PlaceTower p => DoPlace(p),
            MoveTower m => DoMove(m),
            RelocateTower rl => DoRelocate(rl),
            SellTower s => DoSell(s),
            FuseTower f => DoFuse(f),
            FuseTowers ft => DoFuseTowers(ft),
            SetAutoStart a => DoSetAutoStart(a),
            SetTargeting g => DoSetTargeting(g),
            UseAbility u => DoUseAbility(u),
            SetPresence sp => DoSetPresence(sp),
            SetShadow sh => DoSetShadow(sh),
            AnswerBonus ab => DoAnswerBonus(ab),
            _ => Reject(CommandReason.UnknownCommand),
        };
    }

    /// <summary>Simulação pausada (SDD-29 §11): entra no <c>StateHash</c> só quando verdadeira, então partidas sem pausa mantêm o hash.</summary>
    public bool Paused { get; private set; }

    private CommandResult DoPause()
    {
        if (!Running) return Reject(CommandReason.InvalidPhase);
        if (Paused) return Reject(CommandReason.AlreadyPaused);
        Paused = true;
        return CommandResult.Ok;
    }

    private CommandResult DoResume()
    {
        if (!Paused) return Reject(CommandReason.NotPaused);
        Paused = false;
        return CommandResult.Ok;
    }

    private bool Running => Phase is MatchPhase.Preparo or MatchPhase.Onda;

    private CommandResult DoSetAutoStart(SetAutoStart cmd)
    {
        var rules = _content.Stages.Rules.AutoStart;
        double s = double.IsNaN(cmd.CountdownSeconds) ? rules.DefaultCountdownSeconds : cmd.CountdownSeconds;
        AutoStartSeconds = Math.Clamp(s, rules.MinCountdownSeconds, rules.MaxCountdownSeconds);
        AutoStart = Enum.IsDefined(cmd.Mode) ? cmd.Mode : AutoStartMode.Off;
        RestartCountdown();
        return CommandResult.Ok;
    }

    private CommandResult DoStartWave()
    {
        if (Phase != MatchPhase.Preparo) return Reject(CommandReason.InvalidPhase);
        if (NextWaveNumber == 0) return Reject(CommandReason.NotAvailable);
        if (!HasPlacedTower) return Reject(CommandReason.NotAvailable);
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
        if (!HasPlacedTower) return Reject(CommandReason.NotAvailable);
        foreach (var a in _active)
            if (!a.SpawnsDone) return Reject(CommandReason.NotAvailable);
        _earlyCalls++;
        var rules = _content.Stages.Rules;
        double eta = EtaSeconds();
        long bonus = CeilEps(eta * CallBonusFactor(w));
        long cap = CeilEps(rules.CallNext.BonusCapFactor * WaveBonusFor(w));
        if (bonus > cap) bonus = cap;
        if (bonus > 0) Credits = Sat.Add(Credits, bonus);
        long reduction = (long)Math.Floor(eta * rules.CallNext.AbilityCooldownReduction * TdWorld.TicksPerSecond);
        CooldownReductionTicks += reduction;
        for (int i = 0; i < _abilityCd.Length; i++)
            _abilityCd[i] = (int)Math.Max(0L, _abilityCd[i] - reduction); // RF-28.3: chamar cedo encurta as recargas
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
        if (CurrentRankId < def.MinRank) return Reject(CommandReason.TowerLocked);
        if (def.Stats.TryGetValue("maxPerPlayer", out var limit) && TowerCount(di) >= limit) return Reject(CommandReason.TowerTypeLimit);
        var reason = ResolvePlacement(cmd.X, 0, out int px);
        if (reason != CommandReason.None) return Reject(reason);
        if (_towers.Count >= TechnicalTowerLimit) return Reject(CommandReason.TowerLimit);
        long cost = NextTowerCost(di);
        if (Credits < cost) return Reject(CommandReason.InsufficientCredits, cost - Credits);
        Credits -= cost;
        var t = new TdTower { Id = _nextTowerId++, DefIndex = di, PurchasePrice = cost, X = px, Placed = true, Targeting = (byte)ParseTargeting(def.DefaultTargeting) };
        _towers.Add(t);
        if (_towers.Count > _peakTowers) _peakTowers = _towers.Count;
        Emit(TdEventType.TowerBuilt, t.Id, cost);
        return CommandResult.Ok;
    }

    private CommandResult DoSell(SellTower cmd)
    {
        if (!Running) return Reject(CommandReason.InvalidPhase);
        int i = FindTower(cmd.TowerId);
        if (i < 0) return Reject(CommandReason.TowerNotFound);
        long refund = SellValue(cmd.TowerId);
        Credits = Sat.Add(Credits, refund);
        _sold = true;
        Emit(TdEventType.TowerSold, _towers[i].Id, refund);
        _nodes.RemoveAll(n => n.TowerId == _towers[i].Id);
        _towers.RemoveAt(i);
        return CommandResult.Ok;
    }

    private CommandResult DoSetTargeting(SetTargeting cmd)
    {
        if (!Running) return Reject(CommandReason.InvalidPhase);
        int i = FindTower(cmd.TowerId);
        if (i < 0) return Reject(CommandReason.TowerNotFound);
        var t = _towers[i];
        var def = _content.Towers[t.DefIndex];
        if ((def.Kind != "attack" && !(def.Kind == "control" && def.Targets != "none")) || !Enum.IsDefined(cmd.Mode)) return Reject(CommandReason.NotAvailable);
        t.Targeting = (byte)cmd.Mode;
        _towers[i] = t;
        Emit(TdEventType.TowerTargeting, t.Id, (long)cmd.Mode);
        return CommandResult.Ok;
    }

    private static TdTargeting ParseTargeting(string? id) => id switch
    {
        "last" => TdTargeting.Last,
        "strong" => TdTargeting.Strong,
        "weak" => TdTargeting.Weak,
        "close" => TdTargeting.Close,
        _ => TdTargeting.First,
    };

    private void BeginWave(int w)
    {
        ApplyShadowRequest(); // fronteira entre ondas
        var def = EffectiveWave(w);
        var spawns = new List<(long Due, int Seq, int Index, bool Boss, ushort Mut)>();
        double jitter = _content.Stages.Rules.SpawnJitter;
        int seq = 0;
        _runWaves++;
        var plan = PlanMutations(_runWaves, _stageNumber, def, _mutDebut);
        foreach (var g in def.Groups)
        {
            int idx = Array.FindIndex(_enemyStats, s => s.Id == g.Enemy);
            for (int i = 0; i < g.Count; i++)
            {
                double r = _rng.NextUInt() / 4294967296.0;
                double sec = g.Offset + i * g.Interval + (2.0 * r - 1.0) * jitter * g.Interval;
                long due = Math.Max(0L, (long)Math.Round(sec * TdWorld.TicksPerSecond, MidpointRounding.ToEven));
                spawns.Add((due, seq, idx, _enemyStats[idx].IsBoss, plan[seq])); seq++;
            }
        }
        var ordered = spawns.OrderBy(s => s.Due).ThenBy(s => s.Seq).Select(s => new TdSpawn(s.Due, s.Index, s.Boss, s.Mut)).ToArray();
        _active.Add(new TdActiveWave(w, Tick, ordered, def.BonusFactor));
        WaveNumber = w;
        Phase = MatchPhase.Onda;
        _bonusAnswered = false;
        _countdownActive = false;
        _countdownTicks = 0;
        Emit(TdEventType.WaveStarted, w);
    }

    /// <summary>
    /// Atualiza a fase após uma onda concluir. Clássico: Vitória na última, senão Preparo. Contínuo (TD.28a): a próxima onda começa no mesmo
    /// passo (Preparo de 0 s) e, ao fim da fase, a corrida persistente segue para a próxima sem recriar o estado.
    /// </summary>
    private void SettlePhase()
    {
        if (_active.Count > 0 || _enemies.Count > 0 || Phase != MatchPhase.Onda) return;
        if (WaveNumber >= _waves.Length)
        {
            if (_options.PersistentRun)
            {
                ClearStageAndAdvance();
                return;
            }
            Phase = MatchPhase.Vitoria;
            var stars = _content.Stages.Rules.Stars;
            Stars = IntegrityLost <= stars.ThreeStarMaxLoss ? 3 : IntegrityLost <= stars.TwoStarMaxLoss ? 2 : 1;
            Emit(TdEventType.Victory, Stars, IntegrityLost);
        }
        else if (_options.ContinuousWaves)
        {
            BeginWave(NextWaveNumber);
        }
        else
        {
            Phase = MatchPhase.Preparo;
            _bonusAnswered = false;
            RestartCountdown();
            StartImmediateIfReady();
        }
    }

    /// <summary>
    /// Fim de fase na corrida persistente: registra o resumo (estrelas, coroa, medalhas, pontuação da fase), recupera integridade,
    /// avança para a próxima fase (depois da última, volta ao conteúdo da fase 1 com HP escalado por volta) MANTENDO torres, runas,
    /// créditos, habilidades e relógio, e inicia a onda 1 (contínuo) ou volta ao Preparo.
    /// </summary>
    private void ClearStageAndAdvance()
    {
        int lost = _stageIntegrityLoss;
        var stars = _content.Stages.Rules.Stars;
        int starCount = lost <= stars.ThreeStarMaxLoss ? 3 : lost <= stars.TwoStarMaxLoss ? 2 : 1;
        var summary = BuildSummary(starCount, lost);
        Stars = starCount;
        _stageResults.Add(summary);
        _stageClears++;
        Emit(TdEventType.StageCleared, summary.StageNumber, summary.Score);

        GainReputation(TdReputation.StageClearXp(summary.StageNumber, starCount, lost == 0), 0);
        _repLostStage = 0;
        _stageIntegrityLoss = 0;
        _backupHealsThisStage = 0;
        _stageNumber = _options.InitialReputation is null ? _stageNumber + 1 : TdStageGate.Next(_content.Stages.Stages, _stageNumber, TdRank.For(GateReputation));
        var defs = _content.Stages.Stages;
        _stage = defs[(_stageNumber - 1) % defs.Length];
        _waves = _content.Waves.Stages.Single(s => s.Stage == _stage.Id).Waves;
        WaveNumber = 0;
        _stageStartTick = Tick;
        _earlyCalls = 0;
        _sold = false;
        _peakTowers = _towers.Count;
        _hasLeaked = false; // runas "sem vazar" valem por fase na corrida contínua
        Array.Clear(_abilityUses); // "usos por fase" recomeçam; recargas continuam
        _bonusAnswered = false;
        if (_options.ContinuousWaves) BeginWave(1);
        else
        {
            Phase = MatchPhase.Preparo;
            RestartCountdown();
            StartImmediateIfReady();
        }
    }

    private void StartImmediateIfReady()
    {
        if (AutoStart == AutoStartMode.Immediate && Phase == MatchPhase.Preparo && NextWaveNumber != 0 && HasPlacedTower)
            BeginWave(NextWaveNumber);
    }
}

/// <summary>Atributos de um inimigo/chefe pré-calculados do conteúdo (índice igual em <c>TdSpawn.EnemyIndex</c>), com as mecânicas lidas dos <c>params</c>.</summary>
internal sealed class TdEnemyStats
{
    public required string Id { get; init; }
    public bool IsBoss { get; init; }
    public double Hp { get; init; }
    public double Speed { get; init; }
    public double Armor { get; init; }
    public long Gold { get; init; }
    public int CoreDamage { get; init; }
    public bool Flying { get; init; }
    public bool Armored { get; init; }
    public bool Replicator { get; init; }
    public double CopyHpFraction { get; init; }
    public double ShieldHp { get; init; }
    public double RevealRange { get; init; }
    /// <summary>Lentidão máxima permitida (0 = imune; 1 = sem teto). Piso de velocidade = 1 − SlowCap.</summary>
    public double SlowCap { get; init; } = 1.0;
    public bool ParalysisImmune { get; init; }
    public double RegenPct { get; init; }
    public int RegenDelayTicks { get; init; }
    /// <summary>Drenagem/criptografia: alcance, duração da desativação, recarga entre usos e recarga inicial.</summary>
    public double ActRange { get; init; }
    public int ActDisableTicks { get; init; }
    public int ActCdTicks { get; init; }
    public int ActInitialCdTicks { get; init; }
    public double BlockedReduction { get; init; }
    public double Split1 { get; init; }
    public double Split2 { get; init; }
    public int SplitCount { get; init; }
    /// <summary>Traços nativos do conteúdo (compatibilidade com mutações).</summary>
    public string[] NativeTraits { get; init; } = [];
    public int InvulnTicks { get; init; }
    public double Inv1 { get; init; }
    public double Inv2 { get; init; }

    private static double P(Dictionary<string, double> d, string k, double f = 0) => d.TryGetValue(k, out var v) ? v : f;
    private static int T(double s) => (int)Math.Round(s * TdWorld.TicksPerSecond, MidpointRounding.ToEven);

    public static TdEnemyStats[] Build(TdContent c)
    {
        var list = new List<TdEnemyStats>();
        foreach (var e in c.Enemies.Enemies)
        {
            var p = e.Params;
            bool drain = p.ContainsKey("drainRangeUnits");
            list.Add(new()
            {
                Id = e.Id, IsBoss = false, Hp = e.Hp, Speed = e.Speed, Armor = e.Armor, Gold = e.Gold, CoreDamage = e.CoreDamage,
                Flying = e.Traits.Contains("flying"), Armored = e.Traits.Contains("armored"), NativeTraits = e.Traits,
                Replicator = e.Traits.Contains("replicator"), CopyHpFraction = P(p, "copyHpFraction"),
                ShieldHp = P(p, "shieldHp"), RevealRange = P(p, "revealRange"),
                RegenPct = P(p, "regenPctPerSecond"), RegenDelayTicks = T(P(p, "regenDelaySeconds")),
                ActRange = P(p, "drainRangeUnits"), ActDisableTicks = drain ? T(P(p, "disableSeconds")) : 0,
                ActCdTicks = drain ? T(P(p, "disableSeconds")) : 0,
            });
        }
        foreach (var b in c.Enemies.Bosses)
        {
            var p = b.Params;
            bool enc = p.ContainsKey("encryptIntervalSeconds");
            list.Add(new()
            {
                Id = b.Id, IsBoss = true, Hp = b.Hp, Speed = b.Speed, Armor = b.Armor, Gold = b.Gold, CoreDamage = b.CoreDamage,
                Flying = b.Traits.Contains("flying"), Armored = b.Traits.Contains("armored"), NativeTraits = b.Traits,
                SlowCap = b.SlowCap, ParalysisImmune = b.ParalysisImmune,
                RegenPct = P(p, "regenPctPerSecond"), RegenDelayTicks = T(P(p, "regenDelaySeconds")),
                ActRange = P(p, "encryptRangeUnits"), ActDisableTicks = enc ? T(P(p, "encryptSeconds")) : 0,
                ActCdTicks = enc ? T(P(p, "encryptIntervalSeconds")) : 0, ActInitialCdTicks = enc ? T(P(p, "encryptIntervalSeconds")) : 0,
                BlockedReduction = P(p, "blockedTowerDamageReduction"),
                Split1 = P(p, "splitAtHpFraction1"), Split2 = P(p, "splitAtHpFraction2"), SplitCount = (int)P(p, "splitCount"),
                InvulnTicks = T(P(p, "invulnerableSeconds")), Inv1 = P(p, "invulnerableAtPathFraction1"), Inv2 = P(p, "invulnerableAtPathFraction2"),
            });
        }
        return [.. list];
    }
}
