namespace SideWar.Runtime.Presence;

/// <summary>
/// Ganhos pendentes do card "Bem-vindo de volta" (SDD-25 RF-25.9/RF-25.11). <see cref="ReportId"/> é a chave do ledger
/// de COLETAR ×2 (SDD-26 RF-26.16): um relatório responde no máximo uma vez. <see cref="Credits"/> é o pendente líquido
/// do excedente da auto-escala; <see cref="Xp"/> só entra no nível ao coletar.
/// </summary>
public sealed record PendingRewards(long ReportId, OfflineReport Report, long Credits, long Xp)
{
    /// <summary>Cria o pendente de um resultado offline (<c>ShowCard</c> deve ser true; senão os ganhos vão direto ao saldo).</summary>
    public static PendingRewards From(long reportId, OfflineResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new(reportId, result.Report, result.PendingCredits, result.PendingXp);
    }

    /// <summary>
    /// Novo offline ≥ 10 min com pendência não coletada: os valores <b>somam</b> num único relatório (RF-25.9). O tempo é
    /// limitado a 12 h (<paramref name="maxSeconds"/>); a faixa de ondas vai da primeira <c>OndaDe</c> à última <c>OndaPara</c>;
    /// o nível vai do primeiro <c>NivelDe</c> ao último <c>NivelPara</c>; melhorias concatenam; perdas somam.
    /// O relatório unido recebe <paramref name="newReportId"/> (ledger novo; nada foi respondido nele).
    /// </summary>
    public PendingRewards Merge(PendingRewards later, long newReportId, double maxSeconds = 12 * 3600)
    {
        ArgumentNullException.ThrowIfNull(later);
        var a = Report;
        var b = later.Report;
        var upgrades = new List<AutoUpgrade>(a.MelhoriasAuto.Count + b.MelhoriasAuto.Count);
        upgrades.AddRange(a.MelhoriasAuto);
        upgrades.AddRange(b.MelhoriasAuto);
        var report = new OfflineReport(
            Math.Min(maxSeconds, a.TempoAcumuladoS + b.TempoAcumuladoS),
            SatAdd(a.Creditos, b.Creditos), a.OndaDe, b.OndaPara, a.Quedas + b.Quedas,
            SatAdd(a.Xp, b.Xp), a.NivelDe, b.NivelPara, upgrades, a.NMelhorias + b.NMelhorias,
            SatAdd(a.GastoAuto, b.GastoAuto), a.PerdaPicos + b.PerdaPicos, a.PerdaCaixas + b.PerdaCaixas,
            a.PerdaChefes + b.PerdaChefes, b.ParouNoChefe);
        return new(newReportId, report, SatAdd(Credits, later.Credits), SatAdd(Xp, later.Xp));
    }

    private static long SatAdd(long x, long y)
    {
        long r;
        try { r = checked(x + y); }
        catch (OverflowException) { r = (x > 0) ? long.MaxValue : long.MinValue; }
        return r;
    }
}
