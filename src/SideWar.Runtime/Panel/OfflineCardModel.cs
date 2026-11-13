using SideWar.Core.Economy;
using SideWar.Core.Simulation;
using SideWar.Runtime.Presence;

namespace SideWar.Runtime.Panel;

/// <summary>Cor semântica do valor ou da sublinha de um tile (tokens de SDD-20, resolvidos no renderer).</summary>
public enum OfflineTone { Principal, Ambar, Rotulo, Menta }

/// <summary>Tile 2 × 2 do card de retorno (SDD-24 RF-24.10): rótulo, número em Silkscreen 20 e sublinha.</summary>
public sealed record OfflineTile(string Label, string Value, OfflineTone ValueTone, string Sub, OfflineTone SubTone);

/// <summary>
/// Modelo imutável do card "Bem-vindo de volta" (SDD-24 RF-24.10), montado a partir do pendente (RF-25.8/25.9).
/// Puro: nenhum acesso a relógio nem janela. <see cref="WarningText"/> é null quando não há perdas (omitido).
/// </summary>
public sealed record OfflineCardModel(
    string Overline, string Title, string IntroPrefix, string TimeText, string IntroSuffix,
    string TimeLabelRight, float TimeFraction, IReadOnlyList<OfflineTile> Tiles, string? WarningText,
    string CollectX2Label, string CollectX2Hint, string CollectLabel, long Version = 0)
{
    public const string OverlineText = "RELATÓRIO DO MODO GERENCIADO";
    public const string TitleText = "BEM-VINDO DE VOLTA";
    public const string X2Hint = "responda 1 pergunta Azure da fase";
    public const string AutoScaleTail = ". A auto-escala segurou o núcleo.";
    public const double MaxSeconds = 12 * 3600;

    /// <summary>Texto completo do parágrafo de abertura.</summary>
    public string Intro => IntroPrefix + TimeText + IntroSuffix;

    /// <summary><c>3h 12min</c>, <c>3h</c> ou <c>25min</c> (formato não definido no design além do exemplo; DP-24.12).</summary>
    public static string FormatDuration(double seconds)
    {
        var totalMin = (int)Math.Floor(Math.Max(0, seconds) / 60.0);
        var h = totalMin / 60;
        var m = totalMin % 60;
        return h == 0 ? $"{m}min" : m == 0 ? $"{h}h" : $"{h}h {m}min";
    }

    public static string TowerName(TowerType t) => t switch
    {
        TowerType.Bolt => "Raio",
        TowerType.Frost => "Gelo",
        TowerType.Cannon => "Canhão",
        TowerType.ScaleSet => "Scale Set",
        _ => "?",
    };

    /// <summary>
    /// Monta o modelo. <paramref name="autoScaleOn"/> controla a frase "A auto-escala segurou o núcleo." (só ligada e com
    /// melhorias, DP-24.12). <paramref name="fitUpgrades"/> decide quantas entradas da lista cabem na linha do tile 4:
    /// recebe o texto candidato e devolve se cabe (o layout injeta a medida real).
    /// </summary>
    public static OfflineCardModel From(PendingRewards p, bool autoScaleOn, Func<string, bool>? fitUpgrades = null, long version = 0)
    {
        ArgumentNullException.ThrowIfNull(p);
        var r = p.Report;
        var time = FormatDuration(r.TempoAcumuladoS);
        var suffix = autoScaleOn && r.NMelhorias > 0 ? AutoScaleTail : ".";
        var fraction = (float)Math.Clamp(r.TempoAcumuladoS / MaxSeconds, 0.0, 1.0);

        var ondasSub = r.Quedas > 0 ? (r.Quedas == 1 ? "1 queda" : $"{r.Quedas} quedas") : "sem perder vida";
        var tiles = new OfflineTile[]
        {
            new("CRÉDITOS", "+" + CreditFormatter.Format(p.Credits), OfflineTone.Ambar, "a ×0,25", OfflineTone.Rotulo),
            new("ONDAS", $"{r.OndaDe} → {r.OndaPara}", OfflineTone.Principal, ondasSub, OfflineTone.Rotulo),
            new("XP", "+" + CreditFormatter.Format(p.Xp), OfflineTone.Principal, $"LV {r.NivelDe} → {r.NivelPara}", OfflineTone.Menta),
            new("AUTO-ESCALA", r.NMelhorias == 1 ? "1 melhoria" : $"{r.NMelhorias} melhorias", OfflineTone.Principal,
                UpgradeList(r, fitUpgrades), OfflineTone.Rotulo),
        };

        return new(OverlineText, TitleText, "Você esteve fora por ", time, suffix,
            time + " / 12h máx.", fraction, tiles, Warning(r), "COLETAR ×2", X2Hint, "COLETAR +" + CreditFormatter.Format(p.Credits), version);
    }

    /// <summary>Lista <c>Raio 12→14 · Gelo 8→9</c> com quantas entradas couberem, terminando em <c>· +N</c> (DP-24.12).</summary>
    public static string UpgradeList(OfflineReport r, Func<string, bool>? fits = null)
    {
        if (r.MelhoriasAuto.Count == 0) return "sem melhorias";
        fits ??= s => s.Length <= 30;
        var entries = new List<string>(r.MelhoriasAuto.Count);
        foreach (var u in r.MelhoriasAuto) entries.Add($"{TowerName(u.Tipo)} {u.DeNivel}→{u.ParaNivel}");
        var best = 0;
        for (var n = 1; n <= entries.Count; n++)
        {
            var text = string.Join(" · ", entries.Take(n)) + (n < entries.Count ? $" · +{entries.Count - n}" : "");
            if (fits(text)) best = n; else break;
        }
        if (best == 0) return $"+{entries.Count}";
        return string.Join(" · ", entries.Take(best)) + (best < entries.Count ? $" · +{entries.Count - best}" : "");
    }

    /// <summary>Aviso de perdas (RF-25.8): omitido se tudo for 0. Plural simples; ordem do design: chefe, picos, caixas.</summary>
    public static string? Warning(OfflineReport r)
    {
        var parts = new List<string>(3);
        if (r.PerdaChefes > 0) parts.Add(r.PerdaChefes == 1 ? "1 chefe (pergunta Azure)" : $"{r.PerdaChefes} chefes (pergunta Azure)");
        if (r.PerdaPicos > 0) parts.Add(r.PerdaPicos == 1 ? "1 pico de demanda" : $"{r.PerdaPicos} picos de demanda");
        if (r.PerdaCaixas > 0) parts.Add(r.PerdaCaixas == 1 ? "1 caixa" : $"{r.PerdaCaixas} caixas");
        if (parts.Count == 0) return null;
        var list = parts.Count == 1 ? parts[0]
            : string.Join(", ", parts.Take(parts.Count - 1)) + " e " + parts[^1];
        return $"Perdido por estar ausente: {list}. Esses só aparecem com você na tela.";
    }
}
