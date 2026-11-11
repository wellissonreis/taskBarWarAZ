namespace SideWar.Core.Simulation;

/// <summary>Tipos de evento de simulação (SDD-11 RF-11.11). T3.6 emite <see cref="Hit"/>, <see cref="SuperHit"/> e <see cref="Splash"/>.</summary>
public enum SimEventType : byte
{
    None = 0, EnemySpawned, BossSpawned, Hit, SuperHit, Splash, Kill, Leak, TowerBuilt, TowerUpgraded,
    WaveStarted, WaveCleared, LevelUp, Fall, StageCleared, LayoutChanged,
    // SDD-26 RF-26.15 (T6.6B). A = valor (ganho, índice, correto 0/1), B = auxiliar.
    PeakBannerShown, PeakScaled, PeakEnded, PeakIgnored, BossWaiting, BossQuestionShown, BossAnswered, BossTimeout,
    CrateSpawned, CrateCollected, CrateExpired,
}

/// <summary>
/// Evento só para efeitos visuais (sem efeito na lógica). Hit/SuperHit: <c>X</c> = X do alvo, <c>A</c> = dano aplicado,
/// <c>B</c> = <see cref="TowerType"/> como int. Splash: <c>X</c> = centro (X do alvo principal), <c>A</c> = raio, <c>B</c> = nº de outros atingidos.
/// </summary>
public readonly record struct SimEvent(SimEventType Type, float X, float Y, double A, int B);

/// <summary>
/// Buffer circular de 1024 eventos, sem alocação. O escritor só incrementa <see cref="Written"/>; cada leitor mantém o
/// próprio cursor (um <c>long</c>) e chama <see cref="TryRead"/> entre ticks. Leitor com mais de 1024 eventos de atraso
/// perde os mais antigos (o cursor é avançado ao mais antigo disponível).
/// </summary>
public sealed class SimEventBuffer
{
    public const int Capacity = 1024;
    private readonly SimEvent[] _items = new SimEvent[Capacity];

    /// <summary>Total de eventos já escritos (monotônico); é o cursor de um leitor "em dia".</summary>
    public long Written { get; private set; }

    public void Add(in SimEvent e)
    {
        _items[(int)(Written & (Capacity - 1))] = e;
        Written++;
    }

    /// <summary>Lê o próximo evento do cursor e o avança; false se não há mais. Pula eventos sobrescritos.</summary>
    public bool TryRead(ref long cursor, out SimEvent e)
    {
        long oldest = Written - Capacity;
        if (cursor < oldest) cursor = oldest;
        if (cursor < 0) cursor = 0;
        if (cursor >= Written) { e = default; return false; }
        e = _items[(int)(cursor & (Capacity - 1))];
        cursor++;
        return true;
    }
}
