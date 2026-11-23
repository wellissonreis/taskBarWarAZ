using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;

namespace SideWar.Core.Td;

/// <summary>Grupo da prévia de mutações: <c>Count</c> spawns do inimigo <c>EnemyIndex</c> com o mesmo conjunto de traços adicionados.</summary>
public readonly record struct TdMutationPreview(int EnemyIndex, ushort Traits, int Count);

/// <summary>
/// Mutações combináveis de inimigos (SDD-37 §4, TD.37c). A decisão é tomada na montagem da onda por uma função PURA de
/// (semente derivada da partida, id absoluto da onda, fase absoluta, definição da onda, estreias por traço): usa um RNG próprio
/// (não consome o RNG principal, não depende de jitter, relógio ou UI) e por isso a prévia é idêntica à agenda efetiva.
/// Só fora do modo clássico. G = ondas já iniciadas na corrida antes da atual (contador equivalente; não depende de quando as ondas terminam).
/// </summary>
public sealed partial class TdMatchState
{
    private static ulong MutationSeed(ulong state, ulong inc)
    {
        ulong z = unchecked(state ^ (inc * 0x9E3779B97F4A7C15UL) ^ 0x4D55544154494F4EUL); // splitmix64 (constantes fixas)
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }

    /// <summary>Ondas já iniciadas nesta corrida (contador G da progressão de mutações).</summary>
    public int RunWavesStarted => _runWaves;

    /// <summary>Onda absoluta em que cada traço estreou (0 = ainda não apareceu), na ordem do catálogo.</summary>
    public IReadOnlyList<int> MutationDebuts => _mutDebut;

    private bool TraitAllowedFor(TdMutationTraitDef t, TdEnemyStats st)
    {
        if (st.IsBoss || st.NativeTraits.Contains(t.Id)) return false; // chefes fora; traço nativo não se repete
        if (t.Flying && st.Flying) return false;
        if (t.ShieldPct > 0 && st.ShieldHp > 0) return false; // base com escudo próprio não ganha outro
        if (t.SpeedMult != 1)
        {
            var m = _content.Mutations;
            if (Math.Clamp(st.Speed * t.SpeedMult, m.SpeedMin, m.SpeedMax) == st.Speed) return false; // sem efeito
        }
        return true;
    }

    /// <summary>
    /// Escolhe os traços de cada spawn (índice = ordem de enumeração dos grupos; 0 = sem mutação) e REGISTRA as estreias em <paramref name="debut"/>.
    /// Passe uma cópia para só prever.
    /// </summary>
    internal ushort[] PlanMutations(int absWave, int stageNumber, TdWaveDef def, int[] debut)
    {
        var enemyIdx = new List<int>();
        foreach (var g in def.Groups)
        {
            int idx = Array.FindIndex(_enemyStats, s => s.Id == g.Enemy);
            for (int i = 0; i < g.Count; i++) enemyIdx.Add(idx);
        }
        int n = enemyIdx.Count;
        var plan = new ushort[n];
        var mut = _content.Mutations;
        if (_options.IsClassic || def.Rest || n == 0) return plan;
        TdMutationTierDef? tier = null;
        foreach (var t in mut.Tiers)
            if (stageNumber >= t.MinStage && absWave - 1 >= t.MinWaves) tier = t;
        if (tier is null) return plan;

        var traits = mut.Traits;
        var options = new List<int>[n]; // traços disponíveis por spawn (índices do catálogo, em ordem)
        var cands = new List<int>();
        for (int i = 0; i < n; i++)
        {
            var st = _enemyStats[enemyIdx[i]];
            if (st.IsBoss) continue;
            var list = new List<int>();
            for (int k = 0; k < traits.Length; k++)
                if (stageNumber >= traits[k].MinStage && TraitAllowedFor(traits[k], st)) list.Add(k);
            options[i] = list;
            if (list.Count > 0) cands.Add(i);
        }
        int e = cands.Count;
        int maxM = (int)Math.Floor(e * tier.MaxMutatedPct + 1e-9);
        if (e >= mut.MinEligibleForOne && maxM < 1) maxM = 1;
        int m = Math.Min(Math.Min(maxM, e), (int)Math.Floor(n * 0.3 + 1e-9)); // pelo menos 70% sem mutação adicionada
        if (m <= 0) return plan;

        var rng = Rng.Seed(_mutSeed, (ulong)absWave * 2UL + 1UL);
        var chosen = new int[m]; // um por faixa da agenda: distribui ao longo da onda
        for (int k = 0; k < m; k++)
        {
            int lo = k * e / m, hi = (k + 1) * e / m;
            chosen[k] = cands[lo + (int)(rng.NextUInt() % (uint)(hi - lo))];
        }
        int doubles = tier.MaxDoubles >= 0 ? tier.MaxDoubles : (int)Math.Floor(m * tier.DoublePct + 1e-9);
        if (m < mut.DoubleMinMutated) doubles = 0;
        doubles = Math.Min(doubles, m);
        var wantDouble = new bool[m];
        var order = Enumerable.Range(0, m).ToArray(); // Fisher-Yates determinista
        for (int k = m - 1; k > 0; k--) { int j = (int)(rng.NextUInt() % (uint)(k + 1)); (order[k], order[j]) = (order[j], order[k]); }
        for (int k = 0; k < doubles; k++) wantDouble[order[k]] = true;

        var snapshot = (int[])debut.Clone(); // maturidade medida no início da onda
        bool MatureSnap(int t) => snapshot[t] > 0 && absWave - snapshot[t] >= mut.DoubleGapWaves;
        for (int k = 0; k < m; k++)
        {
            int i = chosen[k];
            var opts = options[i]!;
            int first;
            ushort mask;
            var matured = opts.Where(MatureSnap).ToList();
            if (wantDouble[k] && matured.Count >= 2)
            {
                first = matured[(int)(rng.NextUInt() % (uint)matured.Count)];
                var seconds = matured.Where(t => t != first && !traits[first].Excludes.Contains(traits[t].Id)).ToList();
                if (seconds.Count > 0)
                {
                    int second = seconds[(int)(rng.NextUInt() % (uint)seconds.Count)];
                    mask = (ushort)((1 << first) | (1 << second));
                    plan[i] = mask;
                    continue;
                }
            }
            first = opts[(int)(rng.NextUInt() % (uint)opts.Count)];
            plan[i] = (ushort)(1 << first);
        }
        for (int i = 0; i < n; i++)
            for (int t = 0; t < traits.Length; t++)
                if ((plan[i] & (1 << t)) != 0 && debut[t] == 0) debut[t] = absWave;
        return plan;
    }

    private TdEnemy ApplyMutations(TdEnemy e, TdEnemyStats st, ushort mask)
    {
        var mut = _content.Mutations;
        double hp = e.MaxHp, speed = e.Speed, armor = e.Armor, shield = e.ShieldHp;
        int added = 0;
        bool shielded = false, speedChanged = false;
        double shieldPct = 0;
        for (int k = 0; k < mut.Traits.Length; k++)
        {
            if ((mask & (1 << k)) == 0) continue;
            var t = mut.Traits[k];
            added++;
            hp *= t.HpMult; // ordem do SDD: HP, velocidade, armadura, escudo
            if (t.SpeedMult != 1) { speed *= t.SpeedMult; speedChanged = true; }
            armor += t.ArmorAdd;
            if (t.Flying) e.Flying = true;
            if (t.ShieldPct > 0) { shielded = true; shieldPct = Math.Max(shieldPct, t.ShieldPct); }
        }
        if (speedChanged) speed = Math.Clamp(speed, mut.SpeedMin, mut.SpeedMax);
        if (shielded && st.ShieldHp <= 0) shield = hp * shieldPct;
        double reward = 1.0;
        for (int k = 0; k < added; k++) reward = Math.Min(mut.RewardCap, reward * mut.RewardPerTrait); // x1,15 por traço, teto x1,3
        e.Hp = e.MaxHp = hp;
        e.Speed = speed;
        e.Armor = armor;
        e.ShieldHp = shield;
        e.Gold = (long)Math.Floor(e.Gold * reward + 1e-9);
        e.Mutations = mask;
        return e;
    }

    /// <summary>Velocidade de nascimento de um spawn já com seus traços (limites de velocidade do catálogo).</summary>
    private double SpawnSpeed(TdSpawn sp)
    {
        var mut = _content.Mutations;
        double speed = _enemyStats[sp.EnemyIndex].Speed;
        bool changed = false;
        for (int k = 0; k < mut.Traits.Length; k++)
            if ((sp.Mutations & (1 << k)) != 0 && mut.Traits[k].SpeedMult != 1) { speed *= mut.Traits[k].SpeedMult; changed = true; }
        return changed ? Math.Clamp(speed, mut.SpeedMin, mut.SpeedMax) : speed;
    }

    // ---------------------------------------------------------------- API de consulta (prévia, rótulos, união de traços)

    /// <summary>Ids dos traços ADICIONADOS (ordem do catálogo) de uma máscara.</summary>
    public IReadOnlyList<string> MutationTraitIds(ushort mask)
    {
        var list = new List<string>();
        var traits = _content.Mutations.Traits;
        for (int k = 0; k < traits.Length; k++)
            if ((mask & (1 << k)) != 0) list.Add(traits[k].Id);
        return list;
    }

    /// <summary>Rótulos dos traços adicionados, ex.: ["Tank", "Lento"].</summary>
    public IReadOnlyList<string> MutationLabels(ushort mask)
    {
        var list = new List<string>();
        var traits = _content.Mutations.Traits;
        for (int k = 0; k < traits.Length; k++)
            if ((mask & (1 << k)) != 0) list.Add(traits[k].Label);
        return list;
    }

    /// <summary>Nome exibido: nome da base seguido dos rótulos (ex.: "Tráfego pesado Rápido"); sem mutação, só o nome da base.</summary>
    public string MutatedName(int defIndex, ushort mask)
    {
        var def = defIndex < _content.Enemies.Enemies.Length ? _content.Enemies.Enemies[defIndex].Name : _content.Enemies.Bosses[defIndex - _content.Enemies.Enemies.Length].Name;
        return mask == 0 ? def : def + " " + string.Join(" ", MutationLabels(mask));
    }

    /// <summary>União dos traços nativos e adicionados de um inimigo em campo, sem repetição (consulta e avisos; não multiplica vantagens, RF-37.9).</summary>
    public IReadOnlyList<string> EnemyTraitUnion(in TdEnemy e)
    {
        var list = new List<string>(_enemyStats[e.DefIndex].NativeTraits);
        foreach (var id in MutationTraitIds(e.Mutations))
            if (!list.Contains(id)) list.Add(id);
        return list;
    }

    /// <summary>
    /// Mutações previstas para a PRÓXIMA onda a iniciar (a que <c>StartWave</c>/auto-início/chamada antecipada vai montar), idênticas à agenda efetiva:
    /// só traços e quantidade, sem tocar em RNG nem estado. Vazio sem mutação, no modo clássico ou sem próxima onda. Depois da última onda da fase
    /// (corrida persistente) prevê a onda 1 da fase seguinte.
    /// </summary>
    public IReadOnlyList<TdMutationPreview> PreviewNextWaveMutations()
    {
        if (_options.IsClassic || Phase is MatchPhase.Vitoria) return [];
        TdWaveDef def;
        int stageNumber = _stageNumber;
        if (NextWaveNumber > 0) def = _waves[NextWaveNumber - 1];
        else if (_options.PersistentRun)
        {
            stageNumber++;
            var defs = _content.Stages.Stages;
            def = _content.Waves.Stages.Single(s => s.Stage == defs[(stageNumber - 1) % defs.Length].Id).Waves[0];
        }
        else return [];
        var plan = PlanMutations(_runWaves + 1, stageNumber, def, (int[])_mutDebut.Clone());
        var enemyIdx = new List<int>();
        foreach (var g in def.Groups)
        {
            int idx = Array.FindIndex(_enemyStats, s => s.Id == g.Enemy);
            for (int i = 0; i < g.Count; i++) enemyIdx.Add(idx);
        }
        return Group(enemyIdx.Select((idx, i) => (idx, plan[i])));
    }

    /// <summary>Mutações de uma onda em andamento (agenda já fixada, incluindo spawns que ainda não nasceram).</summary>
    public static IReadOnlyList<TdMutationPreview> ActiveWaveMutations(TdActiveWave wave) =>
        Group(wave.Agenda.Select(s => (s.EnemyIndex, s.Mutations)));

    private static List<TdMutationPreview> Group(IEnumerable<(int Enemy, ushort Mask)> items)
    {
        var order = new List<(int Enemy, ushort Mask)>();
        var counts = new Dictionary<(int, ushort), int>();
        foreach (var it in items)
        {
            if (it.Mask == 0) continue;
            if (!counts.TryAdd(it, 1)) counts[it]++;
            else order.Add(it);
        }
        return order.Select(o => new TdMutationPreview(o.Enemy, o.Mask, counts[o])).ToList();
    }
}
