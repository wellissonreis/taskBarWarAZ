using System.Text;
using System.Text.Json;
using SideWar.Core.Content.Td;
using SideWar.Core.Td;
using SideWar.Persistence.Migrations;
using SideWar.Persistence.Profile;
using Xunit;
using static SideWar.Persistence.Tests.StoreTestSupport;

namespace SideWar.Persistence.Tests;

public class TdProfileStoreTests
{
    private const string D = "/data";
    private const string F = "/data/profile-td.json";
    private static readonly TdMetaDef Meta = TdContentLoader.LoadEmbedded().Meta;

    private static (FakeFileSystem fs, TdProfileStore st) Make() { var fs = new FakeFileSystem(); return (fs, new TdProfileStore(fs, D, utcNow: () => Now)); }
    private static string[] Corrupt(FakeFileSystem fs) => fs.Files.Keys.Where(k => k.StartsWith("/data/corrupt/", StringComparison.Ordinal)).ToArray();
    private static string J(TdProfileData p) { using var b = new MemoryStream(); using (var w = new Utf8JsonWriter(b)) TdProfileSerializer.Write(w, p); return Encoding.UTF8.GetString(b.ToArray()); }

    private static TdProfileData Full() => new()
    {
        SealsEarned = 9, MigratedSeals = 4, IdleImportDone = true,
        Stages = [new TdStageProgressData { StageId = 1, Unlocked = true, Stars = 3, ChallengeCompleted = true, Crown = true, Medals = 15, BestScore = 4123 },
                  new TdStageProgressData { StageId = 2, Unlocked = true, Stars = 1, Crown = false, Medals = 5, BestScore = 2987 }],
        PurchasedNodes = ["defense.damage1", "infrastructure.credits"],
        UnlockedTowers = ["bolt", "frost", "cannon", "balancer"],
        InfiniteRuns = [new TdInfiniteRunData { Seed = ulong.MaxValue, MaxWave = 41 }, new TdInfiniteRunData { Seed = 7, MaxWave = 12 }],
    };

    [Fact]
    public void LegacyCreditIsIdempotentAfterReloadAndRecordOnlyIncreases()
    {
        var (_, store) = Make();
        var first = TdProfileRules.RecordLegacy(Full(), "run-1", 12);
        Assert.Equal(2, first.LegacyTotal);
        Assert.Equal(12, first.BestRunWaves);
        Assert.True(store.Save(first));
        var reloaded = store.Load().Profile;
        Assert.Same(reloaded, TdProfileRules.RecordLegacy(reloaded, "run-1", 12));
        var second = TdProfileRules.RecordLegacy(reloaded, "run-2", 5);
        Assert.Equal(3, second.LegacyTotal);
        Assert.Equal(12, second.BestRunWaves);
        Assert.Equal(2, second.RunsPlayed);
    }

    [Fact]
    public void Migration_V3ToV4_PreservesProgressAndRoundTripsLegacy()
    {
        var (fs, store) = Make();
        var payload = System.Text.Json.Nodes.JsonNode.Parse(J(Full()))!.AsObject();
        foreach (var key in new[] { "legacyTotal", "bestRunWaves", "legacyLastRun", "runsPlayed", "reputation" }) payload.Remove(key);
        fs.Files[F] = Envelope(payload.ToJsonString(), TdProfileData.Schema, 3);
        var p = store.Load().Profile;
        Assert.Equal(J(TdProfileSerializer.Sanitize(Full() with { Reputation = Full().Stages.Sum(x => x.BestScore) })), J(p)); // v5: reputação inicial = soma dos recordes
        Assert.Equal(0, p.LegacyTotal);
        Assert.Equal(0, p.BestRunWaves);
        Assert.Equal("", p.LegacyLastRun);
        Assert.Equal(5, SchemaVersionOnDisk(fs.Files[F]));
        p = p with { LegacyTotal = 12, BestRunWaves = 37, LegacyLastRun = "abc-def", RunsPlayed = 4 };
        Assert.True(store.Save(p));
        Assert.Equal(J(p), J(store.Load().Profile));
    }

    [Fact]
    public void Load_NoFiles_ReturnsNewProfileWithStage1Unlocked()
    {
        var (_, st) = Make();
        var r = st.Load();
        Assert.Equal(TdProfileSource.Defaults, r.Source);
        Assert.Null(r.RejectReason);
        Assert.Equal(0, r.Profile.SealsEarned);
        Assert.True(r.Profile.Stages.Single().Unlocked);
    }

    [Fact]
    public void SaveLoad_RoundTrip_AllFields()
    {
        var (fs, st) = Make();
        Assert.True(st.Save(Full()));
        Assert.Equal(5, SchemaVersionOnDisk(fs.Files[F]));
        var r = st.Load();
        Assert.Equal(TdProfileSource.File, r.Source);
        Assert.Equal(J(TdProfileSerializer.Sanitize(Full())), J(r.Profile));
        Assert.Equal(ulong.MaxValue, r.Profile.InfiniteRuns.Single(x => x.MaxWave == 41).Seed);
    }

    [Fact]
    public void Save_IsDeterministic_SameBytesForSameProfile()
    {
        var (_, st) = Make();
        Assert.Equal(st.Encode(Full()), st.Encode(Full()));
    }

    [Fact]
    public void Save_SecondTime_KeepsBakAndLoadsLatest()
    {
        var (fs, st) = Make();
        st.Save(Full());
        st.Save(Full() with { SealsEarned = 20 });
        Assert.True(fs.Files.ContainsKey(F + ".bak"));
        Assert.Equal(20, st.Load().Profile.SealsEarned);
    }

    [Theory]
    [InlineData("truncado")]
    [InlineData("json-invalido")]
    [InlineData("versao-futura")]
    [InlineData("hash-divergente")]
    [InlineData("schema-errado")]
    [InlineData("tipo-errado")]
    [InlineData("acima-de-1mb")]
    public void Load_CorruptMain_QuarantinesAndUsesBackup(string kind)
    {
        var (fs, st) = Make();
        st.Save(Full() with { SealsEarned = 3 });
        st.Save(Full() with { SealsEarned = 4 }); // .bak = 3
        var good = fs.Files[F];
        fs.Files[F] = kind switch
        {
            "truncado" => good[..(good.Length / 2)],
            "json-invalido" => Encoding.UTF8.GetBytes("{ nope"),
            "versao-futura" => Envelope("{}", TdProfileData.Schema, 99),
            "hash-divergente" => Envelope("{}", TdProfileData.Schema, 1, "00"),
            "schema-errado" => Envelope("{}", "sidewar.profile", 1),
            "tipo-errado" => Envelope("""{"sealsEarned":"muitos"}""", TdProfileData.Schema, 1),
            _ => new byte[JsonEnvelope.MaxReadBytes + 1],
        };
        var r = st.Load();
        Assert.Equal(TdProfileSource.Backup, r.Source);
        Assert.Equal(3, r.Profile.SealsEarned);
        Assert.Single(Corrupt(fs));
    }

    [Fact]
    public void Load_BothCorrupt_ReturnsNewProfileWithRejectReason()
    {
        var (fs, st) = Make();
        fs.Files[F] = Encoding.UTF8.GetBytes("lixo");
        fs.Files[F + ".bak"] = Encoding.UTF8.GetBytes("lixo");
        var r = st.Load();
        Assert.Equal(TdProfileSource.Defaults, r.Source);
        Assert.NotNull(r.RejectReason);
        Assert.Equal(2, Corrupt(fs).Length);
    }

    [Fact]
    public void Load_OutOfRangeFields_AreSanitized()
    {
        var (fs, st) = Make();
        fs.Files[F] = Envelope("""{"sealsEarned":-5,"migratedSeals":99,"stages":[{"stageId":2,"stars":9},{"stageId":2,"stars":1},{"stageId":0,"stars":1}],"purchasedNodes":["a","a",""],"infiniteRuns":[{"seed":5,"maxWave":-3}]}""", TdProfileData.Schema, 1);
        var p = st.Load().Profile;
        Assert.Equal(0, p.SealsEarned);
        Assert.Equal(0, p.MigratedSeals);
        Assert.Equal([1, 2], p.Stages.Select(s => s.StageId));
        Assert.Equal(3, p.Stages[1].Stars);
        Assert.True(p.Stages[1].Unlocked);
        Assert.Empty(p.PurchasedNodes);
        Assert.Equal(0, p.InfiniteRuns[0].MaxWave);
    }

    [Fact]
    public void Rules_RecordStage_StarsNeverDecrease_SealsOnlyForNewStarsAndFirstChallenge()
    {
        var p = TdProfileData.Default;
        p = TdProfileRules.RecordStageResult(p, Meta, 1, 2, false, 6);
        Assert.Equal(2, p.SealsEarned);
        p = TdProfileRules.RecordStageResult(p, Meta, 1, 1, true, 6); // sem estrela nova, +1 desafio
        Assert.Equal(3, p.SealsEarned);
        Assert.Equal(2, p.Stages[0].Stars);
        p = TdProfileRules.RecordStageResult(p, Meta, 1, 3, true, 6); // +1 estrela, desafio já contado
        Assert.Equal(4, p.SealsEarned);
        Assert.True(p.Stages.Single(s => s.StageId == 2).Unlocked);
    }

    [Fact]
    public void Rules_Purchase_SpendsSealsAndRejectsUnaffordableOrRepeated()
    {
        var p = TdProfileData.Default with { SealsEarned = 3 };
        p = TdProfileRules.TryPurchase(p, Meta, "defense.damage1");  // custo 1
        p = TdProfileRules.TryPurchase(p, Meta, "defense.damage1");  // repetido
        Assert.Equal(["defense.damage1"], p.PurchasedNodes);
        Assert.Equal(2, TdProfileRules.SealsAvailable(p, Meta));
        var same = TdProfileRules.TryPurchase(p, Meta, "infrastructure.startingCredits3"); // custo 4 > 2
        Assert.Equal(p, same);
        Assert.Equal(p, TdProfileRules.TryPurchase(p, Meta, "inexistente"));
    }

    // ---- Migração do save idle (DP-28.4, T-28.8) ----

    private static int TestFormula(int w) => w / 20; // fórmula de teste; a de produção é decisão pendente

    [Fact]
    public void IdleMaxWave_UsesHighestBossWaveDefeated()
    {
        Assert.Equal(60, IdleProfileMigrator.IdleMaxWave(FullProfile()));
        Assert.Equal(0, IdleProfileMigrator.IdleMaxWave(ProfileData.Default));
    }

    [Fact]
    public void Migrate_ConvertsMaxWaveToSeals_AndDropsIdleFields()
    {
        var t = IdleProfileMigrator.Migrate(FullProfile(), TestFormula); // onda 60 → 3
        Assert.Equal(3, t.SealsEarned);
        Assert.Equal(3, t.MigratedSeals);
        Assert.True(t.IdleImportDone);
        Assert.Empty(t.PurchasedNodes);
        Assert.Empty(t.InfiniteRuns);
        Assert.Single(t.Stages);
        Assert.DoesNotContain("reservas", J(t), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mission", J(t), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(7, 7)]
    [InlineData(9999, IdleProfileMigrator.MaxInitialSeals)]
    public void Migrate_ClampsFormulaResult(int formula, int expected) =>
        Assert.Equal(expected, IdleProfileMigrator.Migrate(FullProfile(), _ => formula).SealsEarned);

    [Fact]
    public void LoadOrMigrate_ImportsIdleOnce_LeavesIdleFileUntouched()
    {
        var fs = new FakeFileSystem();
        var idle = new ProfileStore(fs, D, utcNow: () => Now);
        idle.Save(FullProfile());
        var idleBytes = fs.Files["/data/profile.json"];
        var td = new TdProfileStore(fs, D, utcNow: () => Now);

        var r = IdleProfileMigrator.LoadOrMigrate(td, () => idle.Load(), TestFormula);
        Assert.Equal(3, r.Profile.SealsEarned);
        Assert.True(fs.Files.ContainsKey(F));
        Assert.Equal(idleBytes, fs.Files["/data/profile.json"]);

        // segunda carga: lê o perfil TD, não reimporta (mesmo com fórmula diferente)
        var r2 = IdleProfileMigrator.LoadOrMigrate(td, () => idle.Load(), _ => 30);
        Assert.Equal(TdProfileSource.File, r2.Source);
        Assert.Equal(3, r2.Profile.SealsEarned);
    }

    [Fact]
    public void LoadOrMigrate_NoIdleSave_StartsFresh()
    {
        var fs = new FakeFileSystem();
        var idle = new ProfileStore(fs, D, utcNow: () => Now);
        var td = new TdProfileStore(fs, D, utcNow: () => Now);
        var r = IdleProfileMigrator.LoadOrMigrate(td, () => idle.Load(), TestFormula);
        Assert.Equal(0, r.Profile.SealsEarned);
        Assert.True(r.Profile.IdleImportDone);
        Assert.True(fs.Files.ContainsKey(F));
    }

    [Fact]
    public void LoadOrMigrate_CorruptIdleSave_NeverBlocks()
    {
        var fs = new FakeFileSystem();
        fs.Files["/data/profile.json"] = Encoding.UTF8.GetBytes("lixo");
        var idle = new ProfileStore(fs, D, utcNow: () => Now);
        var td = new TdProfileStore(fs, D, utcNow: () => Now);
        var r = IdleProfileMigrator.LoadOrMigrate(td, () => idle.Load(), TestFormula);
        Assert.Equal(0, r.Profile.SealsEarned);
    }

    [Fact]
    public void LoadOrMigrate_CorruptTdProfile_RestartsWithoutReimport()
    {
        var fs = new FakeFileSystem();
        var idle = new ProfileStore(fs, D, utcNow: () => Now);
        idle.Save(FullProfile());
        fs.Files[F] = Encoding.UTF8.GetBytes("lixo");
        var td = new TdProfileStore(fs, D, utcNow: () => Now);
        var r = IdleProfileMigrator.LoadOrMigrate(td, () => idle.Load(), TestFormula);
        Assert.Equal(TdProfileSource.Defaults, r.Source);
        Assert.NotNull(r.RejectReason);
        Assert.Equal(0, r.Profile.SealsEarned);
        Assert.Single(Corrupt(fs));
    }

    // ---------------------------------------------------------------- TD.13: coroa, medalhas, recorde, migração v1 → v2

    private const string V1Payload = """
        {"sealsEarned":9,"migratedSeals":4,"idleImportDone":true,
         "stages":[{"stageId":1,"unlocked":true,"stars":3,"challengeCompleted":true},{"stageId":2,"unlocked":true,"stars":1,"challengeCompleted":false}],
         "purchasedNodes":["defense.damage1"],"unlockedTowers":["bolt"],"infiniteRuns":[{"seed":7,"maxWave":12}]}
        """;

    [Fact]
    public void Migration_V1ToV3_KeepsEverythingAndAddsDefaults_AndWritesBackV3()
    {
        var (fs, st) = Make();
        fs.Files[F] = Envelope(V1Payload, TdProfileData.Schema, 1);
        var r = st.Load();
        Assert.Equal(TdProfileSource.File, r.Source);
        Assert.Null(r.RejectReason);
        Assert.Empty(Corrupt(fs));
        var p = r.Profile;
        Assert.Equal(9, p.SealsEarned);
        Assert.Equal(4, p.MigratedSeals);
        Assert.Equal(3, p.Stages[0].Stars);
        Assert.True(p.Stages[0].ChallengeCompleted);
        Assert.Equal(1, p.Stages[1].Stars);
        Assert.All(p.Stages, s => { Assert.False(s.Crown); Assert.Equal(0, s.Medals); Assert.Equal(0, s.BestScore); });
        Assert.Equal(["defense.damage1"], p.PurchasedNodes);
        Assert.Equal((ulong)7, p.InfiniteRuns.Single().Seed);
        Assert.Equal(5, SchemaVersionOnDisk(fs.Files[F])); // gravado de volta na versão nova
        Assert.Equal(J(p), J(st.Load().Profile));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Migration_LegacyProfile_RefundsObsoletePurchases_AndIsIdempotent(int version)
    {
        var (fs, store) = Make();
        var payload = J(Full() with
        {
            SealsEarned = 30,
            PurchasedNodes = ["defense.damage1", "infrastructure.credits", "infrastructure.sell75",
                "infrastructure.slots1", "infrastructure.slots2", "infrastructure.slots2", "unknown", "bolt.damage1"],
        });
        fs.Files[F] = Envelope(payload, TdProfileData.Schema, version);
        var result = store.Load();
        Assert.Equal(TdProfileSource.File, result.Source);
        Assert.Null(result.RejectReason);
        var p = result.Profile;
        Assert.Equal(30, p.SealsEarned);
        Assert.Equal(4, p.MigratedSeals);
        Assert.True(p.IdleImportDone);
        Assert.Equal(J(TdProfileSerializer.Sanitize(Full() with { SealsEarned = 30, PurchasedNodes = ["defense.damage1", "infrastructure.credits"] })), J(p));
        Assert.Equal(28, TdProfileRules.SealsAvailable(p, Meta)); // antes: 19; reembolso integral 2+3+4
        Assert.Equal(5, SchemaVersionOnDisk(fs.Files[F]));
        Assert.Equal(J(p), J(store.Load().Profile));
        Assert.True(store.Save(p));
        Assert.Equal(J(p), J(store.Load().Profile));
        Assert.Empty(Corrupt(fs));
        foreach (var id in new[] { "infrastructure.startingCredits1", "infrastructure.startingCredits2", "infrastructure.startingCredits3" })
            p = TdProfileRules.TryPurchase(p, Meta, id);
        Assert.Equal(19, TdProfileRules.SealsAvailable(p, Meta));
        var content = TdContentLoader.LoadEmbedded();
        var match = TdMatchState.Create(content, 1, SideWar.Core.Simulation.Rng.Seed(42, 54), p.PurchasedNodes);
        Assert.Equal(content.Stages.Stages[0].StartingCredits + content.Runes.CreditSources.StartingCreditsBonus + 130, match.Credits);
        Assert.Equal(0, match.AwakenedCount); // a teia começa vazia (TD.29a)
        Assert.True(store.Save(p));
        Assert.Equal(J(p), J(store.Load().Profile));
    }

    [Fact]
    public void V3_SanitizesUnknownAndObsoleteNodes_WithoutPersistingRunes()
    {
        var (fs, store) = Make();
        fs.Files[F] = Envelope("""{"sealsEarned":12,"purchasedNodes":["unknown","infrastructure.sell75","infrastructure.startingCredits1","infrastructure.startingCredits1"],"ownedRunes":["bolt.damage1"]}""", TdProfileData.Schema, 3);
        var p = store.Load().Profile;
        Assert.Equal(["infrastructure.startingCredits1"], p.PurchasedNodes);
        Assert.Equal(10, TdProfileRules.SealsAvailable(p, Meta));
        Assert.True(store.Save(p));
        Assert.DoesNotContain("ownedRunes", Encoding.UTF8.GetString(fs.Files[F]));
        Assert.Equal(J(p), J(store.Load().Profile));
    }

    private static TdMatchResult Win(int stars, int lost, bool crown, TdMedals medals, int score) =>
        new(MatchPhase.Vitoria, stars, lost, crown, medals, score);

    [Fact]
    public void RecordMatch_CrownAndMedalsGrantBonusSealsOnlyOnce_AndBestScoreOnlyRises()
    {
        var p = TdProfileData.Default;
        var all = TdMedals.SemVender | TdMedals.SemHabilidades | TdMedals.Pressa | TdMedals.Desafio;
        p = TdProfileRules.RecordMatch(p, Meta, 1, Win(3, 0, true, all, 4000), 6, out var o1);
        // 3 estrelas (3) + desafio (1) + coroa (2) + 3 medalhas de feito (3)
        Assert.Equal(3 * Meta.SealsPerStar + Meta.SealsPerChallenge + TdProfileRules.SealsPerCrown + 3 * TdProfileRules.SealsPerMedal, p.SealsEarned);
        Assert.True(o1.NewRecord); Assert.True(o1.NewCrown);
        Assert.Equal(TdProfileRules.SealsPerCrown + 3 * TdProfileRules.SealsPerMedal, o1.BonusSeals);
        var st = p.Stages.Single(s => s.StageId == 1);
        Assert.True(st.Crown); Assert.Equal(15, st.Medals); Assert.Equal(4000, st.BestScore);

        int seals = p.SealsEarned;
        p = TdProfileRules.RecordMatch(p, Meta, 1, Win(3, 0, true, all, 3500), 6, out var o2); // pior, nada novo
        Assert.Equal(seals, p.SealsEarned);
        Assert.False(o2.NewRecord); Assert.False(o2.NewCrown); Assert.Equal(0, o2.BonusSeals);
        Assert.Equal(4000, p.Stages.Single(s => s.StageId == 1).BestScore);

        p = TdProfileRules.RecordMatch(p, Meta, 1, Win(3, 0, true, all, 4500), 6, out var o3); // recorde sem selos
        Assert.True(o3.NewRecord);
        Assert.Equal(seals, p.SealsEarned);
        Assert.Equal(4500, p.Stages.Single(s => s.StageId == 1).BestScore);
    }

    [Fact]
    public void RecordMatch_NewMedalLater_PaysOnlyTheNewOne()
    {
        var p = TdProfileRules.RecordMatch(TdProfileData.Default, Meta, 1, Win(1, 5, false, TdMedals.SemVender, 1500), 6, out _);
        int seals = p.SealsEarned; // 1 estrela + 1 medalha
        Assert.Equal(Meta.SealsPerStar + TdProfileRules.SealsPerMedal, seals);
        p = TdProfileRules.RecordMatch(p, Meta, 1, Win(1, 5, false, TdMedals.SemVender | TdMedals.Pressa, 1800), 6, out var o);
        Assert.Equal(seals + TdProfileRules.SealsPerMedal, p.SealsEarned);
        Assert.Equal(TdMedals.Pressa, o.NewMedals);
    }

    [Fact]
    public void Rank_FollowsReputation_AndTotalScoreStaysTheSumOfStageRecords()
    {
        var p = new TdProfileData
        {
            Stages = [new TdStageProgressData { StageId = 1, Unlocked = true, BestScore = 4000 }, new TdStageProgressData { StageId = 2, Unlocked = true, BestScore = 4100 },
                      new TdStageProgressData { StageId = 3, Unlocked = true, BestScore = 4000 }],
        };
        Assert.Equal(12_100, TdProfileRules.TotalScore(p));
        Assert.Equal(TdRankLevel.Estagiario, TdProfileRules.Rank(p).Current); // a patente vem da reputação, não dos recordes
        Assert.Equal(TdRankLevel.Pleno, TdProfileRules.Rank(p with { Reputation = 12_100 }).Current);
        Assert.Equal(TdRankLevel.Estagiario, TdProfileRules.Rank(TdProfileData.Default).Current);
    }

    [Fact]
    public void Sanitize_ClampsNewFields()
    {
        var p = TdProfileSerializer.Sanitize(new TdProfileData
        {
            Stages = [new TdStageProgressData { StageId = 1, Unlocked = true, Medals = 0xFF, BestScore = -5 }, new TdStageProgressData { StageId = 2, BestScore = int.MaxValue }],
        });
        Assert.Equal(15, p.Stages[0].Medals);
        Assert.Equal(0, p.Stages[0].BestScore);
        Assert.Equal(TdProfileSerializer.MaxScore, p.Stages[1].BestScore);
    }
}

public class TdReputationProfileTests
{
    [Fact]
    public void ApplyReputation_floors_at_zero_and_rank_goes_down_and_up()
    {
        var p = TdProfileData.Default with { Reputation = 12_100 };
        Assert.Equal(TdRankLevel.Pleno, TdProfileRules.Rank(p).Current);
        var down = TdProfileRules.ApplyReputation(p, -300);
        Assert.Equal(11_800, down.Reputation);
        Assert.Equal(TdRankLevel.Junior, TdProfileRules.Rank(down).Current); // rebaixado
        Assert.Equal(TdRankLevel.Pleno, TdProfileRules.Rank(TdProfileRules.ApplyReputation(down, 300)).Current); // e sobe de novo
        Assert.Equal(0, TdProfileRules.ApplyReputation(p, -1_000_000).Reputation);
        Assert.Same(p, TdProfileRules.ApplyReputation(p, 0));
    }

    [Fact]
    public void Migration_V4ToV5_seeds_reputation_from_the_stage_records()
    {
        var fs = new FakeFileSystem();
        var store = new TdProfileStore(fs, "/data", utcNow: () => Now);
        var payload = """{"sealsEarned":1,"stages":[{"stageId":1,"unlocked":true,"stars":3,"bestScore":4000},{"stageId":2,"unlocked":true,"stars":1,"bestScore":2500}]}""";
        fs.Files["/data/profile-td.json"] = Envelope(payload, TdProfileData.Schema, 4);
        var p = store.Load().Profile;
        Assert.Equal(6_500, p.Reputation);
        Assert.Equal(TdRankLevel.Junior, TdProfileRules.Rank(p).Current);
        Assert.Equal(5, SchemaVersionOnDisk(fs.Files["/data/profile-td.json"]));
    }
}
