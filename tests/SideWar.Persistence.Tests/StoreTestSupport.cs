using System.Text;
using System.Text.Json;
using SideWar.Persistence.Profile;
using SideWar.Persistence.Session;

namespace SideWar.Persistence.Tests;

internal static class StoreTestSupport
{
    public static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Envelope com hash correto do payload, em qualquer schema/versão (inclusive v0 para migrações).</summary>
    public static byte[] Envelope(string payloadJson, string schema, int version, string? hash = null)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        hash ??= JsonEnvelope.ComputeHash(doc.RootElement);
        return Encoding.UTF8.GetBytes($$"""{"schema":"{{schema}}","schemaVersion":{{version}},"appVersion":"0.1.0","savedAtUtc":"2026-11-10T12:00:00Z","contentHash":"{{hash}}","payload":{{payloadJson}}}""");
    }

    public static string Json(ProfileData p) { using var b = new MemoryStream(); using (var w = new Utf8JsonWriter(b)) ProfileSerializer.Write(w, p); return Encoding.UTF8.GetString(b.ToArray()); }
    public static string Json(OfflineSnapshot s) { using var b = new MemoryStream(); using (var w = new Utf8JsonWriter(b)) SessionSerializer.Write(w, s); return Encoding.UTF8.GetString(b.ToArray()); }

    public static int SchemaVersionOnDisk(byte[] file)
    {
        using var d = JsonDocument.Parse(file);
        return d.RootElement.GetProperty("schemaVersion").GetInt32();
    }

    public static ProfileData FullProfile() => new()
    {
        Level = 14, Xp = 1_234_567_890_123, Reservas = 38, TotalKills = 9_001, PlaySeconds = 12345.678,
        Seed = ulong.MaxValue, Rng = new RngData(0xDEADBEEFCAFEBABEUL, 0x1234567890ABCDEFUL),
        Stages = [new StageProgressData { StageId = 1, Unlocked = true, Stars = 3, Attempt = 2, ChefesVencidos = [20, 40, 60] },
                  new StageProgressData { StageId = 2, Unlocked = true, Stars = 1, Attempt = 1, ChefesVencidos = [20] }],
        Region = "east-us", UnlockedRegions = ["brazil-south", "east-us"], UnlockedTowers = ["bolt", "frost", "cannon", "scaleset"],
        Missions = new MissionsData
        {
            DateLocal = "2026-11-10",
            Items = [new MissionProgressData { Id = "answer-boss", State = MissionStateData.Completed, Progress = 3 },
                     new MissionProgressData { Id = "stay-present", State = MissionStateData.Collected, Progress = 60 },
                     new MissionProgressData { Id = "defeat-enemies", State = MissionStateData.InProgress, Progress = 117 }],
        },
        Ledger = [new LedgerEntryData { Stage = 1, Attempt = 1, Wave = 20, QuestionId = "q-07", OptionIndex = 2, Correct = true, BonusConsumed = true },
                  new LedgerEntryData { Stage = 1, Attempt = 1, Wave = 40, QuestionId = "q-11", OptionIndex = null, Correct = false }],
        DrawnQuestions = [new DrawnQuestionsData { Stage = 1, Attempt = 2, QuestionIds = ["q-01", "q-07", "q-11"] }],
        CollectLedger = [new CollectLedgerEntryData { ReportId = 5, QuestionId = "q-02", OptionIndex = 0, Correct = true }],
        Counters = new EventCountersData { Answers = 7, PeaksScaled = 4, CratesCollected = 12, Kills = 9_001, ManualUpgrades = 33,
            PresentMinutesToday = 42, PresentMinutesDate = "2026-11-10", EventClockSeconds = 1234.5 },
    };

    public static OfflineSnapshot FullSession(string contentHash = "content-A") => new()
    {
        SavedUtc = new DateTime(2026, 11, 10, 11, 59, 30, DateTimeKind.Utc),
        GameContentHash = contentHash,
        StateHash = "0123456789abcdef",
        Presence = new PresenceData { State = PresenceStateData.Ausente, LastInteractionUtc = new DateTime(2026, 11, 10, 11, 40, 0, DateTimeKind.Utc), PresentSeconds = 17.25, AutoScaleAccumSeconds = 611.5 },
        AutoScaleOn = false,
        Pending = new PendingData
        {
            ReportId = 9, Credits = 186_000_000_000_000, Xp = 4_200, Quedas = 0, OndaDe = 22, OndaPara = 37, TempoS = 11520.5,
            Melhorias = [new UpgradeLineData("bolt", 12, 14), new UpgradeLineData("frost", 8, 9)],
            Perdas = new LossesData { Picos = 16, Caixas = 40, Chefes = 1 },
        },
        CardState = CardStateData.Recolhido,
        Layout = new LayoutData { SpawnX = 1500.5f, LeakX = 94f, CoreX = 40f, PortalX = 1560f, Width = 1920, Host = "Band", FreeZones = [new FreeZoneData(100, 400), new FreeZoneData(500, 900)], MigratedSlots = true },
        Sim = new SimData
        {
            Tick = 18234, Stage = 3, Attempt = 2, Falls = 1, Wave = 17, OndaMax = 21, ChefesVencidos = [20],
            Credits = 312, Carry = 0.123456789012345, Xp = 40, Level = 5, BaseHp = 87, MaxBaseHp = 120, Reservas = 8, Region = "east-us",
            UnlockedTowers = ["bolt", "frost"], Rng = new RngData(ulong.MaxValue - 3, 0x1357UL | 1), NextId = 4411,
            WaveState = new WaveStateData { Queue = [new QueuedSpawnData(0.5f, "grunt"), new QueuedSpawnData(1.25f, "runner")], Cursor = 12, Clock = 8.4f, Rest = 0.7f, SpawnIndex = 12 },
            Towers = [new TowerData { Id = 5, Type = "bolt", Anchor = "End", OffsetDip = 120, Placed = true, Level = 3, Cooldown = 0.12f, Mode = "First", Hits = 2 },
                      new TowerData { Id = 6, Type = "frost", Anchor = "Start", OffsetDip = 0, Placed = false, Level = 1, Cooldown = 0f, Mode = "Strong", Hits = 0 }], // SEM POSIÇÃO
            Enemies = [new EnemyData { Id = 4402, Kind = "tank", X = 812.5f, Hp = 210.5, MaxHp = 480, Speed = 30.1f, SpawnSpeed = 33.2f, Armor = 2, Chilled = true, LastHit = "Frost" }],
        },
        Events = new EventsData
        {
            Enabled = true, BossHold = true, PeakQueue = 3, StageAttempt = 2,
            Scheduler = new SchedulerData { Peak = "Offer", PeakTimer = 12.5, PeakDue = true, ExtrasPending = 4, NextExtraIn = 7.5, CrateActive = true, CrateTimer = 22.25, CrateX = 700, CrateCountdown = 200.5, PeaksClicked = 2, CratesCollected = 1 },
            Encounter = new BossEncounterData { Stage = 3, Attempt = 2, Wave = 20, QuestionId = "q-07", ElapsedS = 13.5, CardOpen = true },
            Ledger = [new LedgerEntryData { Stage = 1, Attempt = 1, Wave = 20, QuestionId = "q-07", OptionIndex = 2, Correct = true, BonusConsumed = true }],
            DrawnQuestions = [new DrawnQuestionsData { Stage = 3, Attempt = 2, QuestionIds = ["q-07"] }],
        },
    };
}
