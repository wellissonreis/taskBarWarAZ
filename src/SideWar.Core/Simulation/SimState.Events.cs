using SideWar.Core.Events;

namespace SideWar.Core.Simulation;

/// <summary>
/// Integração de SDD-26 (T6.6B): agendador de pico/caixa, encontro de chefe e ledger ligados ao tick, à agenda de
/// ondas e aos comandos. Opt-in: sem <see cref="StartEvents"/> a simulação se comporta como antes (sem eventos nem
/// espera de chefe). Eventos só rodam em Presente (RF-26.1).
/// </summary>
public sealed partial class SimState
{
    private EventScheduler? _sched;
    private QuestionSelector? _selector;
    private QuestionBank? _bank;
    private SimRngAdapter? _rngAdapter;
    private BossEncounter? _encounter;
    private bool _bossHold;
    private int _peakQueue;
    private double _regionHp = 1.0;

    /// <summary>Ledger de respostas de chefe (RF-26.7); entra no hash. Guardado por (fase, tentativa, onda).</summary>
    public QuestionLedger Ledger { get; } = new();

    /// <summary>Presença: eventos, caixas e chefe só existem com <c>true</c> (RF-26.1). Escrito de fora (SDD-25); no hash.</summary>
    public bool Present { get; set; } = true;

    /// <summary>Tentativa da fase na chave do ledger (RF-26.7); incrementa ao entrar numa nova fase. No hash.</summary>
    public int StageAttempt { get; set; } = 1;

    /// <summary><c>taxaRenda</c> (SDD-27 RF-27.3, sem multiplicador de presença): base da caixa e do baú. Escrito de fora; no hash.</summary>
    public double IncomePerSecond { get; set; }

    public bool EventsEnabled => _sched is not null;
    public EventScheduler? Scheduler => _sched;
    public QuestionSelector? Selector => _selector;
    public BossEncounter? Encounter => _encounter;

    /// <summary>Onda de chefe aguardando a resolução (não começou; RF-26.4).</summary>
    public bool BossHold => _bossHold;

    /// <summary>Extras de pico já liberados pelo agendador e ainda não nascidos (<c>PeakQueue</c>, RF-26.2).</summary>
    public int PeakQueue => _peakQueue;

    public LedgerKey CurrentBossKey => new(Stage, StageAttempt, Wave);

    private SimRngAdapter RngAdapter => _rngAdapter ??= new SimRngAdapter(this);

    /// <summary>Liga SDD-26 (início da presença): sorteia os intervalos de pico e de caixa (RF-26.13, passos 2 e 3).</summary>
    public void StartEvents(SimContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (InTick) throw new InvalidOperationException("StartEvents só entre ticks.");
        _bank = ctx.Content.Questions;
        _selector = new QuestionSelector(_bank);
        _sched = EventScheduler.Start(RngAdapter);
        _encounter = null;
        _bossHold = false;
        _peakQueue = 0;
    }

    // ---- passo 2 do tick ----
    private void EventsSystem(SimContext ctx)
    {
        if (_sched is null) return;
        if (!Present && _encounter is { IsActive: true })
        {
            // RF-26.8: Ausente cancela o encontro sem gravar o ledger; a pergunta volta ao banco.
            _encounter.Cancel();
            _encounter = null;
            _sched.BossResolved(false);
            DrainScheduler();
        }
        if (_encounter is { IsActive: true } enc)
        {
            enc.Advance(SimContext.Dt);
            if (enc.IsResolved) OnBossResolved(ctx, enc.Outcome!);
        }
        _sched.Advance(SimContext.Dt, Present, CrateBounds.From(_layout), RngAdapter);
        DrainScheduler();
        _sched.ApplyTo(this);
        SpawnPeakExtras(ctx);
    }

    private void DrainScheduler()
    {
        foreach (var ev in _sched!.Events)
        {
            switch (ev.Kind)
            {
                case SchedulerEventKind.PeakOffered: Events.Add(new SimEvent(SimEventType.PeakBannerShown, 0f, 0f, 0, 0)); break;
                case SchedulerEventKind.PeakExpired: Events.Add(new SimEvent(SimEventType.PeakIgnored, 0f, 0f, 0, 0)); break;
                case SchedulerEventKind.PeakDiscarded: Events.Add(new SimEvent(SimEventType.PeakIgnored, 0f, 0f, 0, 1)); break;
                case SchedulerEventKind.PeakActivated: Events.Add(new SimEvent(SimEventType.PeakScaled, 0f, 0f, 0, 0)); break;
                case SchedulerEventKind.PeakBonusEnded: Events.Add(new SimEvent(SimEventType.PeakEnded, 0f, 0f, 0, 0)); break;
                case SchedulerEventKind.PeakExtraSpawn: _peakQueue++; break;
                case SchedulerEventKind.CrateSpawned: Events.Add(new SimEvent(SimEventType.CrateSpawned, (float)ev.Value, 0f, 0, 0)); break;
                case SchedulerEventKind.CrateExpired: Events.Add(new SimEvent(SimEventType.CrateExpired, 0f, 0f, 0, 0)); break;
                case SchedulerEventKind.CrateCollected: Events.Add(new SimEvent(SimEventType.CrateCollected, 0f, 0f, ev.Value, 0)); break;
            }
        }
    }

    /// <summary>Extras do pico: swarm com HP/ouro/XP da onda atual (RF-26.2), respeitando o limite do campo.</summary>
    private void SpawnPeakExtras(SimContext ctx)
    {
        int limit = ctx.Rules.MaxEnemiesOnField;
        while (_peakQueue > 0 && Enemies.Count < limit)
        {
            var def = ctx.Enemy(EnemyKind.Swarm);
            int stageIndex = Math.Clamp(Stage - 1, 0, ctx.Content.Stages.Length - 1);
            double hp = def.Hp * ctx.Waves.HpFactor(Wave, stageIndex, _regionHp);
            if (!AddEnemy(def, EnemyKind.Swarm, hp)) break;
            _peakQueue--;
        }
    }

    /// <summary>Efeitos do resultado (RF-26.6): estrelas, baú comum, SimEvent. O HP ×0,4 sai do ledger no spawn.</summary>
    private void OnBossResolved(SimContext ctx, BossOutcome outcome)
    {
        switch (outcome.Result)
        {
            case BossResult.Correct:
                BossQuestionsCorrect++;
                var chest = ctx.Content.Economy.Chests.Common;
                PayCredits(IncomePerSecond * 60.0 * chest.IncomeMinutes);
                PayXp(chest.Xp);
                Events.Add(new SimEvent(SimEventType.BossAnswered, 0f, 0f, 1, outcome.OptionIndex ?? -1));
                break;
            case BossResult.Wrong:
                Events.Add(new SimEvent(SimEventType.BossAnswered, 0f, 0f, 0, outcome.OptionIndex ?? -1));
                break;
            default:
                Events.Add(new SimEvent(SimEventType.BossTimeout, 0f, 0f, 0, 0));
                break;
        }
    }

    /// <summary>
    /// Onda de chefe em espera (RF-26.4). Retorna true quando a onda foi liberada (plano montado). Sem encontro:
    /// em Presente abre um (sorteia a pergunta e mostra o banner, RF-26.13 passo 1); com o ledger já respondido, libera direto.
    /// </summary>
    private bool ReleaseBossHold(SimContext ctx)
    {
        if (_encounter is null)
        {
            if (Present && _sched is not null)
            {
                var stages = ctx.Content.Stages;
                string topic = stages[Math.Clamp(Stage - 1, 0, stages.Length - 1)].StageTopic;
                _encounter = BossEncounter.Begin(_bank!, _selector!, Ledger, CurrentBossKey, topic, RngAdapter);
                if (_encounter is not null)
                {
                    _sched.BossBannerShown(RngAdapter);
                    DrainScheduler();
                    Events.Add(new SimEvent(SimEventType.BossWaiting, _layout.PortalX, 0f, Wave, Stage));
                    return false;
                }
                // ledger já tem resposta: segue direto, bônus derivado do ledger no spawn.
                _bossHold = false;
                BeginWaveStart(ctx);
                return true;
            }
            return false;
        }
        if (!_encounter.IsResolved) return false;
        _sched?.BossResolved(Present);
        if (_sched is not null) DrainScheduler();
        _encounter = null;
        _bossHold = false;
        BeginWaveStart(ctx);
        return true;
    }

    private bool HoldForBoss(SimContext ctx)
    {
        if (_sched is null || !ctx.Waves.IsBossWave(Wave) || (Present && Ledger.Has(CurrentBossKey))) return false;   // Ausente: o chefe nunca começa (SDD-25 RF-25.4)
        ClearWave();
        _bossHold = true;
        return true;
    }

    // ---- comandos (RF-26.15) ----
    private CommandResult ClickPeak()
    {
        if (_sched is null || !Present || !_sched.ClickPeak()) return CommandResult.Reject(CommandReason.NotAvailable);
        DrainScheduler();
        return CommandResult.Ok;
    }

    private CommandResult OpenBoss()
    {
        if (_encounter is not { IsActive: true } enc || !Present) return CommandResult.Reject(CommandReason.NotAvailable);
        enc.OpenCard();
        Events.Add(new SimEvent(SimEventType.BossQuestionShown, 0f, 0f, enc.Remaining, 0));
        return CommandResult.Ok;
    }

    private CommandResult AnswerBoss(SimContext ctx, AnswerBossQuestion cmd)
    {
        if (_encounter is null || !Present) return CommandResult.Reject(CommandReason.NotAvailable);
        if (!_encounter.IsActive || Ledger.Has(_encounter.Key)) return CommandResult.Reject(CommandReason.AlreadyUsed);
        if ((uint)cmd.OptionIndex >= (uint)_encounter.Question.Options.Length) return CommandResult.Reject(CommandReason.UnknownCommand);
        if (_encounter.Answer(cmd.OptionIndex) != LedgerStatus.Ok) return CommandResult.Reject(CommandReason.AlreadyUsed);
        OnBossResolved(ctx, _encounter.Outcome!);
        return CommandResult.Ok;
    }

    private CommandResult ClickCrate(SimContext ctx)
    {
        if (_sched is null || !Present || !_sched.CrateActive) return CommandResult.Reject(CommandReason.NotAvailable);
        double gain = _sched.ClickCrate(IncomePerSecond, RngAdapter) ?? 0.0;
        DrainScheduler();
        PayCredits(gain);   // sem multiplicador de presença (RF-27.13)
        return CommandResult.Ok;
    }

    private void OnLayoutAppliedForEvents()
    {
        if (_sched is null) return;
        _sched.OnLayoutChanged(CrateBounds.From(_layout));
        DrainScheduler();
    }

    // ---- restore (SDD-14) ----
    /// <summary>
    /// Restaura SDD-26 depois de <c>StartWave</c>/<c>RestoreNextSpawn</c>: agendador, ledger, usadas, encontro em espera
    /// (mesma pergunta e tempo; RF-26.5), espera do chefe e <c>PeakQueue</c>.
    /// </summary>
    public void RestoreEvents(SimContext ctx, in EventSchedulerState scheduler,
        IEnumerable<KeyValuePair<LedgerKey, LedgerEntry>> ledger,
        IEnumerable<((int Stage, int Attempt) Key, string[] Used)> used,
        BossEncounterState? encounter, bool bossHold, int peakQueue)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _bank = ctx.Content.Questions;
        _selector = new QuestionSelector(_bank);
        _selector.Restore(used);
        Ledger.Restore(ledger);
        _sched = EventScheduler.Restore(scheduler);
        _encounter = encounter is { } st ? BossEncounter.Restore(_bank, _selector, Ledger, st) : null;
        _bossHold = bossHold;
        _peakQueue = peakQueue;
        _sched.ApplyTo(this);
    }

    partial void HashEventSections(ref StateHasher h)
    {
        h.AddBool(Present);
        h.AddInt32(StageAttempt);
        h.AddDouble(IncomePerSecond);
        h.AddBool(_sched is not null);
        h.AddBool(_bossHold);
        h.AddInt32(_peakQueue);
        IReadOnlyList<KeyValuePair<LedgerKey, LedgerEntry>> ledger = Ledger.Count == 0 ? [] : Ledger.Snapshot();   // vazio: sem alocação
        h.AddInt32(ledger.Count);
        foreach (var (k, v) in ledger)
        {
            h.AddInt32(k.Stage); h.AddInt32(k.Attempt); h.AddInt32(k.Wave);
            h.AddString(v.QuestionId);
            h.AddInt32(v.OptionIndex ?? -1);
            h.AddBool(v.Correct);
            h.AddBool(v.BonusConsumed);
        }
        h.AddInt32(_selector?.SetCount ?? 0);
        if (_selector is { SetCount: > 0 })
        {
            var sets = _selector.Snapshot();
            foreach (var (key, usedIds) in sets)
            {
                h.AddInt32(key.Stage); h.AddInt32(key.Attempt); h.AddInt32(usedIds.Length);
                foreach (var id in usedIds) h.AddString(id);
            }
        }
        if (_sched is not null)
        {
            var s = _sched.Snapshot();
            h.AddByte((byte)s.Peak); h.AddDouble(s.PeakTimer); h.AddBool(s.PeakDue); h.AddBool(s.BossActive);
            h.AddBool(s.NeedsPeakDraw); h.AddInt32(s.ExtrasPending); h.AddDouble(s.NextExtraIn);
            h.AddBool(s.CrateActive); h.AddDouble(s.CrateTimer); h.AddInt32(s.CrateX); h.AddBool(s.NeedsCrateDraw);
            h.AddDouble(s.CrateCountdown); h.AddInt32(s.PeaksClicked); h.AddInt32(s.CratesCollected);
        }
        h.AddBool(_encounter is not null);
        if (_encounter is { } e)
        {
            h.AddInt32(e.Key.Stage); h.AddInt32(e.Key.Attempt); h.AddInt32(e.Key.Wave);
            h.AddString(e.Question.Id);
            h.AddDouble(e.Elapsed);
            h.AddBool(e.CardOpen);
            h.AddInt32(e.Outcome is null ? -1 : (int)e.Outcome.Result);
        }
    }
}
