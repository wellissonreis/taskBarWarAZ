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

/// <summary>Torre da partida (<c>Id</c> sequencial próprio, independente do dos inimigos, para os comandos do log serem previsíveis). <c>Invested</c> = compra + melhorias (base da venda, RF-28.4). <c>Branch</c>/<c>Capstone</c>: hooks da TD.3.</summary>
public struct TdTower
{
    public int Id { get; set; }
    public int DefIndex { get; set; }
    public int X { get; set; }
    public int Level { get; set; }
    public int CooldownTicks { get; set; }
    public long Invested { get; set; }
    public byte Branch { get; set; }
}

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
}

/// <summary>Evento observável (não entra no hash). <c>A</c>/<c>B</c>: valores do evento (onda, créditos, id etc.).</summary>
public readonly record struct TdEvent(TdEventType Type, long Tick, long A, long B);

/// <summary>Resultado ao fim da partida (RF-28.10): estrelas 0 (derrota) a 3.</summary>
public readonly record struct TdMatchResult(MatchPhase Phase, int Stars, int IntegrityLost);
