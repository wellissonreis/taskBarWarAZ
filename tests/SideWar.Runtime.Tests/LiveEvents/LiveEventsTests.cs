using SideWar.Core;
using SideWar.Core.Content;
using SideWar.Core.Events;
using SideWar.Core.Simulation;
using SideWar.Runtime.LiveEvents;
using SideWar.Runtime.Panel;
using Xunit;

namespace SideWar.Runtime.Tests.LiveEvents;

/// <summary>T6.11: banners pico/chefe, caixa e card de pergunta do chefe ligados ao Core (CA-26.x, CA-24.x).</summary>
public sealed class LiveEventsTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);
    private static int BossWave => Content.Waves.BossEvery;

    private static SimState Fresh(int wave = 1, ulong seed = 42)
    {
        var s = SimState.CreateInitial(Content, TestLayouts.Default(), Rng.Seed(seed, 54));
        s.Stage = 1;
        s.Wave = wave;
        s.StartEvents(Ctx);
        return s;
    }

    private static void Steps(SimState s, int n) { for (int i = 0; i < n; i++) s.Step(Ctx); }

    private static bool RunUntil(SimState s, Func<bool> cond, int max = 40_000)
    {
        for (int i = 0; i < max; i++)
        {
            if (cond()) return true;
            s.Step(Ctx);
        }
        return cond();
    }

    [Fact(DisplayName = "Sem eventos ou em Ausente: visão vazia")]
    public void Empty_When_Disabled_Or_Absent()
    {
        var off = SimState.CreateInitial(Content, TestLayouts.Default(), Rng.Seed(1, 54));
        Assert.Equal(LiveEventsView.Empty, LiveEventsViewBuilder.Build(off));
        var s = Fresh(BossWave);
        Steps(s, 5);
        Assert.NotNull(LiveEventsViewBuilder.Build(s).Banner);
        s.Present = false;
        Steps(s, 2);
        Assert.Equal(LiveEventsView.Empty, LiveEventsViewBuilder.Build(s));
    }

    [Fact(DisplayName = "CA-26.1 banner de pico: oferta clicável 0:30, clique escala (não clicável, 60 s) e vira ClickPeakBanner")]
    public void Peak_Banner_Flow()
    {
        var s = Fresh();
        Assert.True(RunUntil(s, () => s.Scheduler!.PeakOffered));
        var v = LiveEventsViewBuilder.Build(s);
        Assert.Equal(LiveBannerKind.Peak, v.Banner!.Kind);
        Assert.True(v.Banner.Clickable);
        Assert.InRange(v.Banner.ChipSeconds, 29, 30);
        var cmd = LiveEventsViewBuilder.CommandFor(v, LiveClickTarget.Banner);
        Assert.IsType<ClickPeakBanner>(cmd);
        Assert.True(s.Apply(Ctx, cmd).IsOk);
        var scaled = LiveEventsViewBuilder.Build(s);
        Assert.Equal(LiveBannerKind.PeakScaled, scaled.Banner!.Kind);
        Assert.False(scaled.Banner.Clickable);
        Assert.InRange(scaled.Banner.ChipSeconds, 59, 60);
        Assert.Null(LiveEventsViewBuilder.CommandFor(scaled, LiveClickTarget.Banner));
        Assert.Null(scaled.WidgetChip);
    }

    [Fact(DisplayName = "CA-26.5 caixa: x do estado, 45 s, clique vira ClickSupplyCrate e some")]
    public void Crate_Flow()
    {
        var s = Fresh();
        s.IncomePerSecond = 10;
        Assert.True(RunUntil(s, () => s.Scheduler!.CrateActive));
        var v = LiveEventsViewBuilder.Build(s);
        Assert.NotNull(v.Crate);
        Assert.Equal(s.Scheduler!.CrateX, v.Crate!.X);
        Assert.InRange(v.Crate.RemainingSeconds, 44, 45);
        long before = s.Credits;
        Assert.True(s.Apply(Ctx, LiveEventsViewBuilder.CommandFor(v, LiveClickTarget.Crate)).IsOk);
        Assert.Equal(before + 600, s.Credits);
        Assert.Null(LiveEventsViewBuilder.Build(s).Crate);
        Assert.Null(LiveEventsViewBuilder.CommandFor(LiveEventsView.Empty, LiveClickTarget.Crate));
    }

    [Fact(DisplayName = "RF-26.4/26.12 banner do chefe tem prioridade, 0:45 clicável; bônus ativo vira chip ×3 no widget")]
    public void Boss_Banner_Priority_And_Chip()
    {
        var s = Fresh(BossWave);
        Steps(s, 5);
        var v = LiveEventsViewBuilder.Build(s);
        Assert.Equal(LiveBannerKind.Boss, v.Banner!.Kind);
        Assert.True(v.Banner.Clickable);
        Assert.InRange(v.Banner.ChipSeconds, 44, 45);
        Assert.Null(v.WidgetChip);
        Assert.IsType<OpenBossQuestion>(LiveEventsViewBuilder.CommandFor(v, LiveClickTarget.Banner));

        // bônus de pico ativo (59 s) durante o chefe: restaura o agendador (mesmo caminho do save) e confere o chip.
        var st = s.Scheduler!.Snapshot() with { Peak = PeakPhase.Bonus, PeakTimer = 59 };
        s.RestoreEvents(Ctx, st, s.Ledger.Snapshot(), [], s.Encounter!.State, true, 0);
        var chip = LiveEventsViewBuilder.Build(s);
        Assert.Equal(LiveBannerKind.Boss, chip.Banner!.Kind);
        Assert.Equal("×3 0:59", chip.WidgetChip!.Text);
        Assert.Equal(59, chip.WidgetChip.Seconds);
    }

    // ---- card de pergunta do chefe ----

    private static SimState AtBoss()
    {
        var s = Fresh(BossWave);
        Steps(s, 5);
        Assert.NotNull(s.Encounter);
        return s;
    }

    [Fact(DisplayName = "CA-26.2 card: abrir, modelo vem do Core, fechar não pausa nem cancela, resposta certa mostra resultado e recolhe em 20 s")]
    public void Boss_Card_Correct_AutoClose()
    {
        var s = AtBoss();
        var clock = new FakeClock();
        var flow = new BossQuestionFlow(clock);
        Assert.Null(flow.Card);
        Assert.True(flow.Open(s, Ctx).IsOk);
        Assert.NotNull(flow.Card);
        Assert.Matches("^0:4[45]$", flow.Card!.TimerText);
        Assert.Contains("CHEFE · ONDA", flow.Card.Header);
        Assert.True(flow.Card.OptionsEnabled);

        double rem = s.Encounter!.Remaining;
        flow.Close();
        Assert.Null(flow.Card);
        Steps(s, 30);
        Assert.True(s.Encounter!.Remaining < rem);   // o tempo corre com o card fechado
        Assert.True(flow.Open(s, Ctx).IsOk);

        Assert.True(flow.Answer(s, Ctx, s.Encounter!.Question.CorrectIndex).IsOk);
        Assert.Equal(QuestionCardState.Correct, flow.Card!.State);
        Assert.Equal(1, s.BossQuestionsCorrect);
        Steps(s, 3);   // o Core libera a onda e descarta o encontro
        flow.Refresh(s, Ctx);
        Assert.Equal(QuestionCardState.Correct, flow.Card!.State);   // resultado persiste
        Assert.NotEqual(0, flow.CloseDeadlineTicks);
        clock.AdvanceTicks(21 * 30_000_000L);
        flow.Refresh(s, Ctx);
        Assert.Null(flow.Card);
        Assert.Equal(0, flow.CloseDeadlineTicks);
    }

    [Fact(DisplayName = "CA-26.2 erro: destaca a certa e a errada; segunda resposta é AlreadyUsed")]
    public void Boss_Card_Wrong()
    {
        var s = AtBoss();
        var flow = new BossQuestionFlow(new FakeClock());
        flow.Open(s, Ctx);
        var q = s.Encounter!.Question;
        int wrong = (q.CorrectIndex + 1) % q.Options.Length;
        Assert.True(flow.Answer(s, Ctx, wrong).IsOk);
        Assert.Equal(QuestionCardState.Wrong, flow.Card!.State);
        Assert.Equal(OptionVisual.Wrong, flow.Card.Visuals[wrong]);
        Assert.Equal(OptionVisual.Correct, flow.Card.Visuals[q.CorrectIndex]);
        Assert.False(flow.Answer(s, Ctx, q.CorrectIndex).IsOk);
        Assert.Equal(0, s.BossQuestionsCorrect);
    }

    [Fact(DisplayName = "CA-26.2 tempo: 45 s de SimTime sem tocar no card resolvem por timeout; resultado reconstruído do ledger")]
    public void Boss_Card_Timeout()
    {
        var s = AtBoss();
        var flow = new BossQuestionFlow(new FakeClock());
        flow.Open(s, Ctx);
        flow.Refresh(s, Ctx);
        Assert.Equal(QuestionCardState.Waiting, flow.Card!.State);
        for (int i = 0; i < 45 * 30 + 5; i++)
        {
            s.Step(Ctx);
            if (i % 30 == 0) flow.Refresh(s, Ctx);
        }
        flow.Refresh(s, Ctx);
        Assert.NotNull(flow.Card);
        Assert.Equal(QuestionCardState.TimedOut, flow.Card!.State);
        Assert.Equal("TEMPO ESGOTADO", flow.Card.ResultText);
        Assert.Contains(Content.Questions.Questions.First(x => flow.Card.Problem == x.Problem).Options.First(o => o.Correct).Explanation,
            flow.Card.Explanation);
        Assert.Equal(0, s.BossQuestionsCorrect);
    }

    [Fact(DisplayName = "RF-26.8 Ausente cancela o encontro: card some, ledger não gravado; ao voltar, nova pergunta")]
    public void Boss_Card_Cancelled_When_Absent()
    {
        var s = AtBoss();
        var flow = new BossQuestionFlow(new FakeClock());
        flow.Open(s, Ctx);
        s.Present = false;
        Steps(s, 2);
        flow.Refresh(s, Ctx);
        Assert.Null(flow.Card);
        Assert.Equal(0, s.Ledger.Count);
        s.Present = true;
        Steps(s, 10);
        Assert.NotNull(s.Encounter);
        Assert.True(flow.Open(s, Ctx).IsOk);
        Assert.Equal(QuestionCardState.Waiting, flow.Card!.State);
    }

    [Fact(DisplayName = "CA-26.9/T-26.9 determinismo 5x: mesmo roteiro de eventos e cliques, mesmo StateHash")]
    public void Deterministic_FiveRuns()
    {
        ulong First = 0;
        for (int run = 0; run < 5; run++)
        {
            var s = Fresh(BossWave, 7);
            var flow = new BossQuestionFlow(new FakeClock());
            Steps(s, 10);
            flow.Open(s, Ctx);
            flow.Answer(s, Ctx, s.Encounter!.Question.CorrectIndex);
            RunUntil(s, () => s.Scheduler!.CrateActive || s.Scheduler.PeakOffered, 40_000);
            if (LiveEventsViewBuilder.CommandFor(LiveEventsViewBuilder.Build(s), s.Scheduler!.CrateActive ? LiveClickTarget.Crate : LiveClickTarget.Banner) is { } c)
                s.Apply(Ctx, c);
            Steps(s, 300);
            var h = s.ComputeStateHash();
            if (run == 0) First = h; else Assert.Equal(First, h);
        }
    }
}
