using SideWar.Core.Content;
using SideWar.Core.Events;
using SideWar.Core.Simulation;
using Xunit;

namespace SideWar.Core.Tests;

/// <summary>T6.4b: sorteio e ledger por relatório do COLETAR ×2 (SDD-26 RF-26.16).</summary>
public sealed class OfflineQuestionTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);

    private static SimState New(bool events = true)
    {
        var s = SimState.CreateInitial(Content, TestLayouts.FromCenters(1000f, 94f, 10f, 980f, new[] { 200f, 260f, 320f, 380f }), Rng.Seed(42, 54));
        s.Stage = 1;
        if (events) s.StartEvents(Ctx);
        return s;
    }

    [Fact(DisplayName = "RF-26.16 sorteio: tema da fase, uma vez por relatório, conta como usada")]
    public void Draw_UsesStageTopic_OncePerReport()
    {
        var s = New();
        Assert.True(s.Apply(Ctx, new DrawOfflineQuestion(1)).IsOk);
        var q = s.OfflineQuestion(1)!;
        Assert.Equal(Content.Stages[0].StageTopic, q.StageTopic);
        Assert.Contains(q.Id, s.Selector!.Used(s.Stage, s.StageAttempt));
        var again = s.Apply(Ctx, new DrawOfflineQuestion(1));
        Assert.Equal(CommandReason.AlreadyUsed, again.Reason);
        Assert.Equal(q.Id, s.OfflineQuestion(1)!.Id);
    }

    [Fact(DisplayName = "RF-26.16: sem eventos ou ReportId inválido é rejeitado")]
    public void Draw_Unavailable()
    {
        Assert.Equal(CommandReason.NotAvailable, New(events: false).Apply(Ctx, new DrawOfflineQuestion(1)).Reason);
        Assert.Equal(CommandReason.NotAvailable, New().Apply(Ctx, new DrawOfflineQuestion(0)).Reason);
    }

    [Fact(DisplayName = "RF-26.16: acerto, erro e tempo; resposta única; não toca estrelas nem ledger de chefe")]
    public void Answer_RecordsOnce()
    {
        var s = New();
        s.Apply(Ctx, new DrawOfflineQuestion(1));
        Assert.Equal(CommandReason.NotAvailable, s.Apply(Ctx, new AnswerOfflineQuestion(2, 0)).Reason);
        var q = s.OfflineQuestion(1)!;
        Assert.Equal(CommandReason.UnknownCommand, s.Apply(Ctx, new AnswerOfflineQuestion(1, q.Options.Length)).Reason);
        long cursor = s.Events.Written;
        Assert.True(s.Apply(Ctx, new AnswerOfflineQuestion(1, q.CorrectIndex)).IsOk);
        Assert.True(s.OfflineLedger.Get(1)!.Correct);
        Assert.True(s.Events.TryRead(ref cursor, out var ev));
        Assert.Equal(SimEventType.OfflineQuestionAnswered, ev.Type);
        Assert.Equal(1, ev.B);
        Assert.Equal(CommandReason.AlreadyUsed, s.Apply(Ctx, new AnswerOfflineQuestion(1, 0)).Reason);
        Assert.Equal(0, s.BossQuestionsCorrect);
        Assert.Equal(0, s.Ledger.Count);

        s.Apply(Ctx, new DrawOfflineQuestion(2));
        Assert.True(s.Apply(Ctx, new AnswerOfflineQuestion(2, -1)).IsOk);
        Assert.False(s.OfflineLedger.Get(2)!.Correct);
        Assert.Null(s.OfflineLedger.Get(2)!.OptionIndex);
        Assert.Equal(2, s.OfflineLedger.MaxReportId);
    }

    [Fact(DisplayName = "RF-26.16: o ledger entra no StateHash e o save/restore do ledger preserva o hash")]
    public void Ledger_InHash_AndRestorable()
    {
        var a = New();
        var b = New();
        Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
        a.Apply(Ctx, new DrawOfflineQuestion(1));
        Assert.NotEqual(a.ComputeStateHash(), b.ComputeStateHash());
        b.Apply(Ctx, new DrawOfflineQuestion(1));
        Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
        a.Apply(Ctx, new AnswerOfflineQuestion(1, 0));
        Assert.NotEqual(a.ComputeStateHash(), b.ComputeStateHash());
    }

    [Fact(DisplayName = "RF-26.16: mesma seed e mesmos comandos dão o mesmo hash (determinismo 5x)")]
    public void Deterministic_FiveRuns()
    {
        ulong Run()
        {
            var s = New();
            for (int i = 0; i < 30; i++) s.Step(Ctx);
            s.Apply(Ctx, new DrawOfflineQuestion(1));
            s.Apply(Ctx, new AnswerOfflineQuestion(1, s.OfflineQuestion(1)!.CorrectIndex));
            s.Apply(Ctx, new CollectOfflineRewards(200, 20));
            for (int i = 0; i < 60; i++) s.Step(Ctx);
            return s.ComputeStateHash();
        }
        var first = Run();
        for (int i = 0; i < 4; i++) Assert.Equal(first, Run());
    }
}
