using System.Globalization;
using System.Text;
using SideWar.Core.Content.Td;
using SideWar.Core.Td;
using Xunit;

namespace SideWar.Core.Tests;

/// <summary>TD.10: SanityBot TD como regressão do balanceamento (RF-28.12, DP-28.10).</summary>
public sealed class TdSanityBotTests
{
    private static readonly TdContent Content = TdContentLoader.LoadEmbedded();

    internal static string Dump(TdContent content, int seeds)
    {
        var sb = new StringBuilder();
        sb.AppendLine("fase perfil     vit  estrelas  perda  creditos  onda  combate_s  total_s  rej  perde_onda  kills");
        for (int stage = 1; stage <= 6; stage++)
            foreach (var p in Enum.GetValues<TdBotProfile>())
            {
                var rs = Enumerable.Range(0, seeds).Select(i => TdSanityBot.Run(content, stage, p, (ulong)(100 + i))).ToArray();
                double wins = rs.Count(r => r.Won) / (double)seeds;
                double stars = rs.Average(r => r.Stars);
                double lost = rs.Average(r => r.IntegrityLost);
                double cr = rs.Average(r => r.CreditsLeft);
                double wv = rs.Average(r => r.WaveReached);
                double cs = rs.Average(r => r.CombatSeconds);
                double ts = rs.Average(r => r.TotalSeconds); double rejA = rs.Average(r => r.CommandsRejected);
                var losses = rs.Where(r => !r.Won).Select(r => r.WaveReached).ToArray();
                var kills = rs.SelectMany(r => r.KillsByTowerType).GroupBy(k => k.Key).OrderBy(g => g.Key)
                    .Select(g => $"{g.Key}:{g.Sum(k => k.Value) / seeds}");
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"{stage,4} {p,-10} {wins,4:P0} {stars,8:F1} {lost,6:F1} {cr,9:F0} {wv,5:F1} {cs,10:F0} {ts,8:F0} {rejA,4:F0}  {(losses.Length == 0 ? "-" : string.Join(",", losses.Distinct().Order()))}  {string.Join(" ", kills)}"));
            }
        return sb.ToString();
    }

    private const ulong GoldenOptimizedStage3 = 6667367544354793914UL;
    private const int Seeds = 6;
    private static readonly Lazy<TdBotResult[]> Matrix = new(() =>
    {
        var list = new List<TdBotResult>();
        for (int stage = 1; stage <= 6; stage++)
            foreach (var p in Enum.GetValues<TdBotProfile>())
                for (int i = 0; i < Seeds; i++)
                    list.Add(TdSanityBot.Run(Content, stage, p, (ulong)(100 + i)));
        return [.. list];
    });

    private static TdBotResult[] Cell(int stage, TdBotProfile p) => Matrix.Value.Where(r => r.Stage == stage && r.Profile == p).ToArray();
    private static double WinRate(int stage, TdBotProfile p) => Cell(stage, p).Count(r => r.Won) / (double)Seeds;
    private static double Stars(int stage, TdBotProfile p) => Cell(stage, p).Average(r => r.Stars);

    [Fact(DisplayName = "TD.10 Novato (só o básico) vence as fases 1 e 2 e perde a fase 6")]
    public void Novato_WinsEarlyStages_LosesFinal()
    {
        for (int stage = 1; stage <= 2; stage++) Assert.True(WinRate(stage, TdBotProfile.Novato) >= 5.0 / Seeds, $"fase {stage}");
        Assert.True(WinRate(6, TdBotProfile.Novato) <= 1.0 / Seeds, "fase 6 deve ser derrota esperada para o novato");
    }

    [Fact(DisplayName = "TD.10 Médio vence as fases 1 a 5 com média de ★ >= 2")]
    public void Medio_WinsStages1To5_WithTwoStars()
    {
        for (int stage = 1; stage <= 5; stage++)
        {
            Assert.True(WinRate(stage, TdBotProfile.Medio) >= 5.0 / Seeds, $"vitórias fase {stage}");
            Assert.True(Stars(stage, TdBotProfile.Medio) >= 2.0, $"estrelas fase {stage}: {Stars(stage, TdBotProfile.Medio):F2}");
        }
    }

    [Fact(DisplayName = "TD.10 Otimizado tira ★ >= 2,5 em média nas 6 fases e nunca fica pior que o médio")]
    public void Otimizado_ThreeStarsEverywhere()
    {
        for (int stage = 1; stage <= 6; stage++)
        {
            Assert.True(WinRate(stage, TdBotProfile.Otimizado) >= 5.0 / Seeds, $"vitórias fase {stage}");
            Assert.True(Stars(stage, TdBotProfile.Otimizado) >= 2.5, $"estrelas fase {stage}: {Stars(stage, TdBotProfile.Otimizado):F2}");
            Assert.True(Stars(stage, TdBotProfile.Otimizado) >= Stars(stage, TdBotProfile.Medio), $"habilidade deve compensar na fase {stage}");
        }
    }

    [Fact(DisplayName = "TD.10 Cada fase exige o contra do seu conceito: quem o ignora perde (CA-28.6)")]
    public void IgnoringTheCounter_Loses()
    {
        for (int stage = 1; stage <= 6; stage++)
            Assert.True(WinRate(stage, TdBotProfile.SemContra) <= 1.0 / Seeds, $"fase {stage}: SemContra venceu {WinRate(stage, TdBotProfile.SemContra):P0}");
    }

    [Fact(DisplayName = "TD.10 Duração de partida de 7,5 a 15 min (DP-28.10) para quem vence, e as ondas terminam")]
    public void Duration_WithinTarget()
    {
        foreach (var r in Matrix.Value.Where(r => r.Won))
        {
            Assert.InRange(r.TotalSeconds / 60.0, 7.5, 15.5);
            Assert.Equal(r.Stage switch { 1 => 10, 2 => 12, 3 => 12, 4 => 14, 5 => 14, _ => 15 }, r.WaveReached);
        }
    }

    [Fact(DisplayName = "TD.10 SanityBot é determinístico: mesma fase, perfil e seed dão o mesmo StateHash; seed diferente muda")]
    public void Bot_IsDeterministic()
    {
        var a = TdSanityBot.Run(Content, 3, TdBotProfile.Otimizado, 100);
        var b = TdSanityBot.Run(Content, 3, TdBotProfile.Otimizado, 100);
        var c = TdSanityBot.Run(Content, 3, TdBotProfile.Otimizado, 101);
        Assert.Equal(a.StateHash, b.StateHash);
        Assert.Equal(a.IntegrityLost, b.IntegrityLost);
        Assert.NotEqual(a.StateHash, c.StateHash);
        Assert.Equal(GoldenOptimizedStage3, a.StateHash); // golden do conteúdo embarcado balanceado (TD.10)
    }

    [Fact(DisplayName = "TD.10 Habilidade importa: o Otimizado usa os comandos da ficha sem estourar rejeições")]
    public void Optimized_UsesCommandsCleanly()
    {
        foreach (var r in Matrix.Value.Where(r => r.Profile == TdBotProfile.Otimizado))
            Assert.True(r.CommandsRejected <= 12, $"fase {r.Stage} seed {r.Seed}: {r.CommandsRejected} rejeições");
    }

    [Fact]
    public void DumpOne()
    {
        string? spec = Environment.GetEnvironmentVariable("TD10_ONE"); // fase,perfil,seed
        string? path = Environment.GetEnvironmentVariable("TD10_DUMP");
        if (string.IsNullOrEmpty(spec) || string.IsNullOrEmpty(path)) return;
        var a = spec.Split(',');
        var r = TdSanityBot.Run(Content, int.Parse(a[0], CultureInfo.InvariantCulture), Enum.Parse<TdBotProfile>(a[1]), ulong.Parse(a[2], CultureInfo.InvariantCulture));
        File.WriteAllText(path, $"{r.Phase} stars={r.Stars} lost={r.IntegrityLost} credits={r.CreditsLeft} wave={r.WaveReached} towers={r.TowersBuilt} rej={r.CommandsRejected}\nlostByWave={string.Join(",", r.LostByWave)}\n");
    }

    /// <summary>Gera a tabela da evidência (só com <c>TD10_DUMP</c>); <c>TD10_CONTENT_DIR</c> carrega td-*.json de uma pasta (comparação antes/depois).</summary>
    [Fact]
    public void DumpMatrix()
    {
        string? path = Environment.GetEnvironmentVariable("TD10_DUMP");
        if (string.IsNullOrEmpty(path)) return;
        var content = Content;
        string? dir = Environment.GetEnvironmentVariable("TD10_CONTENT_DIR");
        if (!string.IsNullOrEmpty(dir))
        {
            var files = TdContentLoader.FileNames.ToDictionary(n => n, n => File.ReadAllBytes(Path.Combine(dir, n)), StringComparer.Ordinal);
            content = TdContentLoader.Load(files).Content ?? throw new InvalidOperationException("conteúdo de comparação inválido");
        }
        File.WriteAllText(path, Dump(content, int.Parse(Environment.GetEnvironmentVariable("TD10_SEEDS") ?? "5", CultureInfo.InvariantCulture)));
    }
}
