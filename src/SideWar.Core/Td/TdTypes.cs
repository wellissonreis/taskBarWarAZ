namespace SideWar.Core.Td;

/// <summary>Fase da partida (SDD-28 RF-28.1): Preparo (relógio do mundo parado), Onda, Vitória (terminal; não há derrota: a base atacada só tira XP de reputação).</summary>
public enum MatchPhase : byte { Preparo = 0, Onda, Vitoria }

/// <summary>Modos de auto-início (RF-28.3, <c>match.autoStart</c>).</summary>
public enum AutoStartMode : byte { Off = 0, Countdown, Immediate }

/// <summary>Constantes do mundo TD. TD.4 troca o mundo fixo por mundo lógico + pads (hook).</summary>
public static class TdWorld
{
    /// <summary>Frequência da simulação (SDD-10): 30 Hz.</summary>
    public const int TicksPerSecond = 30;

    /// <summary>Comprimento do caminho em unidades lógicas (RF-28.15): portal em <c>X = PathLength</c>, núcleo em <c>X = 0</c>.</summary>
    public const double PathLength = 1200.0;

    /// <summary>
    /// Borda direita do núcleo DESENHADO, em DIP (TD.29a): o sprite tem 42 DIP de altura × proporção 1,5 = 63 DIP de largura, a partir da ponta esquerda.
    /// Com layout, o inimigo vaza (dano + some) ao tocar essa borda, não no X lógico 0 (ponta da tela, onde parecia atravessar a base).
    /// </summary>
    public const double CoreEdgeDip = 63.0;
}

/// <summary>Modos de mira (RF-28.6). Valor numérico entra no hash: não reordenar.</summary>
public enum TdTargeting : byte { First = 0, Last, Strong, Weak, Close }

/// <summary>
/// Torre da partida (<c>Id</c> sequencial próprio, independente do dos inimigos, para os comandos do log serem previsíveis).
/// Sem nível ou ramo. A venda deriva da contagem e é limitada pelo preço efetivamente pago (TD.22).
/// </summary>
public struct TdTower
{
    public int Id { get; set; }
    public int DefIndex { get; set; }
    /// <summary>Preço efetivamente pago, usado como teto antiarbitragem da venda; entra no hash.</summary>
    public long PurchasePrice { get; set; }
    /// <summary>Posição em unidades lógicas (mundo fixo 1.200, RF-28.15). Vale também para torre sem posição (posição gravada).</summary>
    public int X { get; set; }
    /// <summary>false = SEM POSIÇÃO (ADR-034): a zona sumiu após <c>LayoutChanged</c>; não atira, não dá aura.</summary>
    public bool Placed { get; set; }
    internal int[]? RangeSeenIds { get; set; }
    internal int BurstLeft { get; set; }
    internal int BurstGapTicks { get; set; }
    internal int BurstTargetId { get; set; }
    public int CooldownTicks { get; set; }
    public byte Targeting { get; set; }
    /// <summary>Ticks restantes desativada (drenagem do parasita / criptografia do Ransomware, RF-28.7/28.8).</summary>
    public int DisabledTicks { get; set; }
    public int FocusTargetId { get; set; }
    public int FocusHits { get; set; }
    public int ShotCount { get; set; }
    /// <summary>Recarga da paralisia periódica (gelo, ramo A).</summary>
    public int ParalyzeCdTicks { get; set; }
    /// <summary>Recarga do próximo nó do balanceador.</summary>
    public int NodeCdTicks { get; set; }
    /// <summary>Segunda componente (módulo) da fusão (SDD-37): 0 = torre simples; n = índice de definição + 1. Entra no hash só quando &gt; 0.</summary>
    public int ModuleSlot { get; set; }
    /// <summary>Preço efetivamente pago pelo módulo (teto antiarbitragem da venda, por componente).</summary>
    public long ModulePrice { get; set; }
    /// <summary>Recarga da ação periódica do módulo (temporizador separado do da base).</summary>
    public int ModuleCdTicks { get; set; }
    /// <summary>Módulos além do primeiro (fusões de 3 a 6 torres). Imutável: cada fusão troca o array.</summary>
    public TdFusionPart[]? ExtraModules { get; set; }
    public readonly bool HasModule => ModuleSlot > 0;
    /// <summary>Torres que compõem a entidade (base + módulos).</summary>
    public readonly int ComponentTotal => HasModule ? 2 + (ExtraModules?.Length ?? 0) : 1;
    public readonly int ModuleCount => ComponentTotal - 1;
    /// <summary>Índice de definição do módulo <paramref name="k"/> (0 = o primeiro).</summary>
    public readonly int ModuleDef(int k) => k == 0 ? ModuleSlot - 1 : ExtraModules![k - 1].DefIndex;
    public readonly long ModulePriceAt(int k) => k == 0 ? ModulePrice : ExtraModules![k - 1].Price;
    /// <summary>Tipos (índices de definição) da entidade, base primeiro.</summary>
    public readonly int[] Components()
    {
        var list = new int[ComponentTotal];
        list[0] = DefIndex;
        for (int k = 0; k < ModuleCount; k++) list[k + 1] = ModuleDef(k);
        return list;
    }
    /// <summary>Índice de definição do módulo (-1 sem módulo).</summary>
    public readonly int ModuleDefIndex => ModuleSlot - 1;
}

/// <summary>Módulo adicional de uma fusão: tipo e preço efetivamente pago.</summary>
public readonly record struct TdFusionPart(int DefIndex, long Price);

/// <summary>Nó bloqueador do balanceador (RF-28.5): retém até <c>Holds</c> inimigos de solo e causa <c>Dps</c>/s.</summary>
public struct TdNode
{
    public int Id { get; set; }
    public int TowerId { get; set; }
    public double X { get; set; }
    public double Hp { get; set; }
    public int LifeTicks { get; set; }
    public int Holds { get; set; }
    public double Dps { get; set; }
    /// <summary>Bit 1: explode ao fim (Failover); bit 2: renasce ao romper (Blue/green).</summary>
    public byte Flags { get; set; }
}

/// <summary>Zona de queimadura (capstone Saturação do canhão): dano por tick a inimigos de solo no raio.</summary>
public struct TdZone
{
    public double X { get; set; }
    public double Radius { get; set; }
    public int TicksLeft { get; set; }
    public double DamagePerTick { get; set; }
}

/// <summary>Estado de uma habilidade (RF-28.9): recarga e efeito restantes em ticks de mundo; <c>UsesLeft</c> &lt; 0 = ilimitado.</summary>
public readonly record struct TdAbilityStatus(string Id, bool Unlocked, int CooldownTicks, int ActiveTicks, int UsesLeft);

/// <summary>Inimigo (ou chefe) em campo. <c>X</c> em unidades lógicas, marcha de <see cref="TdWorld.PathLength"/> até 0.</summary>
public struct TdEnemy
{
    public int Id { get; set; }
    public int Wave { get; set; }
    public int DefIndex { get; set; }
    public bool IsBoss { get; set; }
    /// <summary>Chefão especial atingiu metade da vida; não reverte com regeneração.</summary>
    public bool Enraged { get; set; }
    public double X { get; set; }
    public double Speed { get; set; }
    public double Hp { get; set; }
    public double MaxHp { get; set; }
    public double ShieldHp { get; set; }
    public bool IsReplica { get; set; }
    public int SentinelMarkTicks { get; set; }
    public double Armor { get; set; }
    public long Gold { get; set; }
    public int CoreDamage { get; set; }
    public bool Flying { get; set; }
    /// <summary>Máscara dos traços de mutação ADICIONADOS (SDD-37 RF-37.7; bit = índice em <c>td-mutations.json</c>). 0 = sem mutação. Réplicas e chefes sempre 0.</summary>
    public ushort Mutations { get; set; }
    public double SlowMult { get; set; }
    public int SlowTicks { get; set; }
    public int ParalyzeTicks { get; set; }
    /// <summary>Imunidade breve a nova paralisia após uma terminar (cap contra "stun lock").</summary>
    public int ParalyzeGraceTicks { get; set; }
    public int PushGraceTicks { get; set; }
    public int InvulnTicks { get; set; }
    public double VulnBonus { get; set; }
    public int VulnTicks { get; set; }
    public double CorrodeAmount { get; set; }
    public int CorrodeTicks { get; set; }
    /// <summary>Ticks desde o último dano (regeneração).</summary>
    public int SinceDamageTicks { get; set; }
    /// <summary>Recarga da habilidade própria (drenagem do parasita, criptografia do Ransomware).</summary>
    public int AbilityCdTicks { get; set; }
    /// <summary>Gatilhos de chefe já disparados (bit0/1 divisões, bit2/3 invulnerabilidades).</summary>
    public byte Triggers { get; set; }
    /// <summary>Id da torre do último dano recebido (0 = zona); só observação, fora do hash.</summary>
    public int LastHitTowerId { get; set; }
    /// <summary>Leitura (fora do hash): efetividade do último acerto (0 neutro, 1 forte, 2 fraco), o dano dele e os ticks restantes do aviso.</summary>
    public byte EffectKind { get; set; }
    public int EffectDamage { get; set; }
    public int EffectTicks { get; set; }
}

/// <summary>Spawn agendado: <c>DueTick</c> relativo ao início da onda (ticks de mundo).</summary>
public readonly record struct TdSpawn(long DueTick, int EnemyIndex, bool IsBoss, ushort Mutations = 0);

/// <summary>Onda em andamento (várias podem se sobrepor, RF-28.3).</summary>
public sealed class TdActiveWave
{
    internal TdActiveWave(int number, long startTick, TdSpawn[] spawns, double bonusFactor)
    {
        Number = number;
        StartTick = startTick;
        Spawns = spawns;
        BonusFactor = bonusFactor;
    }

    public int Number { get; }
    public long StartTick { get; }
    public double BonusFactor { get; }
    internal TdSpawn[] Spawns { get; }
    public int NextSpawn { get; internal set; }
    public int Alive { get; internal set; }
    public bool Leaked { get; internal set; }
    public bool SpawnsDone => NextSpawn >= Spawns.Length;
    /// <summary>Agenda efetiva da onda (inclui os traços de mutação escolhidos por spawn).</summary>
    public IReadOnlyList<TdSpawn> Agenda => Spawns;
    internal TdActiveWave Copy() => new(Number, StartTick, Spawns, BonusFactor) { NextSpawn = NextSpawn, Alive = Alive, Leaked = Leaked };
    public int SpawnCount => Spawns.Length;
}

public enum TdEventType : byte
{
    WaveStarted, WaveCompleted, EnemyKilled, EnemyLeaked, TowerBuilt, TowerSold, TowerUpgraded /* obsoleto (SDD-29): não é mais emitido */, WaveBonus,
    CallNextBonus, Victory, Defeat /* obsoleto (REPUTACAO): não é mais emitido */,
    // TD.3 (anexados ao fim: valores anteriores não mudam).
    TowerBranched /* obsoleto (SDD-29): não é mais emitido */, TowerTargeting, AbilityUsed, TowerDisabled, BossSplit, NodeSpawned, NodeEnded,
    // TD.4 (anexados ao fim). A = id da torre; LayoutApplied: A = largura em DIP, B = torres sem posição.
    TowerPlaced, TowerUnplaced, LayoutApplied,
    // TD.17: pergunta-bônus. A = id da pergunta; B = créditos pagos (0 = errou).
    BonusAnswered,
    // TD.28a: corrida contínua. A = número absoluto da fase concluída; B = pontuação. O resumo completo fica em <c>TdMatchState.StageResults</c>.
    StageCleared,
    // TD.29a: runa despertada. A = índice do nó em <c>RuneWeb</c>; B = fragmentos restantes.
    RuneAwakened,
    VaultIncome,
    // TD.37a: fusão de torres. A = id da torre; B = custo pago pelo módulo.
    TowerFused,
    // REPUTACAO: A = XP de reputação ganho (vitória de onda ou recuperação); B = número da onda (0 se recuperação).
    ReputationGained,
    // REPUTACAO: inimigo chegou à base. A = XP de reputação perdido (já com o escudo); B = dano que ele daria ao núcleo.
    BaseAttacked,
    // A = índice de conteúdo; B = 0 normal, 1 elite, 2 enfurecido.
    EnemyEncountered,
    // Modo sombra ligado (A = 1) ou desligado (A = 0) na fronteira entre ondas.
    ShadowChanged,
    // A = onda para a qual a fase voltou após dano na última onda.
    BossRewind,
    // Chefe da última onda venceu: a partida fica pausada até o jogador retomar. A = onda para a qual voltou.
    BossDefeat,
    // Rebaixamento (Sênior+) destruiu um componente de fusão. A = id da torre; B = índice da definição perdida.
    FusionLost,
}

/// <summary>Evento observável (não entra no hash). <c>A</c>/<c>B</c>: valores do evento (onda, créditos, id etc.).</summary>
public readonly record struct TdEvent(TdEventType Type, long Tick, long A, long B);

/// <summary>Resultado ao fim da partida (RF-28.10, RF-28.21): estrelas 0 (derrota) a 3, coroa, medalhas e pontuação (0 se não venceu).</summary>
public readonly record struct TdMatchResult(MatchPhase Phase, int Stars, int IntegrityLost, bool Crown = false, TdMedals Medals = TdMedals.None, int Score = 0);

/// <summary>Medalhas por feitos (RF-28.21); só valem em Vitória. Valores são bits gravados no perfil: não renumerar.</summary>
[Flags]
public enum TdMedals
{
    None = 0,
    /// <summary>Nenhuma torre vendida.</summary>
    SemVender = 1,
    /// <summary>Nenhuma habilidade usada.</summary>
    SemHabilidades = 2,
    /// <summary>Todas as ondas (exceto a primeira) chamadas antecipadamente.</summary>
    Pressa = 4,
    /// <summary>Desafio da fase concluído (RF-28.10).</summary>
    Desafio = 8,
}

/// <summary>O que o resultado trouxe de novo ao perfil (card de Vitória, RF-28.21): recorde, coroa e medalhas inéditos e selos bônus (coroa/medalhas; os das estrelas ficam fora).</summary>
public readonly record struct TdRecordOutcome(bool NewRecord, bool NewCrown, TdMedals NewMedals, int BonusSeals);

/// <summary>
/// Regras de partida da corrida contínua (TD.28a, SDD-29 §20). <see cref="Default"/> = modo "jogar enquanto trabalha": delay 0 entre ondas e
/// corrida persistente (torres/runas/créditos mantidos de fase em fase, infinito depois da última). <see cref="Classic"/> = comportamento
/// anterior (Preparo manual entre ondas, Vitória terminal ao fim da fase) e é o que <c>TdMatchState.Create</c> sem opções usa.
/// </summary>
/// <param name="ContinuousWaves">A próxima onda começa no mesmo passo em que a anterior termina (Preparo de 0 s). O Preparo inicial (antes da onda 1 da corrida) continua existindo.</param>
/// <param name="PersistentRun">Vencer a fase segue para a próxima sem recriar o estado; depois da última, ciclos infinitos mais difíceis. Só a derrota encerra.</param>
/// <param name="InitialAwakened">Teia de runas permanente do perfil: bitset dos nós já despertados (slot = anel*16+setor), carregado em toda partida/corrida nova (modo não clássico).</param>
/// <param name="InitialFragments">Fragmentos permanentes do perfil, carregados na partida nova.</param>
/// <param name="StageClearHeal">Obsoleto (REPUTACAO): a vida do núcleo não existe mais; ignorado.</param>
public enum TdDifficulty : byte { Casual, Hard, Extreme }

public sealed record TdMatchOptions(bool ContinuousWaves = true, bool PersistentRun = true, int StageClearHeal = 5, int? HighestCompletedStage = null, UInt128 InitialAwakened = default, long InitialFragments = 0, int? InitialReputation = null, bool InitialShadow = false, TdDifficulty Difficulty = TdDifficulty.Casual)
{
    /// <summary>Regras de campanha (escala de HP, chefe final, meia barra de XP): valem fora do modo clássico.</summary>
    public bool UsesCampaignRules => !IsClassic;
    public static TdMatchOptions Default { get; } = new();
    public static TdMatchOptions Classic { get; } = new(false, false, 0);
    public bool IsClassic => !ContinuousWaves && !PersistentRun;
}

/// <summary>Resumo de uma fase concluída na corrida contínua (evento <see cref="TdEventType.StageCleared"/>). <c>StageNumber</c> é absoluto (1, 2, … 6, 7, …); <c>StageId</c> é o conteúdo (1 a 6) e <c>Cycle</c> a volta (0 = primeira).</summary>
public readonly record struct TdStageSummary(int StageNumber, int StageId, int Cycle, int Stars, int IntegrityLost, bool Crown, TdMedals Medals, int Score, double CombatSeconds);
