using SideWar.Core.Content.Td;

namespace SideWar.Core.Td;

public sealed partial class TdMatchState
{
    // INFINITO: depois da fase 6 entram ciclos ("Ciclo 2, 3, ..."): as 6 regiões se repetem com escala crescente e sem teto. Tudo determinístico
    // por (ciclo, fase, onda): nenhum sorteio novo (o RNG da partida só mexe no jitter dos spawns, como sempre).

    /// <summary>Quantidade de inimigos comuns por ciclo: +12% por ciclo (arredondada para cima).</summary>
    public const double CycleCountStep = 0.12;
    /// <summary>Cadência: o intervalo entre spawns encurta 6% por ciclo (mais densidade sem alongar demais a onda).</summary>
    public const double CycleIntervalStep = 0.06;
    /// <summary>Velocidade dos inimigos comuns: +4% por ciclo, sem teto artificial.</summary>
    public const double CycleSpeedStep = 0.04;

    /// <summary>Inimigos extras que os ciclos misturam nas ondas (blindados, enxames, tanques, voadores, corredores) e o intervalo base de cada um.</summary>
    private static readonly (string Enemy, double Interval)[] CycleExtras =
        [("armored", 3.0), ("swarm", 0.8), ("tank", 4.8), ("flyer", 2.3), ("runner", 1.5)];

    /// <summary>Ciclo da fase absoluta <paramref name="stageNumber"/> (0 = primeira volta pelas fases do conteúdo).</summary>
    private int CycleOf(int stageNumber) => (Math.Max(1, stageNumber) - 1) / _content.Stages.Stages.Length;

    /// <summary>Multiplicador de velocidade dos inimigos comuns no ciclo atual.</summary>
    private double CycleSpeed() => 1.0 + CycleSpeedStep * Cycle;

    /// <summary>Onda <paramref name="w"/> da fase atual já escalada pelo ciclo (ciclo 0 devolve o conteúdo intacto).</summary>
    private TdWaveDef EffectiveWave(int w) => EffectiveWave(_waves, w, _stageNumber);

    /// <summary>Onda <paramref name="wave"/> (1 a <see cref="TotalWaves"/>) da fase atual como será jogada: quantidades, cadência e extras do ciclo aplicados.</summary>
    public TdWaveDef EffectiveWaveDef(int wave) =>
        EffectiveWave(Math.Clamp(wave, 1, _waves.Length));

    private TdWaveDef EffectiveWave(TdWaveDef[] waves, int w, int stageNumber)
    {
        var def = waves[w - 1];
        int cycle = CycleOf(stageNumber);
        if (cycle == 0) return def;
        double countMul = 1.0 + CycleCountStep * cycle;
        double intervalDiv = 1.0 + CycleIntervalStep * cycle;
        var groups = new List<TdWaveGroupDef>(def.Groups.Length + 1);
        foreach (var g in def.Groups)
        {
            int idx = Array.FindIndex(_enemyStats, s => s.Id == g.Enemy);
            if (idx >= 0 && _enemyStats[idx].IsBoss) { groups.Add(g); continue; } // chefe: sempre 1, sem escala de quantidade
            int count = (int)Math.Ceiling(g.Count * countMul - 1e-9);
            groups.Add(g with { Count = Math.Max(g.Count, count), Interval = g.Interval / intervalDiv });
        }
        // Modificador do ciclo: a cada 3 ondas (deslocado pela fase) um grupo extra de um inimigo misto; a onda final (chefe) não recebe.
        if (w < waves.Length && (w + stageNumber) % 3 == 0)
        {
            var (enemy, interval) = CycleExtras[(cycle * 7 + stageNumber + w) % CycleExtras.Length];
            groups.Add(new TdWaveGroupDef(enemy, Math.Min(14, 2 + cycle), 2.0, interval / intervalDiv));
        }
        return def with { Groups = [.. groups] };
    }

    /// <summary>Créditos de largada nos ciclos (fase absoluta &gt; número de fases do conteúdo): a corrida retomada não recomeça do zero, ganha uma defesa inicial.</summary>
    private static long CycleCatchUpCredits(TdContent content, int stageNumber)
    {
        int count = content.Stages.Stages.Length;
        int cycle = (Math.Max(1, stageNumber) - 1) / count;
        if (cycle == 0) return 0;
        var table = content.Stages.Rules.TowerCostTable;
        int towers = Math.Min(24, 12 + 2 * (cycle - 1));
        long sum = 0;
        for (int i = 0; i < towers && i < table.Length; i++) sum += table[i];
        return sum * 6 / 10;
    }
}
