using SideWar.Core.Simulation;

namespace SideWar.Runtime.Presence;

/// <summary>
/// Cola pura entre o card de retorno e o Core no COLETAR ×2 (SDD-26 RF-26.16; T6.4b). Cada passo vira comando no
/// <c>CommandLog</c> (entre ticks). Não abre janela nem relógio: o host chama <see cref="Begin"/> ao receber
/// <see cref="OfflineCardEventKind.QuestionRequested"/>, mostra <c>QuestionCardModel.FromOfflineSession</c> e encerra com
/// <see cref="Answer"/>, <see cref="Timeout"/> ou <see cref="Close"/>; o <c>Collected</c> devolvido leva o
/// <c>CollectOfflineRewards</c> ao Core.
/// </summary>
public static class OfflineQuestionFlow
{
    /// <summary>
    /// Sorteia (ou recupera, se o relatório já sorteou e a pergunta ficou aberta) a pergunta do relatório e abre a sessão de 45 s.
    /// Null se o Core não pôde sortear (eventos desligados): o host deve chamar <see cref="Close"/> (×1).
    /// </summary>
    public static OfflineQuestionSession? Begin(SimState state, SimContext ctx, long reportId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ctx);
        if (state.OfflineLedger.Has(reportId) is false && !state.Apply(ctx, new DrawOfflineQuestion(reportId)).IsOk) return null;
        if (state.OfflineLedger.Get(reportId) is { Answered: true }) return null;
        return state.OfflineQuestion(reportId) is { } q ? new OfflineQuestionSession(reportId, q) : null;
    }

    /// <summary>Resposta do jogador: registra no ledger e informa o controlador (acerto ×2, erro ×1).</summary>
    public static OfflineCardEvent Answer(SimState state, SimContext ctx, OfflineCardController card, OfflineQuestionSession session, int optionIndex)
    {
        ArgumentNullException.ThrowIfNull(session);
        bool correct = session.Answer(optionIndex);
        state.Apply(ctx, new AnswerOfflineQuestion(session.ReportId, optionIndex));
        return card.AnswerQuestion(correct);
    }

    /// <summary>45 s de SimTime esgotados: ledger sem alternativa, ×1.</summary>
    public static OfflineCardEvent Timeout(SimState state, SimContext ctx, OfflineCardController card, OfflineQuestionSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        state.Apply(ctx, new AnswerOfflineQuestion(session.ReportId, -1));
        return card.QuestionTimedOut();
    }

    /// <summary>Card da pergunta fechado sem resposta: ledger sem alternativa, ×1.</summary>
    public static OfflineCardEvent Close(SimState state, SimContext ctx, OfflineCardController card, long reportId)
    {
        state.Apply(ctx, new AnswerOfflineQuestion(reportId, -1));
        return card.QuestionClosed();
    }
}
