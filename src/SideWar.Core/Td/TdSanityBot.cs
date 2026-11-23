using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;

namespace SideWar.Core.Td;

/// <summary>Perfis do jogador simulado (SDD-28 RF-28.12, DP-28.10).</summary>
public enum TdBotProfile : byte
{
    /// <summary>Só o básico (Raio e Canhão), sem habilidades.</summary>
    Novato = 0,
    /// <summary>Usa o contra óbvio da fase, habilidades; não chama ondas antes.</summary>
    Medio,
    /// <summary>Contra do conceito, mira, habilidades e chamada antecipada.</summary>
    Otimizado,
    /// <summary>Como o médio, mas SEM o contra do conceito da fase (deve perder).</summary>
    SemContra,
}

/// <summary>Resultado de uma partida do SanityBot (uma fase, um perfil, uma seed).</summary>
public sealed record TdBotResult(
    int Stage, TdBotProfile Profile, ulong Seed, MatchPhase Phase, int Stars, int IntegrityLost, long XpLost, long CreditsLeft,
    int WaveReached, double CombatSeconds, double TotalSeconds, int TowersBuilt, int CommandsRejected,
    IReadOnlyDictionary<string, int> KillsByTowerType, ulong StateHash, IReadOnlyList<int> LostByWave, TdMatchResult Match = default)
{
    /// <summary>Venceu e perdeu menos XP que o limite de colapso (<see cref="TdSanityBot.CollapseXp"/>).</summary>
    public bool Won => Phase == MatchPhase.Vitoria && XpLost < TdSanityBot.CollapseXp;
    /// <summary>Colapso: perdeu XP demais (o antigo "game over"); o bot para ali. Medida de balanceamento, não um fim de jogo.</summary>
    public bool Collapsed => XpLost >= TdSanityBot.CollapseXp;
}

/// <summary>Resultado de uma corrida contínua do SanityBot (TD.28a): fases concluídas em sequência com as mesmas torres/runas/créditos, até derrota ou limite.</summary>
public sealed record TdRunBotResult(
    TdBotProfile Profile, ulong Seed, int StartStage, int StagesCleared, int StageNumber, int WaveInStage, MatchPhase Phase, long XpLost,
    long CreditsLeft, int TowersBuilt, int TowersAtEnd, long Frames, double CombatSeconds, int CommandsRejected, IReadOnlyList<TdStageSummary> Stages, ulong StateHash)
{
    /// <summary>Colapso: perdeu XP demais (o antigo "game over"); o bot para ali.</summary>
    public bool Defeated => XpLost >= TdSanityBot.CollapseXp;
}

/// <summary>
/// SanityBot TD (TD.10): jogador headless e determinístico que usa só a API pública de <see cref="TdMatchState"/> (Apply/Step) com os
/// comandos do jogo. Cada perfil segue um roteiro por fase (DSL abaixo) num layout típico de barra de 1920 DIP. Serve de teste de
/// regressão do balanceamento (curva de dificuldade, RF-28.12). Sem relógio real nem <c>Random</c>.
/// </summary>
/// <remarks>
/// DSL do roteiro (passos separados por espaço, executados em ordem; o passo atual espera os créditos):
/// <c>+tipo@X</c> constrói (X lógico preferido; busca o mais próximo livre; preço crescente por tipo, SDD-29 §5.1),
/// <c>tN=modo</c> mira da torre N (N = ordem de construção; first/last/strong/weak/close). rune id compra o caminho, runeSector setor n investe em n nós, sell tipo vende a primeira torre do tipo.
/// </remarks>
public static class TdSanityBot
{
    /// <summary>XP perdido a partir do qual o bot considera a partida colapsada (equivale aos 20 pontos de vida que o núcleo tinha). O jogo real não tem game over.</summary>
    public const int CollapseXp = 20 * TdReputation.XpPerCoreDamage;

    /// <summary>Segundos humanos estimados de Preparo por onda (olhar, construir, apertar iniciar) para a duração total (DP-28.10).</summary>
    public const double PrepSecondsPerWave = 22.0;

    private const int DecisionEveryFrames = 15;       // 0,5 s

    /// <summary>Layout típico: barra de 1920 DIP com 3 zonas livres (entre ícones/área de notificação), como o BuildZoneMap entrega.</summary>
    public static StripLayout TypicalLayout() =>
        StripLayout.Create(1920, 1920, 0f, 0f, 1920, TowerHost.Taskbar, [new FreeZone(100, 520), new FreeZone(640, 1180), new FreeZone(1300, 1800)]);

    public static TdBotResult Run(TdContent content, int stage, TdBotProfile profile, ulong seed, string? script = null, bool awaken = true)
    {
        ArgumentNullException.ThrowIfNull(content);
        var state = TdMatchState.Create(content, stage, Rng.Seed(seed, 54));
        var bot = new Runner(state, script is null ? TdBotPlans.For(stage, profile) : TdBotPlans.Parse(script), profile) { Awaken = awaken };
        return bot.Play(seed);
    }

    /// <summary>
    /// Corrida contínua (TD.28a, SDD-29 §20): uma única partida com <see cref="TdMatchOptions.Default"/> (delay 0 entre ondas, torres/runas/créditos
    /// mantidos), jogada até derrota ou <paramref name="maxStages"/> fases concluídas (6 = a campanha; mais = ciclo infinito). A cada fase o bot
    /// carrega o roteiro da fase de conteúdo (só construir/runas: mira e venda do roteiro de fase única dependem de ids que a corrida já mudou).
    /// </summary>
    public static TdRunBotResult RunContinuous(TdContent content, int startStage, TdBotProfile profile, ulong seed, int maxStages = 6, bool awaken = true)
    {
        ArgumentNullException.ThrowIfNull(content);
        var state = TdMatchState.Create(content, startStage, Rng.Seed(seed, 54), null, TdMatchOptions.Default);
        var bot = new Runner(state, RunPlan(startStage, profile), profile) { RunMaxStages = maxStages, Awaken = awaken };
        return bot.PlayRun(seed, startStage);
    }

    private static Step[] RunPlan(int stageId, TdBotProfile profile) => [.. TdBotPlans.For(stageId, profile).Where(s => s.Kind is 'b' or 'r' or 's')];

    private sealed class Runner
    {
        private readonly TdMatchState _s;
        private Step[] _plan;
        public int RunMaxStages { get; init; }
        public bool Awaken { get; init; } = true;
        private readonly TdBotProfile _profile;
        private int _next;
        private int _rejected;
        private int _builtCount;
        private int _wavesStarted;

        public Runner(TdMatchState s, Step[] plan, TdBotProfile profile)
        {
            _s = s; _plan = plan; _profile = profile;
        }

        private bool Smart => _profile != TdBotProfile.Novato; // o novato não usa habilidades
        private bool Expert => _profile == TdBotProfile.Otimizado;

        public TdRunBotResult PlayRun(ulong seed, int startStage)
        {
            Send(new LayoutChanged(TypicalLayout()));
            Send(new SetPresence(true));
            long maxFrames = 30L * 3600 * 2 * Math.Max(1, RunMaxStages);
            int cleared = 0;
            while (_s.Frame < maxFrames && _s.Phase is MatchPhase.Preparo or MatchPhase.Onda && _s.StagesCleared < RunMaxStages && _s.ReputationLostTotal < CollapseXp)
            {
                if (_s.StagesCleared != cleared)
                {
                    cleared = _s.StagesCleared;
                    _plan = RunPlan(_s.StageId, _profile); // fase nova: roteiro da fase de conteúdo; torres/runas já possuídas ficam
                    _next = 0;
                    _fallbackBuilt = 0;
                }
                if (_s.Phase == MatchPhase.Preparo)
                {
                    Spend();
                    Send(new StartWave());
                }
                else if (_s.Frame % DecisionEveryFrames == 0)
                {
                    Spend();
                    Abilities();
                    if (Expert) TryCall();
                }
                _s.Step();
            }
            return new TdRunBotResult(_profile, seed, startStage, _s.StagesCleared, _s.StageNumber, _s.WaveNumber, _s.Phase, _s.ReputationLostTotal, _s.Credits,
                _builtCount, _s.Towers.Count, _s.Frame, _s.Tick / (double)TdWorld.TicksPerSecond, _rejected, [.. _s.StageResults], _s.ComputeStateHash());
        }

        public TdBotResult Play(ulong seed)
        {
            Send(new LayoutChanged(TypicalLayout()));
            Send(new SetPresence(true));
            long maxFrames = 30L * 3600 * 2;
            while (_s.Frame < maxFrames && _s.Phase is MatchPhase.Preparo or MatchPhase.Onda && _s.ReputationLostTotal < CollapseXp)
            {
                if (_s.Phase == MatchPhase.Preparo)
                {
                    Spend();
                    if (Send(new StartWave())) _wavesStarted++;
                }
                else if (_s.Frame % DecisionEveryFrames == 0)
                {
                    Spend();
                    Abilities();
                    if (Expert) TryCall();
                }
                while (_lostByWave.Count < _s.WaveNumber) _lostByWave.Add(0);
                if (_s.WaveNumber > 0) _lostByWave[_s.WaveNumber - 1] = _s.IntegrityLost;
                _s.Step();
            }
            if (_s.WaveNumber > 0) _lostByWave[_s.WaveNumber - 1] = _s.IntegrityLost;
            var kills = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var kv in _s.KillsByTower)
            {
                string type = kv.Key == 0 ? "zona" : TypeOfTower(kv.Key);
                kills[type] = kills.GetValueOrDefault(type) + kv.Value;
            }
            double combat = _s.Tick / (double)TdWorld.TicksPerSecond;
            return new TdBotResult(_s.StageId, _profile, seed, _s.Phase, _s.Stars, _s.IntegrityLost, _s.ReputationLostTotal, _s.Credits, _s.WaveNumber,
                combat, combat + PrepSecondsPerWave * _s.WaveNumber, _builtCount, _rejected, kills, _s.ComputeStateHash(), _lostByWave, _s.Result);
        }

        private readonly Dictionary<int, string> _types = [];
        private readonly List<int> _lostByWave = [];

        private string TypeOfTower(int id) => _types.TryGetValue(id, out var t) ? t : "vendida";

        private bool Send(IGameCommand c)
        {
            var r = _s.Apply(c);
            if (!r.IsOk) _rejected++;
            return r.IsOk;
        }

        // ---- roteiro ----------------------------------------------------------------------------------------------

        private void Spend()
        {
            while (Awaken && _s.Fragments > 0 && _s.Frontier.Count > 0 && _s.Apply(new AwakenRune(_s.Frontier[0])).IsOk) { } // teia: despertar sempre que houver fragmento
            // Mesmo o perfil sem contra precisa de uma defesa para iniciar a partida.
            if (_profile == TdBotProfile.SemContra && !_s.HasPlacedTower && _s.Phase == MatchPhase.Preparo)
            {
                foreach (int x in Candidates(900))
                    if (_s.Apply(new BuildTower("bolt", x)).IsOk)
                    {
                        _types[_s.Towers[^1].Id] = "bolt";
                        _builtCount++;
                        break;
                    }
            }
            while (_next < _plan.Length)
            {
                var st = _plan[_next];
                var r = Execute(st);
                if (r == StepResult.Wait) return;
                _next++;
            }
            if (!(_profile == TdBotProfile.SemContra && _s.StageId == 1)) // SemContra na fase 1 ignora a construção (conceito da fase)
                while (Fallback()) { }
        }

        private static readonly int[] FallbackX = [1000, 780, 520, 350, 900, 640, 250, 1100];
        private int _fallbackBuilt;
        private int FallbackLimit => _profile is TdBotProfile.Medio or TdBotProfile.Otimizado ? 8 : 4; // sem runas de crédito, os perfis avançados gastam o excedente em mais torres (TD.29a)

        /// <summary>Depois do roteiro: tenta mais quatro construções a preço crescente.</summary>
        private bool Fallback()
        {
            int towers = _s.Towers.Count;
            string kind = _plan.FirstOrDefault(p => p.Kind == 'b').Tower ?? "bolt";
            int di = Array.FindIndex(_s.Content.Towers, t => t.Id == kind);
            if (towers < TdMatchState.TechnicalTowerLimit && _fallbackBuilt < FallbackLimit && _s.Credits >= _s.NextTowerCost(di))
                foreach (int x in Candidates(FallbackX[_fallbackBuilt % FallbackX.Length]))
                    if (_s.Apply(new BuildTower(kind, x)).IsOk)
                    {
                        _types[_s.Towers[^1].Id] = kind;
                        _builtCount++;
                        _fallbackBuilt++;
                        return true;
                    }
            return false;
        }

        private enum StepResult { Done, Wait }

        private StepResult Execute(Step st)
        {
            switch (st.Kind)
            {
                case 'b': // build
                {
                    int def = Array.FindIndex(_s.Content.Towers, t => t.Id == st.Tower);
                    if (_s.Credits < _s.NextTowerCost(def)) return StepResult.Wait;
                    if (_s.Towers.Count >= TdMatchState.TechnicalTowerLimit) return StepResult.Done; // teto técnico: pula
                    foreach (int x in Candidates(st.X))
                    {
                        var r = _s.Apply(new BuildTower(st.Tower, x));
                        if (r.IsOk)
                        {
                            int id = _s.Towers[^1].Id;
                            _types[id] = st.Tower;
                            _builtCount++;
                            return StepResult.Done;
                        }
                        if (r.Reason is CommandReason.InsufficientCredits) return StepResult.Wait;
                        if (r.Reason is CommandReason.TowerLocked or CommandReason.TowerLimit or CommandReason.InvalidPhase) return StepResult.Done;
                    }
                    _rejected++;
                    return StepResult.Done;
                }
                case 'v':
                {
                    var tower = _s.Towers.FirstOrDefault(t => _s.Content.Towers[t.DefIndex].Id == st.Tower);
                    return tower.Id == 0 ? StepResult.Done : Settle(_s.Apply(new SellTower(tower.Id)));
                }
                case 't':
                {
                    var r = _s.Apply(new SetTargeting(st.N, Enum.Parse<TdTargeting>(st.Arg, true)));
                    if (!r.IsOk) _rejected++;
                    return StepResult.Done; // mira nunca bloqueia o roteiro
                }
            }
            return StepResult.Done;
        }

        private StepResult Settle(CommandResult r)
        {
            if (r.IsOk) return StepResult.Done;
            if (r.Reason == CommandReason.InsufficientCredits) return StepResult.Wait;
            _rejected++;
            return StepResult.Done;
        }

        /// <summary>X lógicos candidatos: o preferido e depois alternando para os lados em passos de 11 un. (~1 célula de 44 DIP).</summary>
        private static IEnumerable<int> Candidates(int pref)
        {
            yield return pref;
            for (int d = 11; d <= 1200; d += 11)
            {
                if (pref + d <= 1200) yield return pref + d;
                if (pref - d >= 0) yield return pref - d;
            }
        }

        // ---- habilidades, chamada, caixa ----------------------------------------------------------------------------

        private void Abilities()
        {
            if (!Smart) return;
            int near = 0;
            bool boss = false;
            foreach (var e in _s.Enemies)
            {
                if (e.X < 700) near++;
                if (e.IsBoss) boss = true;
            }
            foreach (var a in _s.AbilityStatuses)
            {
                if (!a.Unlocked || a.CooldownTicks > 0 || a.ActiveTicks > 0 || a.UsesLeft == 0) continue;
                bool use = a.Id switch
                {
                    "scale" => near >= (Expert ? 4 : 6) || boss,
                    "firewall" => near >= (Expert ? 5 : 8) || boss,
                    "snapshot" => _s.ReputationLostTotal >= 4 * TdReputation.XpPerCoreDamage,
                    _ => false,
                };
                if (use) Send(new UseAbility(a.Id));
            }
        }

        private void TryCall()
        {
            if (_s.NextWaveNumber == 0 || _s.Enemies.Count > 3) return;
            if (_s.IntegrityLost > 1) return;
            _s.Apply(new CallNextWave());
        }
    }

    internal readonly record struct Step(char Kind, string Tower, int X, int N, string Arg);
}
