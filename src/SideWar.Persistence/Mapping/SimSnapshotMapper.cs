using System.Globalization;
using SideWar.Core.Events;
using SideWar.Core.Simulation;
using SideWar.Persistence.Profile;
using SideWar.Persistence.Session;

namespace SideWar.Persistence.Mapping;

public enum RestoreStatus
{
    Restored,
    /// <summary>GameContentHash da sessão ≠ ContentHash atual (RF-14.8 passo 5): não restaurar; o perfil é mantido (CA-14.4).</summary>
    ContentHashMismatch,
    /// <summary>StateHash recalculado ≠ gravado (RF-14.6/14.9): sessão corrompida; chamar <c>SessionStore.RejectRestored</c>.</summary>
    StateHashMismatch,
    /// <summary>Layout, enum ou pergunta inválidos no snapshot.</summary>
    Invalid,
}

/// <param name="State">Preenchido só com <see cref="RestoreStatus.Restored"/>.</param>
/// <param name="Region">Id da região (a simulação só guarda o multiplicador de HP); vem do snapshot.</param>
public sealed record RestoreResult(RestoreStatus Status, SimState? State, string? Detail = null,
    string Region = ProfileData.DefaultRegion, IReadOnlyList<string>? UnlockedTowers = null)
{
    public bool IsRestored => Status == RestoreStatus.Restored;
}

/// <summary>
/// Mapeamento puro SimState ⇄ <see cref="ProfileData"/>/<see cref="OfflineSnapshot"/> (SDD-14 RF-14.5/14.6; SDD-25 RF-25.11; T6.9a).
/// Sem I/O, relógio nem gatilhos. Decisões:
/// (1) a sessão é autossuficiente para o StateHash (tabela por fase, ledger e sorteadas duplicados do perfil), pois perfil e
/// sessão são gravados em momentos diferentes; (2) no restore o <c>Rng</c> da sessão vence o do perfil (o perfil só alimenta
/// partida nova); (3) ContentHash divergente: não restaura (o <c>SessionStore.Load</c> já descarta; aqui é a segunda trava).
/// </summary>
public static class SimSnapshotMapper
{
    // ---------------------------------------------------------------- SimState → DTO

    public static string FormatStateHash(ulong hash) => hash.ToString("x16", CultureInfo.InvariantCulture);

    public static OfflineSnapshot ToSession(SimState s, SimContext ctx, DateTime savedUtc, PresenceData presence,
        ProfileData profile, bool autoScaleOn = true, PendingData? pending = null, CardStateData cardState = CardStateData.Aberto)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(profile);
        var layout = s.Layout;
        return new OfflineSnapshot
        {
            SavedUtc = savedUtc.ToUniversalTime(),
            GameContentHash = ctx.Content.ContentHash,
            StateHash = FormatStateHash(s.ComputeStateHash()),
            Presence = presence,
            AutoScaleOn = autoScaleOn,
            Pending = pending ?? new PendingData(),
            CardState = cardState,
            Layout = new LayoutData
            {
                SpawnX = layout.SpawnX, LeakX = layout.LeakX, CoreX = layout.CoreX, PortalX = layout.PortalX,
                Width = layout.Width, Host = layout.Host.ToString(), MigratedSlots = s.MigratedSlotsPending,
                FreeZones = layout.FreeZones.ToArray().Select(z => new FreeZoneData(z.X0, z.X1)).ToArray(),
            },
            Sim = ToSimData(s, ctx, profile),
            Events = ToEventsData(s),
        };
    }

    /// <summary>
    /// Perfil a gravar: sobrepõe ao <paramref name="baseProfile"/> o que a simulação possui (nível, XP, Reservas, estado do Rng,
    /// estrelas/chefes por fase, ledger, sorteadas e melhorias manuais). Estrelas nunca diminuem. O resto (missões, região,
    /// contadores) permanece do perfil base.
    /// </summary>
    public static ProfileData ToProfile(SimState s, SimContext ctx, ProfileData baseProfile)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(baseProfile);
        var simStages = BuildStageTable(s, ctx).ToDictionary(x => x.StageId);
        var merged = new List<StageProgressData>();
        foreach (var b in baseProfile.Stages)
        {
            if (simStages.Remove(b.StageId, out var m))
                merged.Add(m with { Stars = Math.Max(b.Stars, m.Stars), Unlocked = b.Unlocked || m.Unlocked });
            else merged.Add(b);
        }
        merged.AddRange(simStages.Values);
        merged.Sort((a, b) => a.StageId.CompareTo(b.StageId));
        return baseProfile with
        {
            Level = Math.Max(1, s.Level),
            Xp = s.Xp,
            Reservas = s.Reservas,
            Rng = new RngData(s.Rng.State, s.Rng.Inc),
            Stages = merged,
            Ledger = LedgerData(s),
            DrawnQuestions = DrawnData(s),
            Counters = baseProfile.Counters with { ManualUpgrades = s.ManualUpgrades },
        };
    }

    private static SimData ToSimData(SimState s, SimContext ctx, ProfileData profile)
    {
        var towers = new List<TowerData>();
        for (int i = 0; i < s.Towers.Capacity; i++)
        {
            if (!s.Towers.IsAlive(i)) continue;
            ref readonly var t = ref s.Towers.At(i);
            towers.Add(new TowerData { Id = t.Id, Type = t.Type.ToString(), Anchor = t.Anchor.ToString(), OffsetDip = t.OffsetDip, Placed = t.Placed, Level = t.Level, Cooldown = t.Cooldown, Mode = t.Mode.ToString(), Hits = t.Hits });
        }
        towers.Sort((a, b) => a.Id.CompareTo(b.Id));
        var enemies = new List<EnemyData>();
        for (int i = 0; i < s.Enemies.Capacity; i++)
        {
            if (!s.Enemies.IsAlive(i)) continue;
            ref readonly var e = ref s.Enemies.At(i);
            enemies.Add(new EnemyData
            {
                Id = e.Id, Kind = e.Kind.ToString(), X = e.X, Hp = e.Hp, MaxHp = e.MaxHp, Speed = e.Speed, SpawnSpeed = e.SpawnSpeed,
                Armor = e.Armor, Chilled = (e.Flags & EnemyFlags.Chilled) != 0, Boss = (e.Flags & EnemyFlags.Boss) != 0, LastHit = e.LastHit.ToString(),
            });
        }
        enemies.Sort((a, b) => a.Id.CompareTo(b.Id));
        var stages = BuildStageTable(s, ctx);
        return new SimData
        {
            Tick = s.Tick, Stage = s.Stage, Attempt = s.StageAttempt, Falls = s.StageFalls, Wave = s.Wave, OndaMax = s.StageWaveMax,
            ChefesVencidos = BossWaves(ctx, s.LastBossWaveBeaten(s.Stage)),
            Credits = s.Credits, Carry = s.Carry, Xp = s.Xp, Level = s.Level, BaseHp = s.BaseHp, MaxBaseHp = s.MaxBaseHp, Reservas = s.Reservas,
            Region = profile.Region, UnlockedTowers = profile.UnlockedTowers,
            Rng = new RngData(s.Rng.State, s.Rng.Inc), NextId = s.NextId,
            WaveState = new WaveStateData
            {
                Clock = s.WaveClock, Rest = s.Rest, SpawnIndex = s.NextSpawnIndex, HasPlan = s.ActivePlan is not null, FarmWave = s.FarmWave,
            },
            Towers = towers, Enemies = enemies,
            TotalFalls = s.Falls, UnlockedStage = s.UnlockedStage, BossQuestionsCorrect = s.BossQuestionsCorrect,
            PresenceMultiplier = s.PresenceMultiplier, XpCarry = s.XpCarry, ManualUpgrades = s.ManualUpgrades,
            IncomePerSecond = s.IncomePerSecond, RegionHp = s.RegionHpMultiplier, Present = s.Present, StageTable = stages,
        };
    }

    private static EventsData ToEventsData(SimState s)
    {
        if (!s.EventsEnabled) return new EventsData { StageAttempt = s.StageAttempt, Ledger = LedgerData(s), DrawnQuestions = DrawnData(s) };
        var st = s.Scheduler!.Snapshot();
        BossEncounterData? enc = null;
        if (s.Encounter is { } e)
        {
            var es = e.State;
            enc = new BossEncounterData
            {
                Stage = es.Key.Stage, Attempt = es.Key.Attempt, Wave = es.Key.Wave, QuestionId = es.QuestionId, ElapsedS = es.Elapsed,
                CardOpen = es.CardOpen, Result = es.Result?.ToString(), OptionIndex = es.OptionIndex,
            };
        }
        return new EventsData
        {
            Enabled = true, BossHold = s.BossHold, PeakQueue = s.PeakQueue, StageAttempt = s.StageAttempt, Encounter = enc,
            Scheduler = new SchedulerData
            {
                Peak = st.Peak.ToString(), PeakTimer = st.PeakTimer, PeakDue = st.PeakDue, BossActive = st.BossActive, NeedsPeakDraw = st.NeedsPeakDraw,
                ExtrasPending = st.ExtrasPending, NextExtraIn = st.NextExtraIn, CrateActive = st.CrateActive, CrateTimer = st.CrateTimer,
                CrateX = st.CrateX, NeedsCrateDraw = st.NeedsCrateDraw, CrateCountdown = st.CrateCountdown,
                PeaksClicked = st.PeaksClicked, CratesCollected = st.CratesCollected,
            },
            Ledger = LedgerData(s), DrawnQuestions = DrawnData(s),
        };
    }

    private static List<LedgerEntryData> LedgerData(SimState s) =>
        [.. s.Ledger.Snapshot().Select(kv => new LedgerEntryData
        {
            Stage = kv.Key.Stage, Attempt = kv.Key.Attempt, Wave = kv.Key.Wave, QuestionId = kv.Value.QuestionId,
            OptionIndex = kv.Value.OptionIndex, Correct = kv.Value.Correct, BonusConsumed = kv.Value.BonusConsumed,
        })];

    private static List<DrawnQuestionsData> DrawnData(SimState s) =>
        s.Selector is null ? [] :
        [.. s.Selector.Snapshot().Select(x => new DrawnQuestionsData { Stage = x.Key.Stage, Attempt = x.Key.Attempt, QuestionIds = x.Used })];

    private static List<int> BossWaves(SimContext ctx, int last)
    {
        var l = new List<int>();
        for (int w = 1; w <= last; w++) if (ctx.Waves.IsBossWave(w)) l.Add(w);
        return l;
    }

    private static List<StageProgressData> BuildStageTable(SimState s, SimContext ctx)
    {
        var l = new List<StageProgressData>();
        for (int st = 1; st <= SimState.MaxStages; st++)
        {
            int stars = s.StarsOf(st), last = s.LastBossWaveBeaten(st);
            if (stars == 0 && last == 0 && st > s.UnlockedStage && st != s.Stage) continue;
            l.Add(new StageProgressData
            {
                StageId = st, Unlocked = st <= s.UnlockedStage, Stars = stars,
                Attempt = st == s.Stage ? s.StageAttempt : 1, ChefesVencidos = BossWaves(ctx, last),
            });
        }
        return l;
    }

    // ---------------------------------------------------------------- DTO → SimState

    /// <summary>
    /// Restaura a simulação (SDD-14 RF-14.8 passo 4). Ordem: layout/escalares/entidades → <c>RestoreWave</c> (StartWave, relógios,
    /// próximo spawn, farm) → <c>RestoreStageProgress</c> → <c>RestoreEvents</c>; depois confere ContentHash e StateHash.
    /// </summary>
    public static RestoreResult Restore(SimContext ctx, OfflineSnapshot snap)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(snap);
        if (snap.GameContentHash != ctx.Content.ContentHash)
            return new(RestoreStatus.ContentHashMismatch, null, "gameContentHash diverge do ContentHash atual");
        try
        {
            return RestoreCore(ctx, snap);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or OverflowException)
        {
            return new(RestoreStatus.Invalid, null, ex.Message);
        }
    }

    private static RestoreResult RestoreCore(SimContext ctx, OfflineSnapshot snap)
    {
        var l = snap.Layout;
        if (!StripLayout.TryCreate(l.Width, l.SpawnX, l.LeakX, l.CoreX, l.PortalX, ParseEnum<TowerHost>(l.Host),
                l.FreeZones.Select(z => new FreeZone(z.X0, z.X1)).ToArray(), out var layout, out var err))
            return new(RestoreStatus.Invalid, null, "layout inválido: " + err);
        var d = snap.Sim;
        var s = new SimState(layout!)
        {
            Tick = d.Tick, Stage = d.Stage, Wave = d.Wave, Falls = d.TotalFalls, Credits = d.Credits, Carry = d.Carry, Reservas = d.Reservas,
            Xp = d.Xp, Level = d.Level, BaseHp = d.BaseHp, MaxBaseHp = d.MaxBaseHp, NextId = d.NextId,
            UnlockedStage = d.UnlockedStage, StageFalls = d.Falls, StageWaveMax = d.OndaMax, BossQuestionsCorrect = d.BossQuestionsCorrect,
            PresenceMultiplier = d.PresenceMultiplier, XpCarry = d.XpCarry, ManualUpgrades = d.ManualUpgrades,
            IncomePerSecond = d.IncomePerSecond, Present = d.Present, StageAttempt = d.Attempt,
            MigratedSlotsPending = l.MigratedSlots,
        };
        s.Rng = Rng.FromState(d.Rng.State, d.Rng.Inc);
        foreach (var t in d.Towers)
            if (s.Towers.Add(new Tower
            {
                Id = t.Id, Type = ParseEnum<TowerType>(t.Type), Anchor = ParseEnum<TowerAnchor>(t.Anchor), OffsetDip = t.OffsetDip, Placed = t.Placed, Level = t.Level, Cooldown = t.Cooldown,
                Mode = ParseEnum<Targeting>(t.Mode), Hits = checked((byte)t.Hits),
            }).IsNone) return new(RestoreStatus.Invalid, null, "torres além da capacidade");
        foreach (var e in d.Enemies)
            if (s.Enemies.Add(new Enemy
            {
                Id = e.Id, Kind = ParseEnum<EnemyKind>(e.Kind), X = e.X, Hp = e.Hp, MaxHp = e.MaxHp, Speed = e.Speed, SpawnSpeed = e.SpawnSpeed,
                Armor = checked((short)e.Armor), LastHit = ParseEnum<TowerType>(e.LastHit),
                Flags = (e.Chilled ? EnemyFlags.Chilled : 0) | (e.Boss ? EnemyFlags.Boss : 0),
            }).IsNone) return new(RestoreStatus.Invalid, null, "inimigos além da capacidade");

        var w = d.WaveState;
        s.RestoreWave(ctx, d.RegionHp, w.HasPlan, w.FarmWave, w.SpawnIndex, w.Clock, w.Rest);
        foreach (var st in d.StageTable)
            if (st.StageId is >= 1 and <= SimState.MaxStages)
                s.RestoreStageProgress(st.StageId, st.ChefesVencidos.Count == 0 ? 0 : st.ChefesVencidos.Max(), st.Stars);

        var ev = snap.Events;
        var ledger = ev.Ledger.Select(x => new KeyValuePair<LedgerKey, LedgerEntry>(
            new LedgerKey(x.Stage, x.Attempt, x.Wave), new LedgerEntry(x.QuestionId, x.OptionIndex, x.Correct, x.BonusConsumed))).ToList();
        if (ev.Enabled)
        {
            var sc = ev.Scheduler;
            var sched = new EventSchedulerState(ParseEnum<PeakPhase>(sc.Peak), sc.PeakTimer, sc.PeakDue, sc.BossActive, sc.NeedsPeakDraw,
                sc.ExtrasPending, sc.NextExtraIn, sc.CrateActive, sc.CrateTimer, sc.CrateX, sc.NeedsCrateDraw, sc.CrateCountdown,
                sc.PeaksClicked, sc.CratesCollected);
            var used = ev.DrawnQuestions.Select(x => ((x.Stage, x.Attempt), x.QuestionIds.ToArray()));
            BossEncounterState? enc = ev.Encounter is { } b
                ? new BossEncounterState(new LedgerKey(b.Stage, b.Attempt, b.Wave), b.QuestionId, b.ElapsedS, b.CardOpen,
                    b.Result is null ? null : ParseEnum<BossResult>(b.Result), b.OptionIndex)
                : null;
            s.RestoreEvents(ctx, sched, ledger, used, enc, ev.BossHold, ev.PeakQueue);
        }
        else s.Ledger.Restore(ledger);

        string actual = FormatStateHash(s.ComputeStateHash());
        if (snap.StateHash != OfflineSnapshot.MigratedStateHash && !string.Equals(actual, snap.StateHash, StringComparison.OrdinalIgnoreCase))
            return new(RestoreStatus.StateHashMismatch, null, $"StateHash {actual} ≠ gravado {snap.StateHash}");
        return new(RestoreStatus.Restored, s, null, d.Region, d.UnlockedTowers);
    }

    private static T ParseEnum<T>(string v) where T : struct, Enum =>
        Enum.TryParse<T>(v, ignoreCase: false, out var r) && Enum.IsDefined(r) ? r : throw new FormatException($"{typeof(T).Name} inválido: '{v}'");
}
