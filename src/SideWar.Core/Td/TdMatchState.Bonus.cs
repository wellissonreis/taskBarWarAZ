using SideWar.Core.Simulation;

namespace SideWar.Core.Td;

// Pergunta-bônus do Preparo (SDD-28 RF-28.23) e estatísticas de derrota (RF-28.24). Os contadores de derrota ficam FORA do StateHash.
public sealed partial class TdMatchState
{
    private int[]? _leakCount;
    private int[]? _leakDamage;
    private int _crateTowersLost;
    private int _bonusCorrect;
    private int _bonusWrong;

    /// <summary>Já houve resposta à pergunta-bônus neste Preparo (entra no hash: bloqueia a 2ª resposta).</summary>
    public bool BonusAnswered => _bonusAnswered;
    public int BonusCorrectCount => _bonusCorrect;
    public int BonusWrongCount => _bonusWrong;

    /// <summary>Créditos pagos por um acerto antes da onda <paramref name="wave"/> (<c>creditsBase + creditsPerWave × onda</c>).</summary>
    public long BonusCreditsFor(int wave)
    {
        return _content.Runes.CreditSources.BonusQuestionBase + (long)_content.Runes.CreditSources.BonusQuestionPerWave * Math.Max(1, wave);
    }

    private CommandResult DoAnswerBonus(AnswerBonus cmd)
    {
        // Clássico: só em Preparo. Contínuo (TD.28a): não há Preparo entre ondas; a pergunta vale durante a onda em andamento (1 por onda iniciada).
        if (Phase != MatchPhase.Preparo && !(_options.ContinuousWaves && Phase == MatchPhase.Onda)) return Reject(CommandReason.InvalidPhase);
        if (_bonusAnswered) return Reject(CommandReason.AlreadyUsed);
        _bonusAnswered = true;
        long paid = 0;
        if (cmd.Correct)
        {
            _bonusCorrect++;
            var q = _content.Stages.Rules.BonusQuestion;
            paid = BonusCreditsFor(Phase == MatchPhase.Preparo ? NextWaveNumber : WaveNumber);
            Credits = Sat.Add(Credits, paid);
            GainFragments(_content.Stages.Rules.Fragments.PerQuestion);
            int red = q.CooldownReductionSeconds * TdWorld.TicksPerSecond;
            for (int i = 0; i < _abilityCd.Length; i++) _abilityCd[i] = Math.Max(0, _abilityCd[i] - red);
        }
        else _bonusWrong++;
        Emit(TdEventType.BonusAnswered, cmd.QuestionId, paid);
        return CommandResult.Ok;
    }

    private void RecordLeak(TdEnemy e)
    {
        _leakCount ??= new int[_enemyStats.Length];
        _leakDamage ??= new int[_enemyStats.Length];
        if (e.DefIndex < 0 || e.DefIndex >= _leakCount.Length) return;
        _leakCount[e.DefIndex]++;
        _leakDamage[e.DefIndex] += e.CoreDamage;
    }

    /// <summary>Vazamentos por tipo de inimigo/chefe (só os que vazaram), em ordem do conteúdo.</summary>
    public IReadOnlyList<TdLeakStat> Leaks
    {
        get
        {
            var list = new List<TdLeakStat>();
            if (_leakCount is null) return list;
            for (int i = 0; i < _leakCount.Length; i++)
                if (_leakCount[i] > 0)
                    list.Add(new TdLeakStat(_enemyStats[i].Id, _enemyStats[i].IsBoss, _enemyStats[i].Flying, _leakCount[i], _leakDamage![i]));
            return list;
        }
    }

    /// <summary>Torres destruídas por explosão de caixa-bomba.</summary>
    public int CrateTowersLost => _crateTowersLost;

    /// <summary>Motivo principal da derrota (RF-28.24), derivado dos contadores; só faz sentido em Derrota.</summary>
    public TdDefeatInfo DefeatAnalysis() => TdDefeat.Analyze(this);
}

/// <summary>Vazamento agregado de um tipo de inimigo: quantos chegaram ao núcleo e quanta integridade tiraram.</summary>
public readonly record struct TdLeakStat(string EnemyId, bool IsBoss, bool Flying, int Count, int Damage);

public enum TdDefeatReason : byte { BossLeaked, CrateBlast, FlyersLeaked, FewTowersNearPortal, EnemyLeaked }

public enum TdDefeatTip : byte { UseAbility, ClickCrate, UseCounters, BuildNearPortal }

/// <summary>Motivo + dica da derrota. <c>EnemyId</c> = tipo que mais vazou (ou chefe); <c>CounterTowerIds</c> = até 2 torres que o contra-atacam (ids do td-towers.json).</summary>
public sealed record TdDefeatInfo(TdDefeatReason Reason, TdDefeatTip Tip, string? EnemyId, IReadOnlyList<string> CounterTowerIds);

/// <summary>Análise determinística e barata da derrota (regras simples, na ordem: chefe, caixa, voadores, poucas torres perto do portal, inimigo que mais vazou).</summary>
public static class TdDefeat
{
    /// <summary>Metade do caminho a partir do portal (X lógico ≥ 600) conta como "perto do portal".</summary>
    public const int NearPortalX = 600;
    public const int MinTowersNearPortal = 2;

    public static TdDefeatInfo Analyze(TdMatchState s)
    {
        var leaks = s.Leaks;
        int total = 0, flyDmg = 0;
        TdLeakStat? top = null, boss = null;
        foreach (var l in leaks)
        {
            total += l.Damage;
            if (l.Flying) flyDmg += l.Damage;
            if (l.IsBoss && (boss is null || l.Damage > boss.Value.Damage)) boss = l;
            if (top is null || l.Damage > top.Value.Damage) top = l;
        }
        if (boss is { } b) return Make(s, TdDefeatReason.BossLeaked, TdDefeatTip.UseAbility, b.EnemyId);
        int placed = 0, near = 0;
        foreach (var t in s.Towers)
        {
            if (!t.Placed) continue;
            placed++;
            if (t.X >= NearPortalX) near++;
        }
        if (s.CrateTowersLost >= 2 || (s.CrateTowersLost >= 1 && placed <= 3))
            return Make(s, TdDefeatReason.CrateBlast, TdDefeatTip.ClickCrate, null);
        if (total > 0 && flyDmg * 2 >= total)
        {
            string? fly = null;
            foreach (var l in leaks) if (l.Flying && (fly is null || l.Damage > Dmg(leaks, fly))) fly = l.EnemyId;
            return Make(s, TdDefeatReason.FlyersLeaked, TdDefeatTip.UseCounters, fly);
        }
        if (near < MinTowersNearPortal) return Make(s, TdDefeatReason.FewTowersNearPortal, TdDefeatTip.BuildNearPortal, top?.EnemyId);
        return Make(s, TdDefeatReason.EnemyLeaked, TdDefeatTip.UseCounters, top?.EnemyId);
    }

    private static int Dmg(IReadOnlyList<TdLeakStat> l, string id)
    {
        foreach (var x in l) if (x.EnemyId == id) return x.Damage;
        return 0;
    }

    private static TdDefeatInfo Make(TdMatchState s, TdDefeatReason r, TdDefeatTip tip, string? enemyId)
    {
        var counters = new List<string>();
        if (enemyId is not null)
        {
            string[] raw = [];
            foreach (var e in s.Content.Enemies.Enemies) if (e.Id == enemyId) raw = e.Counters;
            foreach (var e in s.Content.Enemies.Bosses) if (e.Id == enemyId) raw = e.Counters;
            foreach (var c in raw)
            {
                int dot = c.IndexOf('.');
                string id = dot < 0 ? c : c[..dot];
                if (!counters.Contains(id) && counters.Count < 2) counters.Add(id);
            }
        }
        return new TdDefeatInfo(r, tip, enemyId, counters);
    }
}
