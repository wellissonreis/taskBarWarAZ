using SideWar.Core.Simulation;

namespace SideWar.Runtime.Panel;

/// <summary>Linha da lista de fases (RF-24.6 §6.2): <c>Stars</c> 0..3, ou bloqueada (<c>BLOQ.</c>, opacidade 0,55).</summary>
public sealed record CampaignRowModel(string Number, string Title, int Stars, bool Locked)
{
    public float Opacity => Locked ? 0.55f : 1f;
}

/// <summary>Modelo imutável da aba Campanha (card da fase atual + lista). Só leitura; nenhum alvo clicável (RF-24.6).</summary>
public sealed record CampaignModel(
    string StageLabel, string StageTitle, int Wave, int TotalWaves, string WaveText, string BossText, IReadOnlyList<CampaignRowModel> Rows)
{
    /// <summary>Fração da barra = onda atual / total (design: 37/60 → 62%), em [0,1]. O arredondamento para px é feito uma vez no render.</summary>
    public float Fraction => TotalWaves <= 0 ? 0f : Math.Clamp((float)Wave / TotalWaves, 0f, 1f);

    public bool SameDisplay(CampaignModel? other)
    {
        if (other is null || StageLabel != other.StageLabel || StageTitle != other.StageTitle || Wave != other.Wave
            || TotalWaves != other.TotalWaves || WaveText != other.WaveText || BossText != other.BossText || Rows.Count != other.Rows.Count) return false;
        for (int i = 0; i < Rows.Count; i++)
            if (!Rows[i].Equals(other.Rows[i])) return false;
        return true;
    }
}

/// <summary>Monta o <see cref="CampaignModel"/> (SDD-24 RF-24.6, SDD-27 RF-27.7/27.9). Leitura pura do estado.</summary>
public static class CampaignModelBuilder
{
    public static CampaignModel Build(SimState state, SimContext ctx)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ctx);
        var stages = ctx.Content.Stages;
        var current = stages[0];
        foreach (var s in stages)
            if (s.Id == state.Stage) { current = s; break; }

        int total = Math.Max(1, current.Waves);
        int wave = Math.Clamp(state.Wave, 1, total);
        int every = Math.Max(1, ctx.Content.Waves.BossEvery);
        int boss = Math.Min(total, (wave + every - 1) / every * every); // próximo chefe >= onda atual (RF-24.6)

        var rows = new List<CampaignRowModel>(stages.Length);
        foreach (var s in stages)
        {
            bool locked = s.Id > state.UnlockedStage;
            rows.Add(new CampaignRowModel(s.Id.ToString("00", System.Globalization.CultureInfo.InvariantCulture), s.Title, locked ? 0 : state.StarsOf(s.Id), locked));
        }

        return new CampaignModel($"FASE ATUAL · {current.Id:00}", current.Title, wave, total,
            $"Onda {wave} de {total}", $"Chefe na onda {boss} · tema {current.StageTopic}", rows);
    }
}
