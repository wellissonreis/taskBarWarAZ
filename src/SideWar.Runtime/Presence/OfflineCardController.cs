using SideWar.Core;
using SideWar.Core.Simulation;

namespace SideWar.Runtime.Presence;

/// <summary>Estado do card "Bem-vindo de volta" (SDD-24 RF-24.10, SDD-25 RF-25.9).</summary>
public enum OfflineCardState
{
    /// <summary>Sem pendência.</summary>
    None,
    /// <summary>Card visível, contando 60 s sem clique.</summary>
    Open,
    /// <summary>Pergunta Azure do COLETAR ×2 aberta; o contador de 60 s está pausado.</summary>
    AskingQuestion,
    /// <summary>Recolhido: só o badge "+X" no widget (clicar reabre).</summary>
    Collapsed,
}

public enum OfflineCardEventKind
{
    /// <summary>Entrada ignorada (estado inválido); nada mudou.</summary>
    Ignored,
    /// <summary>COLETAR ×2 pressionado: o host abre a pergunta Azure da fase (<see cref="OfflineCardEvent.ReportId"/>).</summary>
    QuestionRequested,
    /// <summary>Ganhos coletados: <see cref="OfflineCardEvent.Command"/> deve ir ao Core entre ticks.</summary>
    Collected,
    /// <summary><c>OfflineCardCollapsed</c> (SDD-24 RF-24.10): fechar a janela do card e mostrar o badge.</summary>
    Collapsed,
    /// <summary><c>OfflineCardReopened</c>: reabrir o card sem ativar a janela.</summary>
    Reopened,
    /// <summary>Card criado/atualizado por um novo offline (RF-25.9: soma com pendência).</summary>
    Presented,
}

/// <summary>
/// Efeito de uma entrada do controlador. <see cref="Interaction"/> = conta como interação de presença (RF-25.1: abrir,
/// recolher e clicar no card). <see cref="Multiplier"/> é 1 ou 2 só em <see cref="OfflineCardEventKind.Collected"/>.
/// </summary>
public readonly record struct OfflineCardEvent(
    OfflineCardEventKind Kind, long ReportId = 0, int Multiplier = 0, CollectOfflineRewards? Command = null,
    bool Interaction = false);

/// <summary>
/// Controlador puro do card "Bem-vindo de volta" (SDD-24 RF-24.10, SDD-25 RF-25.9, D-11). Sem janela, sem timer próprio:
/// o host agenda um acordar em <see cref="DeadlineTicks"/> e chama <see cref="Update"/>. Relógio injetável.
/// <list type="bullet">
/// <item>COLETAR +X: coleta ×1 na hora. COLETAR ×2: pede a pergunta; acerto = ×2; erro, tempo esgotado ou card da pergunta
/// fechado sem resposta = ×1. Um relatório pergunta no máximo uma vez (ledger por <c>ReportId</c>).</item>
/// <item>Sem pressionamento de botão do mouse por 60 s: recolhe para o badge "+X". Hover/movimento não contam; cada clique
/// reinicia o contador; ele fica pausado durante a pergunta.</item>
/// <item>O pendente só some ao coletar; coletar emite o comando do Core com valores já multiplicados (saturando).</item>
/// </list>
/// </summary>
public sealed class OfflineCardController(IClock clock, double collapseSeconds = 60.0)
{
    private readonly HashSet<long> _asked = [];
    private long _nextId = 1;

    public OfflineCardState State { get; private set; }
    public PendingRewards? Pending { get; private set; }

    /// <summary>Sobe a cada transição visível (render sob demanda).</summary>
    public long Version { get; private set; }

    /// <summary>Instante (ticks) em que o card recolhe; 0 se não há contagem (sem card, recolhido ou em pergunta).</summary>
    public long DeadlineTicks { get; private set; }

    /// <summary>Texto do badge "+X" (créditos pendentes formatados, SDD-27 RF-27.2) enquanto recolhido; senão null.</summary>
    public string? BadgeText => State == OfflineCardState.Collapsed && Pending is { } p
        ? "+" + Core.Economy.CreditFormatter.Format(p.Credits) : null;

    /// <summary>Créditos do badge para <c>WidgetInputs.Pending</c> (SDD-23): o pendente enquanto recolhido; senão 0 (sem badge).</summary>
    public double BadgeCredits => State == OfflineCardState.Collapsed && Pending is { } p ? p.Credits : 0;

    /// <summary>Existe pendência a persistir (<c>pending</c> e <c>cardState</c>, RF-25.11).</summary>
    public bool HasPending => Pending is not null;

    private long CollapseTicks => (long)(collapseSeconds * clock.TicksPerSecond);

    /// <summary>Próximo id de relatório (o host persiste junto de <c>pending</c>; avança a cada uso).</summary>
    public long TakeReportId() => _nextId++;

    /// <summary>
    /// Novo offline ≥ 10 min (<c>ShowCard</c>). Sem pendência cria o card; com pendência soma num único relatório (RF-25.9).
    /// Uma pergunta aberta é descartada (o app esteve oculto; o relatório novo é outro ledger). Sempre termina Aberto.
    /// </summary>
    public OfflineCardEvent Present(OfflineResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var id = TakeReportId();
        var incoming = PendingRewards.From(id, result);
        Pending = Pending is { } old ? old.Merge(incoming, id) : incoming;
        return Open(OfflineCardEventKind.Presented);
    }

    /// <summary>Garante que os próximos ids de relatório ficam acima de <paramref name="lastUsedId"/> (ledger persistido).</summary>
    public void SeedReportIds(long lastUsedId)
    {
        if (lastUsedId >= _nextId) _nextId = lastUsedId + 1;
    }

    /// <summary>
    /// Restore (SDD-14): pendência persistida. <paramref name="collapsed"/> = <c>cardState == Recolhido</c>.
    /// <paramref name="alreadyAsked"/> = o ledger do Core já tem pergunta para este relatório (COLETAR ×2 vale ×1; RF-26.16).
    /// </summary>
    public void Restore(PendingRewards pending, bool collapsed, bool alreadyAsked = false)
    {
        ArgumentNullException.ThrowIfNull(pending);
        Pending = pending;
        if (alreadyAsked) _asked.Add(pending.ReportId);
        if (pending.ReportId >= _nextId) _nextId = pending.ReportId + 1;
        if (collapsed) { State = OfflineCardState.Collapsed; DeadlineTicks = 0; Version++; }
        else Open(OfflineCardEventKind.Presented);
    }

    /// <summary>Mouse up em COLETAR +X: coleta ×1 (RF-25.9). Também aceito recolhido? Não: o botão só existe no card aberto.</summary>
    public OfflineCardEvent PressCollect()
        => State == OfflineCardState.Open ? Collect(1) : Ignore();

    /// <summary>Mouse up em COLETAR ×2: pede a pergunta (uma vez por relatório). O contador de 60 s pausa.</summary>
    public OfflineCardEvent PressCollectX2()
    {
        if (State != OfflineCardState.Open || Pending is not { } p) return Ignore();
        if (!_asked.Add(p.ReportId)) return Collect(1);   // já perguntou neste relatório: não repete (ledger)
        State = OfflineCardState.AskingQuestion;
        DeadlineTicks = 0;
        Version++;
        return new(OfflineCardEventKind.QuestionRequested, p.ReportId, 0, null, true);
    }

    /// <summary>Resposta da pergunta Azure: acerto ×2, erro ×1.</summary>
    public OfflineCardEvent AnswerQuestion(bool correct)
        => State == OfflineCardState.AskingQuestion ? Collect(correct ? 2 : 1) : Ignore();

    /// <summary>45 s esgotados (SimTime do host): ×1.</summary>
    public OfflineCardEvent QuestionTimedOut()
        => State == OfflineCardState.AskingQuestion ? Collect(1) : Ignore();

    /// <summary>Card da pergunta fechado sem resposta: ×1 (RF-25.9).</summary>
    public OfflineCardEvent QuestionClosed()
        => State == OfflineCardState.AskingQuestion ? Collect(1) : Ignore();

    /// <summary>
    /// Mouse down no card fora dos botões (fundo) ou em qualquer botão ainda não resolvido: reinicia o contador de 60 s.
    /// Os métodos <c>Press*</c> já encerram o contador; use este só para cliques no fundo. Hover não chama.
    /// </summary>
    public bool NotifyMousePress()
    {
        if (State != OfflineCardState.Open) return false;
        DeadlineTicks = clock.NowTicks + CollapseTicks;
        return true;
    }

    /// <summary>Chamar no deadline: recolhe se passou o prazo sem ação e o card não está em pergunta.</summary>
    public OfflineCardEvent Update()
    {
        if (State != OfflineCardState.Open || clock.NowTicks < DeadlineTicks) return Ignore();
        State = OfflineCardState.Collapsed;
        DeadlineTicks = 0;
        Version++;
        return new(OfflineCardEventKind.Collapsed, Pending?.ReportId ?? 0, 0, null, true);
    }

    /// <summary>Clique no badge "+X" do widget: reabre o card e reinicia o contador (conta como interação).</summary>
    public OfflineCardEvent ReopenFromBadge()
        => State == OfflineCardState.Collapsed ? Open(OfflineCardEventKind.Reopened) : Ignore();

    private OfflineCardEvent Open(OfflineCardEventKind kind)
    {
        State = OfflineCardState.Open;
        DeadlineTicks = clock.NowTicks + CollapseTicks;
        Version++;
        return new(kind, Pending?.ReportId ?? 0, 0, null, true);
    }

    private OfflineCardEvent Collect(int multiplier)
    {
        var p = Pending!;
        var cmd = new CollectOfflineRewards(Scale(p.Credits, multiplier), Scale(p.Xp, multiplier));
        Pending = null;
        State = OfflineCardState.None;
        DeadlineTicks = 0;
        Version++;
        return new(OfflineCardEventKind.Collected, p.ReportId, multiplier, cmd, true);
    }

    private static long Scale(long v, int m) => v > long.MaxValue / m ? long.MaxValue : v * m;

    private static OfflineCardEvent Ignore() => new(OfflineCardEventKind.Ignored);
}
