using SideWar.Core.Simulation;
using SideWar.Runtime.Composition;
using SideWar.Runtime.Lifecycle;
using SideWar.Runtime.Panel;
using SideWar.Runtime.Presence;
using Xunit;

namespace SideWar.Runtime.Tests.Composition;

/// <summary>T4.10b: ligação headless do card "Bem-vindo de volta" (fakes; sem janelas).</summary>
public sealed class OfflineReturnCoordinatorTests
{
    private sealed class FakeView : IOfflineCardPresenter
    {
        public readonly List<string> Log = [];
        public void ShowCard() => Log.Add("ShowCard");
        public void HideCard() => Log.Add("HideCard");
        public void RefreshCard() => Log.Add("RefreshCard");
        public void ShowQuestion(OfflineQuestionSession session, int stage) => Log.Add("ShowQuestion");
        public void RefreshQuestion(OfflineQuestionSession session, int stage) => Log.Add("RefreshQuestion");
        public void HideQuestion() => Log.Add("HideQuestion");
        public void BadgeChanged() => Log.Add("Badge");
        public int Count(string s) => Log.Count(x => x == s);
    }

    private sealed class Rig
    {
        public readonly FakeClock Clock = new();
        public DateTime Utc = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        public readonly FakeView View = new();
        public readonly GameRuntime Runtime;
        public readonly OfflineCardController Card;
        public readonly OfflineReturnCoordinator Coord;

        public Rig()
        {
            Runtime = new GameRuntime(new GameRuntimeOptions { Clock = Clock, UtcNow = () => Utc });
            Card = new OfflineCardController(Clock);
            Coord = new OfflineReturnCoordinator(Runtime, Card, View, Clock, () => Utc);
            Runtime.State.Credits = 100_000;
            Runtime.State.StartEvents(Runtime.Context);
            Assert.True(Runtime.State.Apply(Runtime.Context, new BuildTower(TowerType.Bolt, TestLayouts.X(0))).IsOk);
            Runtime.Dispatch(Runtime.App.LeaveHidden(HiddenReason.Starting)); // visível
            Runtime.Host.OnWake(Clock);
        }

        public void Advance(double seconds) => Clock.AdvanceTicks((long)(seconds * Clock.TicksPerSecond));
        public void Wake() => Coord.OnWake(Clock);

        /// <summary>Oculta, passa <paramref name="hours"/> h e reexibe (RestoreContext).</summary>
        public void Away(double hours)
        {
            Coord.OnHide();
            Utc += TimeSpan.FromHours(hours);
            Coord.OnRestoreContext();
        }
    }

    [Fact(DisplayName = "T4.10b retorno ≥ 10 min: Compute → Apply → Present abre o card e conta como presença")]
    public void LongAbsenceShowsCard()
    {
        var r = new Rig();
        r.Away(3);
        Assert.Equal(OfflineCardState.Open, r.Card.State);
        Assert.True(r.Card.Pending!.Credits > 0);
        Assert.Equal(1, r.View.Count("ShowCard"));
        Assert.False(r.Coord.HasBaseline);
        r.Runtime.Host.OnWake(r.Clock);
        Assert.Equal(PresenceState.Presente, r.Runtime.Presence.State); // Present tem Interaction (RF-25.1)
    }

    [Fact(DisplayName = "T4.10b retorno < 10 min: ganhos direto no saldo, sem card")]
    public void ShortAbsenceCreditsDirectly()
    {
        var r = new Rig();
        r.Away(0.1); // 6 min
        Assert.Equal(OfflineCardState.None, r.Card.State);
        Assert.Equal(0, r.View.Count("ShowCard"));
    }

    [Fact(DisplayName = "T4.10b COLETAR +X: comando ao Core, card fecha e saldo sobe")]
    public void CollectApplies()
    {
        var r = new Rig();
        r.Away(3);
        long credits = r.Card.Pending!.Credits;
        long before = r.Runtime.State.Credits;
        r.Coord.CardClick(OfflineCardHitKind.Collect);
        Assert.Equal(before + credits, r.Runtime.State.Credits);
        Assert.Equal(OfflineCardState.None, r.Card.State);
        Assert.Equal(1, r.View.Count("HideCard"));
        Assert.Null(r.Coord.NextDeadline);
    }

    [Fact(DisplayName = "T4.10b 60 s sem clique recolhe para o badge; clique no fundo reinicia; badge reabre")]
    public void CollapseAndBadge()
    {
        var r = new Rig();
        r.Away(3);
        Assert.NotNull(r.Coord.NextDeadline);
        r.Advance(50);
        r.Coord.CardMouseDown(); // reinicia
        r.Advance(50);
        r.Wake();
        Assert.Equal(OfflineCardState.Open, r.Card.State);
        r.Advance(11);
        r.Wake();
        Assert.Equal(OfflineCardState.Collapsed, r.Card.State);
        Assert.Equal(1, r.View.Count("HideCard"));
        Assert.True(r.Card.BadgeCredits > 0);
        Assert.Null(r.Coord.NextDeadline);
        r.Coord.BadgeClicked();
        Assert.Equal(OfflineCardState.Open, r.Card.State);
        Assert.Equal(2, r.View.Count("ShowCard"));
    }

    [Fact(DisplayName = "T4.10b oculto: 0 deadlines; ao reexibir o prazo de 60 s é rearmado")]
    public void HiddenHasNoDeadlineAndRearms()
    {
        var r = new Rig();
        r.Away(3);
        r.Runtime.Dispatch(r.Runtime.App.EnterHidden(HiddenReason.User));
        Assert.Null(r.Coord.NextDeadline);
        r.Advance(300);
        r.Wake();
        Assert.Equal(OfflineCardState.Open, r.Card.State);
        r.Runtime.Dispatch(r.Runtime.App.LeaveHidden(HiddenReason.User));
        r.Coord.OnRestoreContext();
        r.Wake();
        Assert.Equal(OfflineCardState.Open, r.Card.State);
    }

    [Fact(DisplayName = "T4.10b COLETAR ×2: pergunta de 45 s; acerto ×2 e janela fecha 2 s depois")]
    public void QuestionCorrectDoubles()
    {
        var r = new Rig();
        r.Away(3);
        long credits = r.Card.Pending!.Credits;
        long before = r.Runtime.State.Credits;
        r.Coord.CardClick(OfflineCardHitKind.CollectX2);
        var q = r.Coord.Question;
        Assert.NotNull(q);
        Assert.Equal(OfflineCardState.AskingQuestion, r.Card.State);
        Assert.Equal(1, r.View.Count("ShowQuestion"));
        int correct = q!.Question.CorrectIndex;
        r.Coord.QuestionAnswered(correct);
        Assert.Equal(before + credits * 2, r.Runtime.State.Credits);
        Assert.True(r.Coord.Question!.IsResolved);
        r.Advance(2.1);
        r.Wake();
        Assert.Null(r.Coord.Question);
        Assert.Equal(1, r.View.Count("HideQuestion"));
    }

    [Fact(DisplayName = "T4.10b pergunta: erro ×1; 45 s esgotados ×1; fechar ×1")]
    public void QuestionWrongTimeoutClose()
    {
        var wrong = new Rig();
        wrong.Away(3);
        long c1 = wrong.Card.Pending!.Credits, b1 = wrong.Runtime.State.Credits;
        wrong.Coord.CardClick(OfflineCardHitKind.CollectX2);
        var q = wrong.Coord.Question!;
        wrong.Coord.QuestionAnswered(Enumerable.Range(0, q.Question.Options.Length).First(i => i != q.Question.CorrectIndex));
        Assert.Equal(b1 + c1, wrong.Runtime.State.Credits);

        var timeout = new Rig();
        timeout.Away(3);
        long c2 = timeout.Card.Pending!.Credits, b2 = timeout.Runtime.State.Credits;
        timeout.Coord.CardClick(OfflineCardHitKind.CollectX2);
        for (int i = 0; i < 50 && timeout.Card.State == OfflineCardState.AskingQuestion; i++) { timeout.Advance(1); timeout.Wake(); }
        Assert.Equal(OfflineCardState.None, timeout.Card.State);
        Assert.Equal(b2 + c2, timeout.Runtime.State.Credits);

        var close = new Rig();
        close.Away(3);
        long c3 = close.Card.Pending!.Credits, b3 = close.Runtime.State.Credits;
        close.Coord.CardClick(OfflineCardHitKind.CollectX2);
        close.Coord.QuestionClosedByUser();
        Assert.Null(close.Coord.Question);
        Assert.Equal(b3 + c3, close.Runtime.State.Credits);
    }

    [Fact(DisplayName = "T4.10b a pergunta não corre com a cena oculta")]
    public void QuestionPausesWhileHidden()
    {
        var r = new Rig();
        r.Away(3);
        r.Coord.CardClick(OfflineCardHitKind.CollectX2);
        r.Runtime.Dispatch(r.Runtime.App.EnterHidden(HiddenReason.User));
        r.Advance(120);
        r.Wake();
        Assert.Equal(OfflineCardState.AskingQuestion, r.Card.State);
        Assert.True(r.Coord.Question!.Remaining > 44);
    }

    [Fact(DisplayName = "T4.10b baseline do início (session.json) calcula no primeiro RestoreContext")]
    public void StartupBaseline()
    {
        var r = new Rig();
        r.Coord.SetBaseline(r.Utc - TimeSpan.FromHours(5), true, 0);
        Assert.True(r.Coord.HasBaseline);
        r.Coord.OnRestoreContext();
        Assert.Equal(OfflineCardState.Open, r.Card.State);
    }

    [Fact(DisplayName = "T4.10b card restaurado do save reabre e conta 60 s de novo")]
    public void RestoredCardReopens()
    {
        var r = new Rig();
        r.Away(3);
        var pending = r.Card.Pending!;
        var other = new Rig();
        other.Card.Restore(pending, collapsed: false);
        other.Coord.OnRestoreContext();
        Assert.Equal(1, other.View.Count("ShowCard"));
        var collapsed = new Rig();
        collapsed.Card.Restore(pending, collapsed: true);
        collapsed.Coord.OnRestoreContext();
        Assert.Equal(0, collapsed.View.Count("ShowCard"));
        Assert.True(collapsed.Card.BadgeCredits > 0);
    }
}
