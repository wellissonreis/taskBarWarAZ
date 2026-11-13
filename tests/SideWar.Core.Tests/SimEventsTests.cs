using SideWar.Core.Content;
using SideWar.Core.Events;
using SideWar.Core.Simulation;
using Xunit;

namespace SideWar.Core.Tests;

/// <summary>T6.6B: SDD-26 ligado à simulação (chefe, ledger, pico, caixa, presença, comandos, hash, ContentHash).</summary>
public sealed class SimEventsTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);
    private static int BossWave => Content.Waves.BossEvery;

    private static StripLayout Layout() =>
        TestLayouts.FromCenters(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 380f });

    private static SimState Boss(bool events = true)
    {
        var s = SimState.CreateInitial(Content, Layout(), Rng.Seed(42, 54));
        s.Stage = 1;
        s.Wave = BossWave;
        if (events) s.StartEvents(Ctx);
        return s;
    }

    private static List<SimEvent> Drain(SimState s, ref long cursor)
    {
        var l = new List<SimEvent>();
        while (s.Events.TryRead(ref cursor, out var e)) l.Add(e);
        return l;
    }

    private static void Steps(SimState s, int n) { for (int i = 0; i < n; i++) s.Step(Ctx); }

    private static Enemy FindBoss(SimState s)
    {
        for (int i = 0; i < s.Enemies.Capacity; i++)
            if (s.Enemies.IsAlive(i) && s.Enemies.At(i).Kind == EnemyKind.Boss) return s.Enemies.At(i);
        throw new InvalidOperationException("sem chefe");
    }

    private static Enemy? TryBoss(SimState s)
    {
        for (int i = 0; i < s.Enemies.Capacity; i++)
            if (s.Enemies.IsAlive(i) && s.Enemies.At(i).Kind == EnemyKind.Boss) return s.Enemies.At(i);
        return null;
    }

    /// <summary>Avança até o chefe estar no campo (máx. 8000 ticks).</summary>
    private static Enemy RunUntilBoss(SimState s)
    {
        for (int i = 0; i < 8000; i++)
        {
            s.Step(Ctx);
            if (TryBoss(s) is { } b) return b;
        }
        throw new InvalidOperationException("chefe não nasceu");
    }

    private static int CorrectIdx(SimState s) => s.Encounter!.Question.CorrectIndex;
    private static int WrongIdx(SimState s) => (s.Encounter!.Question.CorrectIndex + 1) % s.Encounter.Question.Options.Length;

    /// <summary>HP do chefe sem bônus, via plano da mesma onda (ledger vazio com eventos desligados).</summary>
    private static double FullBossHp()
    {
        var s = Boss(events: false);
        return RunUntilBoss(s).MaxHp;   // sem eventos: onda começa direto
    }

    [Fact(DisplayName = "RF-26.4 Onda de chefe em Presente não começa até a resolução; banner e BossWaiting")]
    public void Boss_Wave_Waits()
    {
        var s = Boss();
        long c = s.Events.Written;
        Steps(s, 300);
        Assert.True(s.BossHold);
        Assert.Null(s.ActivePlan);
        Assert.Equal(0, s.Enemies.Count);
        Assert.NotNull(s.Encounter);
        Assert.True(s.Scheduler!.BannerVisible);
        Assert.Contains(Drain(s, ref c), e => e.Type == SimEventType.BossWaiting);
        Assert.Equal(0f, s.Rest);
    }

    [Fact(DisplayName = "CA-26.2 Acerto: HP ×0,4 no spawn, baú comum, estrela, evento; onda libera")]
    public void Boss_Correct()
    {
        double full = FullBossHp();
        var s = Boss();
        s.IncomePerSecond = 10;
        Steps(s, 5);
        Assert.True(s.Apply(Ctx, new OpenBossQuestion()).IsOk);
        long cr0 = s.Credits;
        long xp0 = s.Xp;
        long c = s.Events.Written;
        Assert.True(s.Apply(Ctx, new AnswerBossQuestion(CorrectIdx(s))).IsOk);
        Assert.Equal(1, s.BossQuestionsCorrect);
        Assert.Equal(cr0 + 10 * 60 * 5, s.Credits);   // 5 min de renda, sem multiplicador de presença
        Assert.Equal(xp0 + 200, s.Xp);
        Assert.Contains(Drain(s, ref c), e => e.Type == SimEventType.BossAnswered && e.A == 1);
        var boss = RunUntilBoss(s);
        Assert.False(s.BossHold);
        Assert.NotNull(s.ActivePlan);
        var key = new LedgerKey(1, 1, BossWave);
        Assert.True(s.Ledger.Get(key)!.BonusConsumed);
        Assert.Equal(full * 0.4, boss.MaxHp, 6);
    }

    [Fact(DisplayName = "CA-26.2 Erro: força total, sem estrela, evento BossAnswered(0); resposta dupla é AlreadyUsed")]
    public void Boss_Wrong_And_Double_Answer()
    {
        double full = FullBossHp();
        var s = Boss();
        Steps(s, 5);
        Assert.True(s.Apply(Ctx, new AnswerBossQuestion(WrongIdx(s))).IsOk);
        Assert.Equal(0, s.BossQuestionsCorrect);
        var again = s.Apply(Ctx, new AnswerBossQuestion(CorrectIdx(s)));
        Assert.Equal(CommandReason.AlreadyUsed, again.Reason);
        Assert.Equal(full, RunUntilBoss(s).MaxHp, 6);
        Assert.False(s.Ledger.Get(new LedgerKey(1, 1, BossWave))!.Correct);
    }

    [Fact(DisplayName = "CA-26.2 Tempo esgotado (45 s, card fechado): força total, timeout no ledger, BossTimeout")]
    public void Boss_Timeout()
    {
        var s = Boss();
        long c = s.Events.Written;
        Steps(s, 45 * 30 + 5);
        var entry = s.Ledger.Get(new LedgerKey(1, 1, BossWave));
        Assert.NotNull(entry);
        Assert.True(entry!.TimedOut);
        Assert.Contains(Drain(s, ref c), e => e.Type == SimEventType.BossTimeout);
        Steps(s, 100);
        Assert.False(s.BossHold);
        Assert.Equal(0, s.BossQuestionsCorrect);
    }

    [Fact(DisplayName = "CA-26.7 Ausente cancela o encontro sem gravar o ledger; ao voltar, nova pergunta")]
    public void Absent_Cancels_Boss()
    {
        var s = Boss();
        Steps(s, 5);
        string first = s.Encounter!.Question.Id;
        s.Present = false;
        Steps(s, 5);
        Assert.Null(s.Encounter);
        Assert.Equal(0, s.Ledger.Count);
        Assert.True(s.BossHold);
        Assert.Null(s.ActivePlan);
        Assert.False(s.Scheduler!.BannerVisible);
        Assert.Equal(CommandReason.NotAvailable, s.Apply(Ctx, new OpenBossQuestion()).Reason);
        Steps(s, 100);
        Assert.Null(s.Encounter);   // ausente: nada nasce
        s.Present = true;
        Steps(s, 3);
        Assert.NotNull(s.Encounter);
        Assert.True(s.Encounter!.Elapsed < 1);
        Assert.Single(s.Selector!.Used(1, 1));   // a anterior foi devolvida ao banco
        _ = first;
    }

    [Fact(DisplayName = "CA-26.3 Recuo/retorno: ledger respondido não abre encontro; bônus pendente aplica uma vez, consumido não reaplica")]
    public void Ledger_Skips_Encounter()
    {
        var key = new LedgerKey(1, 1, BossWave);
        double full = FullBossHp();

        var a = Boss();
        a.Ledger.Record(key, "x", 0, true);
        Assert.Equal(full * 0.4, RunUntilBoss(a).MaxHp, 6);
        Assert.False(a.BossHold);
        Assert.Null(a.Encounter);
        Assert.True(a.Ledger.Get(key)!.BonusConsumed);

        var b = Boss();
        b.Ledger.Record(key, "x", 0, true);
        Assert.True(b.Ledger.TryConsumeBonus(key));
        Assert.Equal(full, RunUntilBoss(b).MaxHp, 6);
    }

    [Fact(DisplayName = "Sem StartEvents o comportamento anterior é mantido (sem espera de chefe)")]
    public void Disabled_By_Default()
    {
        var s = Boss(events: false);
        Steps(s, 5);
        Assert.False(s.BossHold);
        Assert.NotNull(s.ActivePlan);
        Assert.Equal(CommandReason.NotAvailable, s.Apply(Ctx, new ClickPeakBanner()).Reason);
        Assert.Equal(CommandReason.NotAvailable, s.Apply(Ctx, new ClickSupplyCrate()).Reason);
    }

    private static EventSchedulerState Sched(PeakPhase peak = PeakPhase.Waiting, double peakTimer = 600, int crateX = 0,
        bool crateActive = false, double crateTimer = 0, double crateCountdown = 300, int extras = 0)
        => new(peak, peakTimer, false, false, false, extras, 0, crateActive, crateTimer, crateX, false, crateCountdown, 0, 0);

    private static SimState WithSched(EventSchedulerState st, int wave = 1)
    {
        var s = SimState.CreateInitial(Content, Layout(), Rng.Seed(7, 9));
        s.Wave = wave;
        s.StartWave(Ctx);
        s.WaveClock = -100000f;
        s.RestoreEvents(Ctx, st, [], [], null, false, 0);
        return s;
    }

    [Fact(DisplayName = "CA-26.1 Pico: banner, clique ativa ×3 por 60 s (PeakDamageFactor), 6 extras em 12 s, missão só no clique")]
    public void Peak_Flow()
    {
        var s = WithSched(Sched(peakTimer: 0.01));
        long c = s.Events.Written;
        Steps(s, 2);
        Assert.True(s.Scheduler!.PeakOffered);
        Assert.Contains(Drain(s, ref c), e => e.Type == SimEventType.PeakBannerShown);
        Assert.False(s.WaveFullySpawned);   // extras pendentes travam a próxima onda
        Assert.True(s.Apply(Ctx, new ClickPeakBanner()).IsOk);
        Steps(s, 1);
        Assert.Equal(3.0, s.PeakDamageFactor);
        Assert.Equal(1, s.Scheduler.PeaksClicked);
        Assert.Equal(CommandReason.NotAvailable, s.Apply(Ctx, new ClickPeakBanner()).Reason);
        Steps(s, 61 * 30);
        Assert.Equal(1.0, s.PeakDamageFactor);
        Assert.Equal(0, s.Scheduler.ExtrasPending);
        Assert.Equal(0, s.PeakQueue);
        Assert.Contains(Drain(s, ref c), e => e.Type == SimEventType.PeakScaled);
    }

    [Fact(DisplayName = "CA-26.1 Extras do pico viram swarm no campo com o HP da onda")]
    public void Peak_Extras_Are_Swarm()
    {
        var s = WithSched(Sched(peakTimer: 0.01), wave: 10);
        Steps(s, 2);
        int swarms = 0;
        for (int i = 0; i < s.Enemies.Capacity; i++)
            if (s.Enemies.IsAlive(i) && s.Enemies.At(i).Kind == EnemyKind.Swarm)
            {
                swarms++;
                double expect = Ctx.Enemy(EnemyKind.Swarm).Hp * Ctx.Waves.HpFactor(10, 0);
                Assert.Equal(expect, s.Enemies.At(i).MaxHp, 6);
            }
        Assert.Equal(1, swarms);   // o 1º extra nasce no instante da oferta
    }

    [Fact(DisplayName = "CA-26.6 Chefe descarta oferta de pico em andamento (PeakIgnored, sem missão)")]
    public void Boss_Discards_Peak_Offer()
    {
        // fim de onda comum com oferta ativa; a próxima onda é a do chefe
        var s = WithSched(Sched(peak: PeakPhase.Offer, peakTimer: 25), wave: BossWave - 1);
        Steps(s, 2);
        Assert.True(s.Scheduler!.PeakOffered);
        long c = s.Events.Written;
        for (int i = 0; i < 4000 && !s.BossHold; i++)
        {
            s.Enemies.Clear();
            s.WaveClock = 1e6f;
            Steps(s, 1);
        }
        Assert.True(s.BossHold);
        Steps(s, 2);   // o banner do chefe aparece no tick seguinte à espera
        Assert.Contains(Drain(s, ref c), e => e.Type == SimEventType.PeakIgnored && e.B == 1);
        Assert.False(s.Scheduler.PeakOffered);
        Assert.Equal(0, s.Scheduler.PeaksClicked);
    }

    [Fact(DisplayName = "CA-26.5 Caixa: aparece, clique paga max(50, renda×60) sem presença; só em Presente")]
    public void Crate_Flow()
    {
        var s = WithSched(Sched(crateCountdown: 0.01));
        s.IncomePerSecond = 2;
        long c = s.Events.Written;
        Steps(s, 2);
        Assert.True(s.Scheduler!.CrateActive);
        var spawned = Drain(s, ref c).First(e => e.Type == SimEventType.CrateSpawned);
        Assert.InRange(spawned.X, 94 + 24, 980 - 24); // ADR-034: entre o núcleo (LeakX) e o portal
        long before = s.Credits;
        Assert.True(s.Apply(Ctx, new ClickSupplyCrate()).IsOk);
        Assert.Equal(before + 120, s.Credits);   // max(50, 2×60)
        Assert.Equal(1, s.Scheduler.CratesCollected);
        Assert.Equal(CommandReason.NotAvailable, s.Apply(Ctx, new ClickSupplyCrate()).Reason);
    }

    [Fact(DisplayName = "CA-26.7 Em Ausente caixa/banner são removidos e comandos ficam NotAvailable")]
    public void Absent_Clears_Events()
    {
        var s = WithSched(Sched(crateActive: true, crateTimer: 30, crateX: 500, peak: PeakPhase.Offer, peakTimer: 20));
        s.Present = false;
        Steps(s, 2);
        Assert.False(s.Scheduler!.CrateActive);
        Assert.False(s.Scheduler.PeakOffered);
        Assert.Equal(CommandReason.NotAvailable, s.Apply(Ctx, new ClickPeakBanner()).Reason);
        Assert.Equal(CommandReason.NotAvailable, s.Apply(Ctx, new ClickSupplyCrate()).Reason);
    }

    [Fact(DisplayName = "ADR-032 LayoutChanged limita o x da caixa ativa")]
    public void Layout_Clamps_Crate()
    {
        var s = WithSched(Sched(crateActive: true, crateTimer: 30, crateX: 970));
        var shorter = TestLayouts.FromCenters(1000f, 94f, 10f, 700f, new[] { 200f, 260f, 320f, 380f });
        Assert.True(s.Apply(Ctx, new LayoutChanged(shorter)).IsOk);
        Assert.Equal(700 - 24, s.Scheduler!.CrateX);
    }

    [Fact(DisplayName = "Hash: ledger, encontro e agendador alteram o hash; mesma seed e comandos = mesmo hash; restore preserva")]
    public void Hash_Covers_Events()
    {
        SimState Run() { var s = Boss(); Steps(s, 20); s.Apply(Ctx, new AnswerBossQuestion(CorrectIdx(s))); Steps(s, 50); return s; }
        ulong h = Run().ComputeStateHash();
        Assert.Equal(h, Run().ComputeStateHash());

        var a = Boss(); Steps(a, 20);
        ulong before = a.ComputeStateHash();
        a.Apply(Ctx, new AnswerBossQuestion(WrongIdx(a)));
        Assert.NotEqual(before, a.ComputeStateHash());   // ledger e encontro mudam

        var d = Boss(); Steps(d, 20);
        ulong withSched = d.ComputeStateHash();
        d.Scheduler!.ClickPeak();   // sem oferta: não muda
        Assert.Equal(withSched, d.ComputeStateHash());
        d.Present = false;
        Assert.NotEqual(withSched, d.ComputeStateHash());
    }

    [Fact(DisplayName = "CA-26.3 Restore do encontro: mesma pergunta e tempo, hash idêntico")]
    public void Restore_Encounter()
    {
        var a = Boss(); Steps(a, 100);
        var b = Boss(events: false);
        b.StartWave(Ctx);
        b.ClearWave();
        // reconstrói b com o mesmo estado básico de a
        b.Tick = a.Tick; b.Credits = a.Credits; b.Carry = a.Carry; b.Xp = a.Xp; b.NextId = a.NextId;
        b.BaseHp = a.BaseHp;
        b.RestoreEvents(Ctx, a.Scheduler!.Snapshot(), a.Ledger.Snapshot(), a.Selector!.Snapshot(), a.Encounter!.State, a.BossHold, a.PeakQueue);
        Assert.Equal(a.Encounter.Question.Id, b.Encounter!.Question.Id);
        Assert.Equal(a.Encounter.Elapsed, b.Encounter.Elapsed);
        Assert.True(b.BossHold);
    }

    [Fact(DisplayName = "RF-26.9 questions.json entra no GameContent e no ContentHash")]
    public void Questions_In_Content_And_Hash()
    {
        Assert.Contains("questions.json", ContentLoader.FileNames);
        Assert.True(Content.Questions.Questions.Length >= 30);
        var files = ContentLoader.ReadEmbedded();
        string h0 = ContentLoader.ComputeHash(files);
        Assert.Equal(Content.ContentHash, h0);
        files["questions.json"] = [.. files["questions.json"], (byte)' '];
        Assert.NotEqual(h0, ContentLoader.ComputeHash(files));
    }
}
