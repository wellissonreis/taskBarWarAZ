using SideWar.Core.Simulation;
using SideWar.Persistence.Session;
using SideWar.Runtime.Presence;

namespace SideWar.Persistence.Mapping;

/// <summary>
/// <c>PendingRewards</c> ⇄ <see cref="PendingData"/> + <see cref="CardStateData"/> (SDD-14 RF-14.5, SDD-25 RF-25.11; T6.4b).
/// Puro. A pergunta do COLETAR ×2 aberta no save volta como card <c>Aberto</c> (a pergunta é descartada; o relatório já
/// consta no ledger e vale ×1 ao reabrir).
/// </summary>
public static class PendingMapper
{
    public static PendingData ToData(PendingRewards p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var r = p.Report;
        return new PendingData
        {
            ReportId = p.ReportId, Credits = p.Credits, Xp = p.Xp, Quedas = r.Quedas, OndaDe = r.OndaDe, OndaPara = r.OndaPara,
            TempoS = r.TempoAcumuladoS,
            Melhorias = [.. r.MelhoriasAuto.Select(m => new UpgradeLineData(m.Tipo.ToString(), m.DeNivel, m.ParaNivel))],
            Perdas = new LossesData { Picos = r.PerdaPicos, Caixas = r.PerdaCaixas, Chefes = r.PerdaChefes },
            RawCredits = r.Creditos, LevelFrom = r.NivelDe, LevelTo = r.NivelPara, NMelhorias = r.NMelhorias,
            GastoAuto = r.GastoAuto, ParouNoChefe = r.ParouNoChefe,
        };
    }

    public static PendingRewards FromData(PendingData d)
    {
        ArgumentNullException.ThrowIfNull(d);
        var upgrades = d.Melhorias.Select(m => new AutoUpgrade(
            Enum.TryParse<TowerType>(m.Tower, out var t) && Enum.IsDefined(t) ? t : throw new FormatException($"torre inválida: '{m.Tower}'"),
            m.From, m.To)).ToList();
        var report = new OfflineReport(d.TempoS, d.RawCredits, d.OndaDe, d.OndaPara, d.Quedas, d.Xp, d.LevelFrom, d.LevelTo,
            upgrades, d.NMelhorias, d.GastoAuto, d.Perdas.Picos, d.Perdas.Caixas, d.Perdas.Chefes, d.ParouNoChefe);
        return new PendingRewards(d.ReportId, report, d.Credits, d.Xp);
    }

    /// <summary>Estado a gravar: sem pendência = <c>(vazio, Aberto)</c>; recolhido = <c>Recolhido</c>; o resto = <c>Aberto</c>.</summary>
    public static (PendingData Pending, CardStateData CardState) ToSession(OfflineCardController c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return c.Pending is { } p
            ? (ToData(p), c.State == OfflineCardState.Collapsed ? CardStateData.Recolhido : CardStateData.Aberto)
            : (new PendingData(), CardStateData.Aberto);
    }

    /// <summary>
    /// Restaura o controlador a partir do snapshot e do estado do Core: o relatório que já sorteou pergunta não pergunta
    /// de novo (ledger) e os próximos ids nunca reutilizam um id do ledger. Sem pendência (<c>ReportId == 0</c>) só semeia os ids.
    /// </summary>
    public static void Restore(OfflineCardController c, Session.OfflineSnapshot snap, SimState state)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(snap);
        ArgumentNullException.ThrowIfNull(state);
        c.SeedReportIds(state.OfflineLedger.MaxReportId);
        if (snap.Pending.ReportId == 0) return;
        var pending = FromData(snap.Pending);
        c.Restore(pending, snap.CardState == CardStateData.Recolhido, state.OfflineLedger.Has(pending.ReportId));
    }
}
