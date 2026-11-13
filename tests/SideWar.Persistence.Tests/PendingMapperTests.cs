using System.Text.Json;
using SideWar.Core.Bot;
using SideWar.Core.Content;
using SideWar.Core.Simulation;
using SideWar.Persistence.Mapping;
using SideWar.Persistence.Profile;
using SideWar.Persistence.Session;
using SideWar.Runtime;
using SideWar.Runtime.Presence;
using Xunit;

namespace SideWar.Persistence.Tests;

/// <summary>T6.4b: pending + cardState (SDD-14 RF-25.11) e ledger do COLETAR ×2 (SDD-26 RF-26.16) no save → restore.</summary>
public sealed class PendingMapperTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);
    private static readonly DateTime T0 = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Clock : IClock
    {
        public long NowTicks { get; private set; }
        public double TicksPerSecond => 1000;
        public void Advance(double s) => NowTicks += (long)(s * 1000);
    }

    private static OfflineResult Result() => new(
        new OfflineReport(3 * 3600 + 720, 186_000, 22, 37, 1, 4100, 22, 24,
            [new AutoUpgrade(TowerType.Bolt, 12, 14), new AutoUpgrade(TowerType.Frost, 8, 9)], 3, 5000, 4, 3, 1, true),
        new SideWar.Runtime.Presence.OfflineSnapshot(DateTime.UnixEpoch, 1, 37, 37, "brazil-south", 0, 1, 0, 0, 0, [], false, 0),
        181_000, 4100, true, []);

    private static SimState Game()
    {
        var s = SimState.CreateInitial(Content, SanityBot.FallbackLayout(), Rng.Seed(7, 3));
        s.StartEvents(Ctx);
        return s;
    }

    private static Session.OfflineSnapshot Cycle(SimState s, OfflineCardController c)
    {
        var (pending, card) = PendingMapper.ToSession(c);
        var snap = SimSnapshotMapper.ToSession(s, Ctx, T0, new PresenceData(), ProfileData.Default, pending: pending, cardState: card);
        using var doc = JsonDocument.Parse(StoreTestSupport.Json(snap));
        return SessionSerializer.Read(doc.RootElement);
    }

    [Fact(DisplayName = "CA-25.9 pending e cardState: ida e volta idêntica (Aberto e Recolhido), pelo JSON real")]
    public void Pending_RoundTrip_BothStates()
    {
        var s = Game();
        var k = new Clock();
        var c = new OfflineCardController(k);
        c.Present(Result());
        var original = c.Pending!;

        var open = Cycle(s, c);
        Assert.Equal(CardStateData.Aberto, open.CardState);
        var back = PendingMapper.FromData(open.Pending);
        Assert.Equal(original with { Report = original.Report with { MelhoriasAuto = [] } }, back with { Report = back.Report with { MelhoriasAuto = [] } });
        Assert.Equal(original.Report.MelhoriasAuto, back.Report.MelhoriasAuto);

        k.Advance(61);
        c.Update();
        Assert.Equal(OfflineCardState.Collapsed, c.State);
        var collapsed = Cycle(s, c);
        Assert.Equal(CardStateData.Recolhido, collapsed.CardState);

        var c2 = new OfflineCardController(k);
        PendingMapper.Restore(c2, collapsed, s);
        Assert.Equal(OfflineCardState.Collapsed, c2.State);
        Assert.Equal(original.Credits, c2.Pending!.Credits);
        Assert.Equal("+" + SideWar.Core.Economy.CreditFormatter.Format(181_000), c2.BadgeText);
        Assert.Equal(OfflineCardState.Open, c2.ReopenFromBadge().Kind == OfflineCardEventKind.Reopened ? c2.State : OfflineCardState.None);
    }

    [Fact(DisplayName = "Sem pendência grava vazio/Aberto e o restore não cria card")]
    public void NoPending()
    {
        var s = Game();
        var c = new OfflineCardController(new Clock());
        var snap = Cycle(s, c);
        Assert.Equal(0, snap.Pending.ReportId);
        var c2 = new OfflineCardController(new Clock());
        PendingMapper.Restore(c2, snap, s);
        Assert.False(c2.HasPending);
        Assert.Equal(OfflineCardState.None, c2.State);
    }

    [Fact(DisplayName = "RF-26.16 ledger por relatório sobrevive ao save: hash igual; relatório já sorteado não pergunta de novo; ids não repetem")]
    public void Ledger_Survives_Restore()
    {
        var s = Game();
        var c = new OfflineCardController(new Clock());
        c.Present(Result());
        long id = c.Pending!.ReportId;
        Assert.True(s.Apply(Ctx, new DrawOfflineQuestion(id)).IsOk);   // pergunta aberta no momento do save

        var snap = Cycle(s, c);
        var r = SimSnapshotMapper.Restore(Ctx, snap);
        Assert.True(r.IsRestored, r.Detail);
        Assert.Equal(s.ComputeStateHash(), r.State!.ComputeStateHash());
        Assert.Equal(s.OfflineQuestion(id)!.Id, r.State.OfflineQuestion(id)!.Id);

        var c2 = new OfflineCardController(new Clock());
        PendingMapper.Restore(c2, snap, r.State);
        var ev = c2.PressCollectX2();
        Assert.Equal(OfflineCardEventKind.Collected, ev.Kind);   // ledger: já perguntou → ×1
        Assert.Equal(1, ev.Multiplier);
        Assert.True(c2.TakeReportId() > id);
        Assert.Equal(CommandReason.AlreadyUsed, r.State.Apply(Ctx, new DrawOfflineQuestion(id)).Reason);

        // resposta registrada também sobrevive
        s.Apply(Ctx, new AnswerOfflineQuestion(id, s.OfflineQuestion(id)!.CorrectIndex));
        var r2 = SimSnapshotMapper.Restore(Ctx, Cycle(s, c));
        Assert.True(r2.IsRestored, r2.Detail);
        Assert.True(r2.State!.OfflineLedger.Get(id)!.Correct);
        Assert.Equal(s.ComputeStateHash(), r2.State.ComputeStateHash());
    }

    [Fact(DisplayName = "Perfil guarda o ledger do COLETAR ×2 (collectLedger)")]
    public void Profile_HasCollectLedger()
    {
        var s = Game();
        s.Apply(Ctx, new DrawOfflineQuestion(4));
        s.Apply(Ctx, new AnswerOfflineQuestion(4, -1));
        var p = SimSnapshotMapper.ToProfile(s, Ctx, ProfileData.Default);
        var e = Assert.Single(p.CollectLedger);
        Assert.Equal(4, e.ReportId);
        Assert.True(e.Answered);
        Assert.Null(e.OptionIndex);
    }
}
