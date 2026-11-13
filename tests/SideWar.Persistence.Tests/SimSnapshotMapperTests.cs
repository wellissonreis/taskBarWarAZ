using System.Text.Json;
using SideWar.Core.Bot;
using SideWar.Core.Content;
using SideWar.Core.Events;
using SideWar.Core.Simulation;
using SideWar.Persistence.Mapping;
using SideWar.Persistence.Profile;
using SideWar.Persistence.Session;
using Xunit;

namespace SideWar.Persistence.Tests;

/// <summary>T6.9a: ida e volta SimState ⇄ DTO com StateHash (CA-14.1) e ContentHash divergente (CA-14.4).</summary>
public sealed class SimSnapshotMapperTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);
    private static readonly DateTime T0 = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private static OfflineSnapshot Save(SimState s, ProfileData? p = null) =>
        SimSnapshotMapper.ToSession(s, Ctx, T0, new PresenceData { State = s.Present ? PresenceStateData.Presente : PresenceStateData.Ausente, AutoScaleAccumSeconds = 12.5 }, p ?? ProfileData.Default);

    /// <summary>Passa pelo JSON de verdade (source-gen) antes de restaurar.</summary>
    private static OfflineSnapshot ThroughJson(OfflineSnapshot s)
    {
        using var doc = JsonDocument.Parse(StoreTestSupport.Json(s));
        return SessionSerializer.Read(doc.RootElement);
    }

    private static SimState NewGame(ulong seed = 11)
    {
        var s = SimState.CreateInitial(Content, SanityBot.FallbackLayout(), Rng.Seed(seed, 5));
        s.StartEvents(Ctx);
        s.IncomePerSecond = 25;
        return s;
    }

    [Fact(DisplayName = "T6.9a ida e volta: StateHash igual em ≥50 pontos de uma partida do SanityBot (Presente, Ausente, chefe, farm)")]
    public void RoundTrip_ManyPoints_Hash_Equal_And_Continues_Identically()
    {
        var s = NewGame();
        var bot = new SanityBot(Ctx);
        int points = 0, encounter = 0, hold = 0, farm = 0, enemies = 0, peak = 0;
        for (int tick = 0; tick < 400_000 && points < 120; tick++)
        {
            // Ausente por trechos; em Ausente o host joga a onda anterior em farm enquanto o chefe espera.
            s.Present = (tick / 6000) % 4 != 3;
            if (!s.Present && s.BossHold && s.ActivePlan is null) s.StartFarmWave(Ctx, s.Wave - 1);
            if (s.Encounter is { IsActive: true } e && e.Elapsed > 10 + (tick % 7))
                s.Apply(Ctx, tick % 2 == 0 ? new AnswerBossQuestion(0) : new OpenBossQuestion());
            if (s.Scheduler?.PeakOffered == true && tick % 3 == 0) s.Apply(Ctx, new ClickPeakBanner());
            if (s.Scheduler?.CrateActive == true) s.Apply(Ctx, new ClickSupplyCrate());
            bot.Act(s);
            s.Step(Ctx);

            bool special = (s.Encounter is not null && tick % 53 == 0) || (s.FarmWave > 0 && tick % 211 == 0) || (s.BossHold && tick % 97 == 0);
            if (tick % 2500 != 0 && !special) continue;
            points++;
            if (s.Encounter is not null) encounter++;
            if (s.BossHold) hold++;
            if (s.FarmWave > 0) farm++;
            if (s.Enemies.Count > 0) enemies++;
            if (s.Scheduler?.Peak != PeakPhase.Waiting) peak++;

            var r = SimSnapshotMapper.Restore(Ctx, ThroughJson(Save(s)));
            Assert.True(r.IsRestored, $"tick {s.Tick}: {r.Status} {r.Detail}");
            Assert.Equal(s.ComputeStateHash(), r.State!.ComputeStateHash());

            if (points % 10 == 0)
            {
                // Continua idêntica: mesmos comandos/ticks no original e no restaurado.
                var copy = r.State;
                var botB = new SanityBot(Ctx);
                for (int k = 0; k < 600; k++) { copy.Present = s.Present; botB.Act(copy); copy.Step(Ctx); }
                var orig = SimSnapshotMapper.Restore(Ctx, ThroughJson(Save(s))).State!;
                var botC = new SanityBot(Ctx);
                for (int k = 0; k < 600; k++) { orig.Present = s.Present; botC.Act(orig); orig.Step(Ctx); }
                Assert.Equal(orig.ComputeStateHash(), copy.ComputeStateHash());
            }
        }
        Assert.True(points >= 50, $"pontos={points}");
        Assert.True(enemies > 0 && encounter > 0 && hold > 0 && farm > 0, $"cobertura: inimigos={enemies} encontro={encounter} espera={hold} farm={farm} pico={peak}");
    }

    [Fact(DisplayName = "T6.9a ida e volta com encontro resolvido e card aberto, fila de pico e SEM SLOT")]
    public void RoundTrip_Encounter_Card_And_NoSlot()
    {
        var s = NewGame(99);
        var bot = new SanityBot(Ctx);
        for (int i = 0; i < 300_000 && s.Encounter is not { IsActive: true }; i++) { bot.Act(s); s.Step(Ctx); }
        Assert.NotNull(s.Encounter);
        s.Apply(Ctx, new OpenBossQuestion());
        for (int i = 0; i < 400; i++) s.Step(Ctx);
        var r = SimSnapshotMapper.Restore(Ctx, ThroughJson(Save(s)));
        Assert.True(r.IsRestored, r.Detail);
        Assert.True(r.State!.Encounter!.CardOpen);
        Assert.Equal(s.Encounter!.Elapsed, r.State.Encounter!.Elapsed);
        Assert.Equal(s.ComputeStateHash(), r.State.ComputeStateHash());

        // resolvido e ainda não liberado (resposta entre ticks)
        s.Apply(Ctx, new AnswerBossQuestion(0));
        Assert.True(s.Encounter!.IsResolved);
        r = SimSnapshotMapper.Restore(Ctx, ThroughJson(Save(s)));
        Assert.True(r.IsRestored, r.Detail);
        Assert.Equal(s.ComputeStateHash(), r.State!.ComputeStateHash());

        // layout menor: torres SEM SLOT continuam no estado
        s.ApplyLayout(TestLayouts.FromCenters(900f, 94f, 10f, 880f, new[] { 300f, 350f }));
        r = SimSnapshotMapper.Restore(Ctx, ThroughJson(Save(s)));
        Assert.True(r.IsRestored, r.Detail);
        Assert.Equal(s.Towers.Count, r.State!.Towers.Count);
        Assert.Equal(s.ComputeStateHash(), r.State.ComputeStateHash());
    }

    [Fact(DisplayName = "T6.9a sem eventos (StartEvents não chamado): ida e volta preserva o hash")]
    public void RoundTrip_Without_Events()
    {
        var s = SimState.CreateInitial(Content, SanityBot.FallbackLayout(), Rng.Seed(3, 4));
        var bot = new SanityBot(Ctx);
        for (int i = 0; i < 5000; i++) { bot.Act(s); s.Step(Ctx); }
        var r = SimSnapshotMapper.Restore(Ctx, ThroughJson(Save(s)));
        Assert.True(r.IsRestored, r.Detail);
        Assert.Equal(s.ComputeStateHash(), r.State!.ComputeStateHash());
        Assert.False(r.State.EventsEnabled);
    }

    [Fact(DisplayName = "CA-14.4 T6.9a ContentHash divergente não restaura; StateHash adulterado é detectado")]
    public void ContentHash_And_StateHash_Mismatch()
    {
        var s = NewGame();
        var bot = new SanityBot(Ctx);
        for (int i = 0; i < 1000; i++) { bot.Act(s); s.Step(Ctx); }
        var snap = Save(s);
        var r = SimSnapshotMapper.Restore(Ctx, snap with { GameContentHash = "outro" });
        Assert.Equal(RestoreStatus.ContentHashMismatch, r.Status);
        Assert.Null(r.State);
        r = SimSnapshotMapper.Restore(Ctx, snap with { StateHash = "0000000000000000" });
        Assert.Equal(RestoreStatus.StateHashMismatch, r.Status);
        r = SimSnapshotMapper.Restore(Ctx, snap with { Sim = snap.Sim with { Towers = [new TowerData { Id = 1, Type = "Nope" }] } });
        Assert.Equal(RestoreStatus.Invalid, r.Status);
    }

    [Fact(DisplayName = "T6.9a ToProfile: nível, XP, Reservas, Rng, estrelas, ledger e melhorias manuais; estrelas nunca diminuem")]
    public void ToProfile_Maps_Sim_Owned_Fields()
    {
        var s = NewGame();
        s.RestoreStageProgress(2, 40, 2);
        s.Level = 7; s.Xp = 99; s.Reservas = 3; s.ManualUpgrades = 5;
        var baseP = ProfileData.Default with
        {
            Region = "east-us",
            Stages = [new StageProgressData { StageId = 2, Unlocked = true, Stars = 3 }, new StageProgressData { StageId = 9, Unlocked = true, Stars = 1 }],
        };
        var p = SimSnapshotMapper.ToProfile(s, Ctx, baseP);
        Assert.Equal(7, p.Level);
        Assert.Equal(99, p.Xp);
        Assert.Equal(3, p.Reservas);
        Assert.Equal(5, p.Counters.ManualUpgrades);
        Assert.Equal(s.Rng.State, p.Rng!.State);
        Assert.Equal("east-us", p.Region);
        Assert.Equal(3, p.Stages.Single(x => x.StageId == 2).Stars);
        Assert.Contains(p.Stages, x => x.StageId == 9);
        Assert.Equal([20, 40], p.Stages.Single(x => x.StageId == 2).ChefesVencidos);
    }
}
