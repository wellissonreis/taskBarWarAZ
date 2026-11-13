using SideWar.Core.Content;
using SideWar.Core.Simulation;

namespace SideWar.Core.Missions;

public enum MissionStatus : byte { InProgress = 0, Completed, Collected }

/// <summary>Missão do dia: id do <c>missions.json</c>, progresso (sempre ≤ meta) e estado.</summary>
public sealed record MissionEntry(string Id, long Progress, MissionStatus Status);

/// <summary>Estado persistível (SDD-14): data local <c>yyyy-MM-dd</c>, as missões do dia e os segundos presentes do dia.</summary>
public sealed record MissionServiceState(string Date, IReadOnlyList<MissionEntry> Missions, double PresentSeconds);

/// <summary>
/// Missões diárias (SDD-27 RF-27.11): 4 por dia sorteadas de um pool por PCG32 com seed derivada de
/// <c>(seedPersistida, aaaaMMdd)</c> (não consome o <c>Rng</c> principal), progresso por <see cref="SimEvent"/>, estados
/// <c>Em andamento → Concluída → Coletada</c>, recompensa só ao coletar e coleta automática na virada do dia (DP-27.13).
/// A recompensa em renda é relativa (D-18): <c>minutos × 60 × IncomePerSecond</c>, sem multiplicador de presença (RF-27.13).
/// Sem relógio: quem chama passa a data local.
/// </summary>
public sealed class MissionService
{
    private readonly GameContent _content;
    private readonly ulong _seed;
    private readonly List<MissionEntry> _items = [];
    private double _presentSeconds;

    public MissionService(GameContent content, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        _seed = seed;
    }

    /// <summary>Data local do dia vigente (<c>yyyy-MM-dd</c>); vazia antes do primeiro <see cref="Sync"/>.</summary>
    public string Date { get; private set; } = "";

    public IReadOnlyList<MissionEntry> Missions => _items;

    /// <summary>Segundos em Presente acumulados no dia (não zera ao ficar Ausente).</summary>
    public double PresentSeconds => _presentSeconds;

    /// <summary>Badge do nav: concluídas e não coletadas.</summary>
    public int Badge
    {
        get
        {
            int n = 0;
            foreach (var m in _items) if (m.Status == MissionStatus.Completed) n++;
            return n;
        }
    }

    /// <summary>
    /// Sorteio puro das missões de um dia: <paramref name="count"/> ids distintos do pool, em ordem de sorteio
    /// (Fisher–Yates parcial com PCG32, seed derivada de <paramref name="seed"/> e <paramref name="date"/>).
    /// </summary>
    public static string[] Draw(MissionsDef defs, ulong seed, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(defs);
        int pool = defs.Missions.Length;
        int count = Math.Min(defs.DailyCount, pool);
        var idx = new int[pool];
        for (int i = 0; i < pool; i++) idx[i] = i;
        var rng = Rng.Seed(Mix(seed, (ulong)(date.Year * 10000 + date.Month * 100 + date.Day)), 54);
        var result = new string[count];
        for (int i = 0; i < count; i++)
        {
            int span = pool - i;
            int j = i + (int)(((ulong)rng.NextUInt() * (ulong)span) >> 32);   // multiplicação 32x32 sem viés de módulo relevante
            (idx[i], idx[j]) = (idx[j], idx[i]);
            result[i] = defs.Missions[idx[i]].Id;
        }
        return result;
    }

    // splitmix64 sobre (seed, data): determinístico e independente da ordem dos eventos.
    private static ulong Mix(ulong seed, ulong dateKey)
    {
        ulong z = unchecked(seed + 0x9E3779B97F4A7C15UL * (dateKey + 1));
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }

    /// <summary>
    /// Garante as missões do dia. Na virada (data diferente), coleta automaticamente as concluídas e não coletadas
    /// (DP-27.13), zera o progresso e sorteia as novas. Não entrega nada: as coletas da virada vão para
    /// <paramref name="grants"/> (em ordem) e o host aplica cada <see cref="GrantMissionReward"/> entre ticks. Devolve quantas foram coletadas.
    /// </summary>
    public int Sync(DateOnly today, List<GrantMissionReward> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);
        string key = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (key == Date) return 0;
        int collected = 0;
        for (int i = 0; i < _items.Count; i++)
            if (Collect(i) is { } g) { grants.Add(g); collected++; }
        _items.Clear();
        foreach (var id in Draw(_content.Missions, _seed, today)) _items.Add(new MissionEntry(id, 0, MissionStatus.InProgress));
        _presentSeconds = 0;
        Date = key;
        return collected;
    }

    private MissionDef Def(string id)
    {
        foreach (var d in _content.Missions.Missions) if (d.Id == id) return d;
        throw new InvalidOperationException($"Missão desconhecida: {id}");
    }

    private void Add(string id, long amount)
    {
        if (amount <= 0) return;
        for (int i = 0; i < _items.Count; i++)
        {
            var m = _items[i];
            if (m.Id != id || m.Status != MissionStatus.InProgress) continue;
            long target = Def(id).Target;
            long p = Math.Min(target, Sat.Add(m.Progress, amount));
            _items[i] = m with { Progress = p, Status = p >= target ? MissionStatus.Completed : MissionStatus.InProgress };
            return;
        }
    }

    /// <summary>
    /// Conta um evento da simulação: <c>Kill</c> (mortes por torres, inclusive farm), <c>BossAnswered</c> (acerto ou erro;
    /// <c>BossTimeout</c> não conta), <c>PeakScaled</c> (clique), <c>CrateCollected</c>, <c>TowerUpgraded</c> (só melhoria manual:
    /// a auto-escala não passa por <c>Apply</c> e não emite, DP-27.7).
    /// </summary>
    public void Observe(in SimEvent e)
    {
        switch (e.Type)
        {
            case SimEventType.Kill: Add("defeat-enemies", 1); break;
            case SimEventType.BossAnswered: Add("answer-boss", 1); break;
            case SimEventType.PeakScaled: Add("scale-spikes", 1); break;
            case SimEventType.CrateCollected: Add("collect-crates", 1); break;
            case SimEventType.TowerUpgraded: Add("upgrade-towers", 1); break;
        }
    }

    /// <summary>Lê os eventos novos do buffer da simulação a partir do cursor do chamador e os conta.</summary>
    public void Consume(SimState state, ref long cursor)
    {
        ArgumentNullException.ThrowIfNull(state);
        while (state.Events.TryRead(ref cursor, out var e)) Observe(e);
    }

    /// <summary>Soma tempo em Presente ("Fique presente por 60 min": minutos inteiros acumulados no dia, contínuos ou não).</summary>
    public void AddPresentSeconds(double seconds)
    {
        if (!(seconds > 0)) return;
        _presentSeconds += seconds;
        long minutes = (long)Math.Floor(_presentSeconds / 60.0);
        for (int i = 0; i < _items.Count; i++)
        {
            var m = _items[i];
            if (m.Id != "stay-present" || m.Status != MissionStatus.InProgress) continue;
            Add("stay-present", minutes - m.Progress);
            break;
        }
    }

    /// <summary>
    /// COLETAR: só missão Concluída. Valida localmente, marca Coletada e DEVOLVE o <see cref="GrantMissionReward"/> (id + tipo + valor
    /// explícitos); não entrega nada. O host o aplica entre ticks via <c>SimState.Apply</c>/<c>CommandLog</c> (ADR-026).
    /// Devolve null se o índice é inválido ou a missão não está Concluída.
    /// </summary>
    public GrantMissionReward? Collect(int index)
    {
        if ((uint)index >= (uint)_items.Count || _items[index].Status != MissionStatus.Completed) return null;
        var id = _items[index].Id;
        var reward = Def(id).Reward;
        _items[index] = _items[index] with { Status = MissionStatus.Collected };
        return new GrantMissionReward(id, reward.Kind, reward.Amount);
    }

    public MissionServiceState Snapshot() => new(Date, _items.ToArray(), _presentSeconds);

    /// <summary>Restaura o estado persistido (ids desconhecidos são descartados; progresso é limitado à meta).</summary>
    public void Restore(MissionServiceState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        _items.Clear();
        foreach (var m in s.Missions)
        {
            MissionDef? def = null;
            foreach (var d in _content.Missions.Missions) if (d.Id == m.Id) def = d;
            if (def is null) continue;
            long p = Math.Clamp(m.Progress, 0, def.Target);
            var st = m.Status == MissionStatus.Collected ? MissionStatus.Collected
                : p >= def.Target ? MissionStatus.Completed : MissionStatus.InProgress;
            _items.Add(new MissionEntry(m.Id, p, st));
        }
        Date = s.Date;
        _presentSeconds = Math.Max(0, s.PresentSeconds);
    }
}
