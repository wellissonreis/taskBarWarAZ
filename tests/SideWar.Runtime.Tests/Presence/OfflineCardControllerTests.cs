using SideWar.Core.Content;
using SideWar.Core.Events;
using SideWar.Core.Simulation;
using SideWar.Runtime.Panel;
using SideWar.Runtime.Presence;
using Xunit;

namespace SideWar.Runtime.Tests.Presence;

public sealed class OfflineCardControllerTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);

    private static OfflineReport Report(long credits = 186_000, long xp = 4100, int de = 22, int para = 37, int quedas = 0,
        int nivelDe = 22, int nivelPara = 23, int perdaPicos = 4, int perdaCaixas = 3, int perdaChefes = 1,
        double tempo = 3 * 3600 + 12 * 60, params AutoUpgrade[] ups)
        => new(tempo, credits, de, para, quedas, xp, nivelDe, nivelPara, ups, ups.Length, 0, perdaPicos, perdaCaixas, perdaChefes, false);

    private static OfflineResult Result(OfflineReport r, long? pendingCredits = null, bool show = true)
        => new(r, new OfflineSnapshot(DateTime.UnixEpoch, 1, r.OndaPara, r.OndaPara, "brazil-south", 0, 1, 0, 0, 0, [], false, 0),
            pendingCredits ?? r.Creditos, r.Xp, show, []);

    private static (OfflineCardController C, FakeClock K) New()
    {
        var k = new FakeClock();
        return (new OfflineCardController(k), k);
    }

    private static void Wait(FakeClock k, double s) => k.AdvanceTicks((long)(s * k.TicksPerSecond));

    [Fact(DisplayName = "CA-25.7 COLETAR +X coleta ×1 na hora e limpa o pendente")]
    public void CollectSimple()
    {
        var (c, _) = New();
        var e = c.Present(Result(Report()));
        Assert.Equal(OfflineCardEventKind.Presented, e.Kind);
        Assert.Equal(OfflineCardState.Open, c.State);
        var ev = c.PressCollect();
        Assert.Equal(OfflineCardEventKind.Collected, ev.Kind);
        Assert.Equal(1, ev.Multiplier);
        Assert.Equal(new CollectOfflineRewards(186_000, 4100), ev.Command);
        Assert.True(ev.Interaction);
        Assert.Null(c.Pending);
        Assert.Equal(OfflineCardState.None, c.State);
    }

    [Fact(DisplayName = "CA-25.7 COLETAR ×2: abre a pergunta; acerto dobra créditos e XP")]
    public void X2Correct()
    {
        var (c, _) = New();
        c.Present(Result(Report()));
        var q = c.PressCollectX2();
        Assert.Equal(OfflineCardEventKind.QuestionRequested, q.Kind);
        Assert.Equal(OfflineCardState.AskingQuestion, c.State);
        Assert.NotNull(c.Pending);   // nada coletado ainda
        var ev = c.AnswerQuestion(true);
        Assert.Equal(2, ev.Multiplier);
        Assert.Equal(new CollectOfflineRewards(372_000, 8200), ev.Command);
        Assert.Null(c.Pending);
    }

    [Theory(DisplayName = "CA-25.7 erro, tempo esgotado e pergunta fechada valem ×1")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void X2FallbacksAreX1(int how)
    {
        var (c, _) = New();
        c.Present(Result(Report()));
        c.PressCollectX2();
        var ev = how switch { 0 => c.AnswerQuestion(false), 1 => c.QuestionTimedOut(), _ => c.QuestionClosed() };
        Assert.Equal(1, ev.Multiplier);
        Assert.Equal(new CollectOfflineRewards(186_000, 4100), ev.Command);
    }

    [Fact(DisplayName = "Resposta/timeout/fechar sem pergunta aberta são ignorados; nada é coletado duas vezes")]
    public void NoDoubleCollect()
    {
        var (c, _) = New();
        Assert.Equal(OfflineCardEventKind.Ignored, c.PressCollect().Kind);
        c.Present(Result(Report()));
        Assert.Equal(OfflineCardEventKind.Ignored, c.AnswerQuestion(true).Kind);
        Assert.Equal(OfflineCardEventKind.Ignored, c.QuestionTimedOut().Kind);
        c.PressCollect();
        Assert.Equal(OfflineCardEventKind.Ignored, c.PressCollect().Kind);
        Assert.Equal(OfflineCardEventKind.Ignored, c.PressCollectX2().Kind);
    }

    [Fact(DisplayName = "CA-25.7 sem clique por 60 s recolhe; antes disso não; deadline = agora + 60 s")]
    public void CollapseAt60()
    {
        var (c, k) = New();
        c.Present(Result(Report()));
        Assert.Equal(60 * 30_000_000L, c.DeadlineTicks);
        Wait(k, 59.9);
        Assert.Equal(OfflineCardEventKind.Ignored, c.Update().Kind);
        Wait(k, 0.1);
        var e = c.Update();
        Assert.Equal(OfflineCardEventKind.Collapsed, e.Kind);
        Assert.True(e.Interaction);
        Assert.Equal(OfflineCardState.Collapsed, c.State);
        Assert.Equal("+186K", c.BadgeText);
        Assert.Equal(0, c.DeadlineTicks);
        Assert.Equal(OfflineCardEventKind.Ignored, c.Update().Kind);   // não recolhe duas vezes
    }

    [Fact(DisplayName = "Clique no fundo reinicia os 60 s; hover (nenhuma chamada) não conta")]
    public void ClickResetsTimer()
    {
        var (c, k) = New();
        c.Present(Result(Report()));
        Wait(k, 50);
        Assert.True(c.NotifyMousePress());
        Wait(k, 50);
        Assert.Equal(OfflineCardEventKind.Ignored, c.Update().Kind);   // 100 s desde o início, 50 s desde o clique
        Wait(k, 10);
        Assert.Equal(OfflineCardEventKind.Collapsed, c.Update().Kind);
    }

    [Fact(DisplayName = "O contador pausa durante a pergunta e o card não recolhe por baixo dela")]
    public void PausedWhileAsking()
    {
        var (c, k) = New();
        c.Present(Result(Report()));
        c.PressCollectX2();
        Assert.Equal(0, c.DeadlineTicks);
        Wait(k, 600);
        Assert.Equal(OfflineCardEventKind.Ignored, c.Update().Kind);
        Assert.Equal(OfflineCardState.AskingQuestion, c.State);
        Assert.False(c.NotifyMousePress());
    }

    [Fact(DisplayName = "Clicar no badge reabre o card e reinicia o contador; pendente intacto")]
    public void ReopenFromBadge()
    {
        var (c, k) = New();
        c.Present(Result(Report()));
        Wait(k, 60);
        c.Update();
        var v = c.Version;
        var e = c.ReopenFromBadge();
        Assert.Equal(OfflineCardEventKind.Reopened, e.Kind);
        Assert.True(e.Interaction);
        Assert.Equal(OfflineCardState.Open, c.State);
        Assert.Null(c.BadgeText);
        Assert.Equal(k.NowTicks + 60 * 30_000_000L, c.DeadlineTicks);
        Assert.True(c.Version > v);
        Assert.Equal(186_000, c.Pending!.Credits);
        Assert.Equal(OfflineCardEventKind.Ignored, c.ReopenFromBadge().Kind);
    }

    [Fact(DisplayName = "Novo offline com pendência soma num relatório; ledger novo permite perguntar de novo")]
    public void MergesPending()
    {
        var (c, _) = New();
        c.Present(Result(Report(credits: 100, xp: 10, de: 5, para: 9, nivelDe: 3, nivelPara: 4, perdaPicos: 1, perdaCaixas: 2, perdaChefes: 0,
            tempo: 3600, ups: new AutoUpgrade(TowerType.Bolt, 1, 3))));
        var first = c.Pending!.ReportId;
        c.Present(Result(Report(credits: 50, xp: 5, de: 9, para: 12, nivelDe: 4, nivelPara: 5, perdaPicos: 2, perdaCaixas: 1, perdaChefes: 1,
            tempo: 3600, ups: new AutoUpgrade(TowerType.Frost, 2, 4))));
        var p = c.Pending!;
        Assert.NotEqual(first, p.ReportId);
        Assert.Equal(150, p.Credits);
        Assert.Equal(15, p.Xp);
        Assert.Equal(7200, p.Report.TempoAcumuladoS);
        Assert.Equal((5, 12, 3, 5), (p.Report.OndaDe, p.Report.OndaPara, p.Report.NivelDe, p.Report.NivelPara));
        Assert.Equal((3, 3, 1), (p.Report.PerdaPicos, p.Report.PerdaCaixas, p.Report.PerdaChefes));
        Assert.Equal(2, p.Report.MelhoriasAuto.Count);
        Assert.Equal(2, p.Report.NMelhorias);
        Assert.Equal(OfflineCardState.Open, c.State);
    }

    [Fact(DisplayName = "Merge limita o tempo a 12 h e satura créditos")]
    public void MergeCaps()
    {
        var a = PendingRewards.From(1, Result(Report(credits: long.MaxValue - 1, tempo: 10 * 3600)));
        var b = PendingRewards.From(2, Result(Report(credits: 10, tempo: 10 * 3600)));
        var m = a.Merge(b, 3);
        Assert.Equal(12 * 3600, m.Report.TempoAcumuladoS);
        Assert.Equal(long.MaxValue, m.Credits);
    }

    [Fact(DisplayName = "Coleta ×2 satura em vez de estourar")]
    public void ScaleSaturates()
    {
        var (c, _) = New();
        c.Present(Result(Report(credits: long.MaxValue / 2 + 5, xp: 1), pendingCredits: long.MaxValue / 2 + 5));
        c.PressCollectX2();
        Assert.Equal(long.MaxValue, c.AnswerQuestion(true).Command!.Credits);
    }

    [Fact(DisplayName = "Restore: Recolhido volta como badge; Aberto reinicia o contador; ids não colidem")]
    public void Restore()
    {
        var (c, k) = New();
        var p = PendingRewards.From(7, Result(Report()));
        c.Restore(p, collapsed: true);
        Assert.Equal(OfflineCardState.Collapsed, c.State);
        Assert.Equal("+186K", c.BadgeText);
        Assert.Equal(8, c.TakeReportId());
        var (d, _) = New();
        d.Restore(p, collapsed: false);
        Assert.Equal(OfflineCardState.Open, d.State);
        Assert.Equal(60 * 30_000_000L, d.DeadlineTicks);
        _ = k;
    }

    [Fact(DisplayName = "Pressionar COLETAR com o card recolhido é ignorado (o botão só existe aberto)")]
    public void CollectNeedsOpen()
    {
        var (c, k) = New();
        c.Present(Result(Report()));
        Wait(k, 60);
        c.Update();
        Assert.Equal(OfflineCardEventKind.Ignored, c.PressCollect().Kind);
        Assert.Equal(OfflineCardEventKind.Ignored, c.PressCollectX2().Kind);
        Assert.NotNull(c.Pending);
    }

    [Fact(DisplayName = "D-11 determinismo: coleta pelo CollectOfflineRewards entre ticks; níveis sobem no próximo tick")]
    public void CollectCommandAppliesBetweenTicks()
    {
        static SimState Run()
        {
            var layout = TestLayouts.Default();
            var s = SimState.CreateInitial(Content, layout, Rng.Seed(42, 54));
            var (c, _) = New();
            c.Present(Result(Report(credits: 1234, xp: 200)));
            c.PressCollectX2();
            var cmd = c.AnswerQuestion(true).Command!;
            Assert.True(s.Apply(Ctx, cmd).IsOk);
            Assert.Equal(Content.Economy.StartingCredits + 2468, s.Credits);
            Assert.Equal(400, s.Xp);
            s.Step(Ctx);
            return s;
        }
        var a = Run();
        var b = Run();
        Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
        Assert.True(a.Level > 1);   // 400 XP: vários níveis (45 + 90 + 135 = 270 → nível 4)
        Assert.Equal(4, a.Level);
    }

    [Fact(DisplayName = "Comando com valor negativo é rejeitado")]
    public void NegativeRejected()
    {
        var s = SimState.CreateInitial(Content, TestLayouts.Default(), Rng.Seed(1, 1));
        Assert.False(s.Apply(Ctx, new CollectOfflineRewards(-1, 0)).IsOk);
        Assert.False(s.Apply(Ctx, new CollectOfflineRewards(0, -1)).IsOk);
    }

    [Fact(DisplayName = "OfflineQuestionSession: 45 s de SimTime, uma resposta, acerto/erro/timeout")]
    public void QuestionSession()
    {
        var q = new QuestionDef("q", "iaas", "mandatory", "?", [new("a", false, "x"), new("b", true, "y")]);
        var s = new OfflineQuestionSession(1, q);
        Assert.Equal(45, s.Remaining);
        Assert.False(s.Advance(44.9));
        Assert.True(s.Advance(0.2));
        Assert.Equal(BossResult.Timeout, s.Result);
        Assert.False(s.Answer(1));      // já resolvida
        Assert.False(s.Advance(1));

        var t = new OfflineQuestionSession(2, q);
        Assert.True(t.Answer(1));
        Assert.Equal(BossResult.Correct, t.Result);
        Assert.False(t.Answer(0));
        Assert.Equal(1, t.OptionIndex);
        var w = new OfflineQuestionSession(3, q);
        Assert.False(w.Answer(0));
        Assert.Equal(BossResult.Wrong, w.Result);
        Assert.Throws<ArgumentOutOfRangeException>(() => new OfflineQuestionSession(4, q).Answer(2));
    }

    [Fact(DisplayName = "RF-25.10 aplicação: níveis, saldo, carry, onda e ondaMax; sem inimigos; ShowCard não credita")]
    public void ApplierWithCard()
    {
        var layout = TestLayouts.Default();
        var s = SimState.CreateInitial(Content, layout, Rng.Seed(42, 54));
        s.Credits = 1_000_000;
        Assert.True(s.Apply(Ctx, new BuildTower(TowerType.Bolt, TestLayouts.X(0))).IsOk);
        var id = s.Towers.At(0).Id;
        var snap = new OfflineSnapshot(DateTime.UnixEpoch, 1, 9, 9, "brazil-south", 0, 1, 0, 777, 0.25,
            [new OfflineTower(id, TowerType.Bolt, true, 5), new OfflineTower(999, TowerType.Frost, true, 3)], true, 0);
        var res = new OfflineResult(Report(), snap, 500, 40, true, []);
        OfflineResultApplier.Apply(s, Ctx, res);
        Assert.Equal(5, s.Towers.At(0).Level);
        Assert.Equal(777, s.Credits);
        Assert.Equal(0.25, s.Carry);
        Assert.Equal(0, s.Xp);
        Assert.Equal(9, s.Wave);
        Assert.True(s.StageWaveMax >= 9);
        Assert.Equal(0, s.Enemies.Count);
        Assert.NotNull(s.ActivePlan);
    }

    [Fact(DisplayName = "DP-25.2 sem card: pendente entra direto no saldo e no XP")]
    public void ApplierWithoutCard()
    {
        var s = SimState.CreateInitial(Content, TestLayouts.Default(), Rng.Seed(42, 54));
        var snap = new OfflineSnapshot(DateTime.UnixEpoch, 1, 3, 3, "brazil-south", 0, 1, 0, 100, 0, [], false, 0);
        OfflineResultApplier.Apply(s, Ctx, new OfflineResult(Report(), snap, 40, 7, false, []));
        Assert.Equal(140, s.Credits);
        Assert.Equal(7, s.Xp);
        Assert.Equal(3, s.Wave);
    }

    [Fact(DisplayName = "Aplicação é determinística e recusada durante um tick")]
    public void ApplierDeterministicAndGuarded()
    {
        static ulong Hash()
        {
            var s = SimState.CreateInitial(Content, TestLayouts.Default(), Rng.Seed(42, 54));
            var snap = new OfflineSnapshot(DateTime.UnixEpoch, 1, 12, 14, "brazil-south", 0, 1, 0, 321, 0.5, [], false, 0);
            OfflineResultApplier.Apply(s, Ctx, new OfflineResult(Report(), snap, 0, 0, true, []));
            for (var i = 0; i < 90; i++) s.Step(Ctx);
            return s.ComputeStateHash();
        }
        Assert.Equal(Hash(), Hash());
        var st = SimState.CreateInitial(Content, TestLayouts.Default(), Rng.Seed(1, 1));
        st.BeginTick();
        Assert.Throws<InvalidOperationException>(() => OfflineResultApplier.Apply(st, Ctx,
            new OfflineResult(Report(), new OfflineSnapshot(DateTime.UnixEpoch, 1, 1, 1, "x", 0, 1, 0, 0, 0, [], false, 0), 0, 0, true, [])));
    }
}
