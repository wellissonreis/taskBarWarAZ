using SideWar.Core.Events;
using SideWar.Core.Simulation;
using SideWar.Runtime.Panel;

namespace SideWar.Runtime.LiveEvents;

/// <summary>
/// Fluxo do card de pergunta do chefe (SDD-26 RF-26.4 a RF-26.10; SDD-24 RF-24.9, DP-24.10). O sorteio, o tempo (45 s de
/// SimTime) e a resposta são do Core (<see cref="OpenBossQuestion"/>/<see cref="AnswerBossQuestion"/>); aqui só a visibilidade
/// do card (fechar/minimizar não pausa nem cancela, RF-26.10) e o resultado exibido por 20 s depois da resolução.
/// Chamado entre ticks da T-Game; sem timer: o host agenda um acordar em <see cref="CloseDeadlineTicks"/>.
/// </summary>
public sealed class BossQuestionFlow(IClock clock, double resultSeconds = 20.0)
{
    private QuestionCardModel? _last;
    private LedgerKey _key;
    private int _wave;
    private string _topic = "";
    private QuestionDef? _question;
    private bool _minimized;
    private long _closeAt;

    /// <summary>Modelo do card a mostrar, ou null (sem encontro, card minimizado/fechado ou resultado já recolhido).</summary>
    public QuestionCardModel? Card { get; private set; }

    /// <summary>Instante (ticks) em que o resultado é recolhido; 0 se não há resultado exibido.</summary>
    public long CloseDeadlineTicks => _closeAt;

    /// <summary>Card aberto pelo jogador (clique no banner): <c>OpenBossQuestion</c> no Core e desminimiza.</summary>
    public CommandResult Open(SimState state, SimContext ctx)
    {
        ArgumentNullException.ThrowIfNull(state);
        var r = state.Apply(ctx, new OpenBossQuestion());
        if (r.IsOk) { _minimized = false; Refresh(state, ctx); }
        return r;
    }

    /// <summary>Resposta A–D. Rejeitada pelo Core (ex.: <c>AlreadyUsed</c>) deixa tudo como estava.</summary>
    public CommandResult Answer(SimState state, SimContext ctx, int optionIndex)
    {
        ArgumentNullException.ThrowIfNull(state);
        var r = state.Apply(ctx, new AnswerBossQuestion(optionIndex));
        Refresh(state, ctx);
        return r;
    }

    /// <summary>Fechar ou minimizar: só esconde; o tempo do Core continua (RF-26.10).</summary>
    public void Close()
    {
        _minimized = true;
        Card = null;
        _closeAt = 0;
    }

    /// <summary>Atualiza o modelo a partir do Core (por mudança de dados, ~1 Hz pelo timer) e recolhe o resultado vencido.</summary>
    public bool Refresh(SimState state, SimContext ctx)
    {
        ArgumentNullException.ThrowIfNull(state);
        var before = Card;
        var enc = state.Encounter;
        if (enc is { IsActive: true })
        {
            var stages = ctx.Content.Stages;
            _topic = stages[Math.Clamp(state.Stage - 1, 0, stages.Length - 1)].StageTopic;
            _wave = state.Wave;
            _key = enc.Key;
            _question = enc.Question;
            _last = QuestionCardModel.From(enc, _wave, _topic);
            _closeAt = 0;
            Card = state.Encounter!.CardOpen && !_minimized ? _last : null;
        }
        else if (enc is { Outcome: not null })
        {
            Resolved(QuestionCardModel.From(enc, state.Wave, _topic));
        }
        else if (_last is { State: QuestionCardState.Waiting } && _question is not null && state.Ledger.Get(_key) is { } entry)
        {
            // o Core liberou a onda (encontro descartado) antes do próximo Refresh: reconstrói o resultado do ledger.
            Resolved(FromLedger(entry));
        }
        else if (_last is { State: QuestionCardState.Waiting })
        {
            _last = null;   // cancelado (Ausente, RF-26.8): sem ledger, sem card
            Card = null;
            _minimized = false;
        }
        if (_closeAt != 0 && clock.NowTicks >= _closeAt) { Close(); _last = null; _minimized = false; }
        return !Equals(before, Card);
    }

    private void Resolved(QuestionCardModel model)
    {
        if (_last is { State: not QuestionCardState.Waiting }) { Card = _minimized ? null : _last; return; }
        _last = model;
        _closeAt = clock.NowTicks + (long)(resultSeconds * clock.TicksPerSecond);
        Card = _minimized ? null : model;
    }

    private QuestionCardModel FromLedger(LedgerEntry e)
    {
        var q = _question!;
        int correct = q.CorrectIndex;
        var visuals = new OptionVisual[q.Options.Length];
        if (correct >= 0) visuals[correct] = OptionVisual.Correct;
        var state = e.TimedOut ? QuestionCardState.TimedOut : e.Correct ? QuestionCardState.Correct : QuestionCardState.Wrong;
        if (state == QuestionCardState.Wrong && e.OptionIndex is { } chosen) visuals[chosen] = OptionVisual.Wrong;
        string correctExp = correct >= 0 ? q.Options[correct].Explanation : "";
        string? chosenExp = state == QuestionCardState.Wrong && e.OptionIndex is { } c ? q.Options[c].Explanation : null;
        string explanation = !string.IsNullOrWhiteSpace(chosenExp) ? chosenExp + " " + correctExp : correctExp;
        return _last! with
        {
            TimerText = QuestionCardModel.FormatTimer(0), Visuals = visuals, State = state, Explanation = explanation,
        };
    }
}
