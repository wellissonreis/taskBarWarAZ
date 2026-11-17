namespace SideWar.Core.Td;

/// <summary>Fase da partida (SDD-28 RF-28.1): Preparo (relógio do mundo parado), Onda, Vitória e Derrota (terminais).</summary>
public enum MatchPhase : byte { Preparo = 0, Onda, Vitoria, Derrota }

/// <summary>Auto-início (RF-28.3, <c>match.autoStart</c>): desligado por padrão.</summary>
public enum AutoStartMode : byte { Off = 0, Countdown }

/// <summary>Constantes do mundo TD. TD.4 troca o mundo fixo por mundo lógico + pads (hook).</summary>
public static class TdWorld
{
    /// <summary>Frequência da simulação (SDD-10): 30 Hz.</summary>
    public const int TicksPerSecond = 30;

    /// <summary>Comprimento do caminho em unidades lógicas (RF-28.15): portal em <c>X = PathLength</c>, núcleo em <c>X = 0</c>.</summary>
    public const double PathLength = 1200.0;
}

/// <summary>Modos de mira (RF-28.6). Valor numérico entra no hash: não reordenar.</summary>
public enum TdTargeting : byte { First = 0, Last, Strong, Weak, Close }

/// <summary>
/// Torre da partida (<c>Id</c> sequencial próprio, independente do dos inimigos, para os comandos do log serem previsíveis).
/// <c>Invested</c> = compra + melhorias (base da venda, RF-28.4). <c>Branch</c>: 0 = sem ramo, 1 = A, 2 = B (nível 4+); nível 5 = capstone.
/// </summary>
public struct TdTower
{
    public int Id { get; set; }
    public int DefIndex { get; set; }
    /// <summary>Posição em unidades lógicas (mundo fixo 1.200, RF-28.15). Vale também para torre sem posição (posição gravada).</summary>
    public int X { get; set; }
    /// <summary>false = SEM POSIÇÃO (ADR-034): a zona sumiu após <c>LayoutChanged</c>; não atira, não dá aura, não conta no bônus de maxTowers.</summary>
    public bool Placed { get; set; }
    public int Level { get; set; }
    public int CooldownTicks { get; set; }
    public long Invested { get; set; }
    public byte Branch { get; set; }
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
}

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
    public double X { get; set; }
    public double Speed { get; set; }
    public double Hp { get; set; }
    public double MaxHp { get; set; }
    public double Armor { get; set; }
    public long Gold { get; set; }
    public int CoreDamage { get; set; }
    public bool Flying { get; set; }
    public double SlowMult { get; set; }
    public int SlowTicks { get; set; }
    public int ParalyzeTicks { get; set; }
    /// <summary>Imunidade breve a nova paralisia após uma terminar (cap contra "stun lock").</summary>
    public int ParalyzeGraceTicks { get; set; }
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
}

/// <summary>Spawn agendado: <c>DueTick</c> relativo ao início da onda (ticks de mundo).</summary>
public readonly record struct TdSpawn(long DueTick, int EnemyIndex, bool IsBoss);

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
    public bool SpawnsDone => NextSpawn >= Spawns.Length;
    public int SpawnCount => Spawns.Length;
}

public enum TdEventType : byte
{
    WaveStarted, WaveCompleted, EnemyKilled, EnemyLeaked, TowerBuilt, TowerSold, TowerUpgraded, WaveBonus,
    CallNextBonus, Victory, Defeat,
    // TD.3 (anexados ao fim: valores anteriores não mudam).
    TowerBranched, TowerTargeting, AbilityUsed, TowerDisabled, BossSplit, NodeSpawned, NodeEnded,
    // TD.4 (anexados ao fim). A = id da torre; LayoutApplied: A = largura em DIP, B = torres sem posição.
    TowerPlaced, TowerUnplaced, LayoutApplied,
    // TD.3b (anexados ao fim). Caixa-bomba: A = id da caixa; CrateSpawned B = X lógico; CrateCollected B = créditos; CrateExploded B = id da torre destruída (0 = nenhuma).
    CrateSpawned, CrateCollected, CrateExploded, CrateVanished,
    // TD.17: pergunta-bônus. A = id da pergunta; B = créditos pagos (0 = errou).
    BonusAnswered,
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
