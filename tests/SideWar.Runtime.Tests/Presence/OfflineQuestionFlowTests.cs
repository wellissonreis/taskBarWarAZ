using SideWar.Runtime.Localization;
using SideWar.Core.Bot;
using SideWar.Core.Content;
using SideWar.Core.Simulation;
using SideWar.Runtime.Panel;
using SideWar.Runtime.Presence;
using Xunit;

namespace SideWar.Runtime.Tests.Presence;

/// <summary>T6.4b: COLETAR ×2 ligado ao Core (sorteio, ledger por relatório, ×2/×1) e cabeçalho do card.</summary>
public sealed class OfflineQuestionFlowTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);

    private static OfflineResult Result() => new(
        new OfflineReport(7200, 100_000, 3, 9, 0, 2000, 4, 5, [], 0, 0, 1, 1, 0, false),
        new SideWar.Runtime.Presence.OfflineSnapshot(DateTime.UnixEpoch, 1, 9, 9, "brazil-south", 0, 1, 0, 0, 0, [], false, 0),
        100_000, 2000, true, []);

    private static (SimState S, OfflineCardController C, long Id) Setup()
    {
        var s = SimState.CreateInitial(Content, SanityBot.FallbackLayout(), Rng.Seed(9, 1));
        s.StartEvents(Ctx);
        var c = new OfflineCardController(new FakeClock());
        c.Present(Result());
        return (s, c, c.Pending!.ReportId);
    }

    [Fact(DisplayName = "RF-26.16 acerto dobra: sorteio no Core, ledger respondido, coleta ×2")]
    public void Correct_Doubles()
    {
        var (s, c, id) = Setup();
        var req = c.PressCollectX2();
        Assert.Equal(OfflineCardEventKind.QuestionRequested, req.Kind);
        var session = OfflineQuestionFlow.Begin(s, Ctx, id)!;
        Assert.NotNull(session);
        Assert.Equal(Content.Stages[0].StageTopic, session.Question.StageTopic);
        var ev = OfflineQuestionFlow.Answer(s, Ctx, c, session, session.Question.CorrectIndex);
        Assert.Equal(2, ev.Multiplier);
        Assert.Equal(new CollectOfflineRewards(200_000, 4000), ev.Command);
        Assert.True(s.OfflineLedger.Get(id)!.Correct);
        Assert.Equal(0, s.BossQuestionsCorrect);
    }

    [Fact(DisplayName = "RF-26.16 erro, tempo e fechar valem ×1 e gravam o ledger")]
    public void WrongTimeoutClose_AreOne()
    {
        var (s, c, id) = Setup();
        c.PressCollectX2();
        var q = OfflineQuestionFlow.Begin(s, Ctx, id)!;
        int wrong = q.Question.Options.Select((o, i) => (o, i)).First(x => !x.o.Correct).i;
        Assert.Equal(1, OfflineQuestionFlow.Answer(s, Ctx, c, q, wrong).Multiplier);
        Assert.False(s.OfflineLedger.Get(id)!.Correct);

        var (s2, c2, id2) = Setup();
        c2.PressCollectX2();
        var q2 = OfflineQuestionFlow.Begin(s2, Ctx, id2)!;
        Assert.Equal(1, OfflineQuestionFlow.Timeout(s2, Ctx, c2, q2).Multiplier);
        Assert.Null(s2.OfflineLedger.Get(id2)!.OptionIndex);

        var (s3, c3, id3) = Setup();
        c3.PressCollectX2();
        OfflineQuestionFlow.Begin(s3, Ctx, id3);
        Assert.Equal(1, OfflineQuestionFlow.Close(s3, Ctx, c3, id3).Multiplier);
        Assert.True(s3.OfflineLedger.Get(id3)!.Answered);
    }

    [Fact(DisplayName = "Um relatório responde no máximo uma vez: Begin depois de respondido é nulo")]
    public void OncePerReport()
    {
        var (s, c, id) = Setup();
        c.PressCollectX2();
        var q = OfflineQuestionFlow.Begin(s, Ctx, id)!;
        OfflineQuestionFlow.Answer(s, Ctx, c, q, 0);
        Assert.Null(OfflineQuestionFlow.Begin(s, Ctx, id));
    }

    [Fact(DisplayName = "Sem eventos ligados o sorteio falha (Begin nulo) e Close coleta ×1")]
    public void NoEvents()
    {
        var s = SimState.CreateInitial(Content, SanityBot.FallbackLayout(), Rng.Seed(9, 1));
        var c = new OfflineCardController(new FakeClock());
        c.Present(Result());
        c.PressCollectX2();
        Assert.Null(OfflineQuestionFlow.Begin(s, Ctx, c.Pending!.ReportId));
        Assert.Equal(1, OfflineQuestionFlow.Close(s, Ctx, c, 1).Multiplier);
    }

    [Fact(DisplayName = "Cabeçalho do card: COLETAR ×2 · FASE nn; estados e explicação como no card do chefe")]
    public void Header_And_States()
    {
        var q = Content.Questions.Questions[0];
        var session = new OfflineQuestionSession(1, q);
        var m = QuestionCardModel.FromOfflineSession(session, 7);
        Assert.Equal("COLETAR ×2 · FASE 07", m.Header);
        Assert.Equal(QuestionCardState.Waiting, m.State);
        Assert.Equal("0:45", m.TimerText);
        Assert.Equal(q.Options.Length, m.OptionTexts.Count);
        Assert.StartsWith("A · ", m.OptionTexts[0]);

        int wrong = q.Options.Select((o, i) => (o, i)).First(x => !x.o.Correct).i;
        session.Answer(wrong);
        var w = QuestionCardModel.FromOfflineSession(session, 12);
        Assert.Equal("COLETAR ×2 · FASE 12", w.Header);
        Assert.Equal(QuestionCardState.Wrong, w.State);
        Assert.Equal(OptionVisual.Wrong, w.Visuals[wrong]);
        Assert.Equal(OptionVisual.Correct, w.Visuals[q.CorrectIndex]);
        Assert.False(string.IsNullOrEmpty(w.Explanation));
        Assert.Equal("CHEFE · ONDA 40 · IAAS", Strings.BossQuestionHeader(40, "iaas"));
    }

    [Fact(DisplayName = "Badge do widget: BadgeCredits só enquanto recolhido")]
    public void BadgeCredits_OnlyWhenCollapsed()
    {
        var k = new FakeClock();
        var c = new OfflineCardController(k);
        c.Present(Result());
        Assert.Equal(0, c.BadgeCredits);
        k.AdvanceTicks(61 * 30_000_000L);
        c.Update();
        Assert.Equal(100_000, c.BadgeCredits);
        c.ReopenFromBadge();
        Assert.Equal(0, c.BadgeCredits);
    }
}
