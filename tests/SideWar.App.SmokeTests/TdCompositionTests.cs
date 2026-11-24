using System.IO;
using SideWar.App.Composition;
using SideWar.Core.Td;
using SideWar.Persistence.IO;
using SideWar.Persistence.Profile;
using SideWar.Persistence.Settings;
using SideWar.Runtime;
using SideWar.Runtime.Lifecycle;
using Xunit;

namespace SideWar.App.SmokeTests;

/// <summary>TD.6: composição headless do host TD (sem janelas nem T-Game; pasta temporária).</summary>
public sealed class TdCompositionTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "SideWar-TD6-" + Guid.NewGuid().ToString("N"));

    private sealed class ManualClock : IClock
    {
        public long NowTicks { get; set; }
        public double TicksPerSecond => 1000;
    }

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static AppStateMachine VisibleApp()
    {
        var app = new AppStateMachine();
        app.LeaveHidden(HiddenReason.Starting);
        return app;
    }

    [Fact]
    public void Seals_formula_is_min_12_of_max_wave_over_10()
    {
        Assert.Equal(0, TdProfileRecorder.SealsForMaxWave(9));
        Assert.Equal(6, TdProfileRecorder.SealsForMaxWave(60));
        Assert.Equal(12, TdProfileRecorder.SealsForMaxWave(120));
        Assert.Equal(12, TdProfileRecorder.SealsForMaxWave(500));
    }

    [Fact]
    public void Fresh_install_creates_the_td_profile_and_leaves_autostart_off()
    {
        Directory.CreateDirectory(dir);
        var clock = new ManualClock();
        var td = TdComposition.Create(dir, "0.1.0", VisibleApp(), clock, new AppSettings(), _ => { }, seed: 7);
        td.Host.OnWake(clock);
        Assert.True(File.Exists(Path.Combine(dir, TdProfileStore.FileName)));
        Assert.False(File.Exists(Path.Combine(dir, ProfileStore.FileName))); // o perfil idle nunca é criado/alterado
        Assert.Equal(1, td.Host.Match.StageId);
        Assert.Equal(AutoStartMode.Immediate, td.Host.Match.AutoStart);
    }

    [Fact]
    public void Towers_survive_stage_change_restart_reopen_and_only_the_wipe_clears_them()
    {
        Directory.CreateDirectory(dir);
        var clock = new ManualClock();
        var td = TdComposition.Create(dir, "0.1.0", VisibleApp(), clock, new AppSettings(), _ => { }, seed: 7);
        td.Host.OnCommand(new SideWar.Core.Td.LayoutChanged(TdSanityBot.TypicalLayout()));
        string id = td.Host.Match.Content.Towers.First(t => t.MinRank <= 0).Id;
        typeof(TdMatchState).GetProperty("Credits")!.SetValue(td.Host.Match, 5000L); // 360 iniciais só pagam a 1ª torre (preço crescente)
        td.Host.OnCommand(new SideWar.Core.Td.BuildTower(id, 150));
        td.Host.OnCommand(new SideWar.Core.Td.BuildTower(id, 250));
        td.Host.OnWake(clock);
        Assert.Equal(2, td.Host.Match.Towers.Count);
        var before = td.Host.Match.ExportTowers();
        Assert.Equal(2, td.Recorder.Profile.RunTowers.Count); // gravadas no perfil já na construção

        td.StartStage(2); // mapa: Jogar (a trava pode reduzir à fase 1; as torres vêm junto de qualquer forma)
        Assert.Equal(before, td.Host.Match.ExportTowers());
        Assert.All(td.Host.Match.Towers, t => Assert.True(t.Placed));
        td.StartStage(td.RunStartStage); // Reiniciar corrida
        Assert.Equal(before, td.Host.Match.ExportTowers());

        var reopened = TdComposition.Create(dir, "0.1.0", VisibleApp(), clock, new AppSettings(), _ => { }, seed: 8);
        Assert.Equal(2, reopened.Host.Match.Towers.Count);
        reopened.Host.OnCommand(new SideWar.Core.Td.LayoutChanged(TdSanityBot.TypicalLayout()));
        reopened.Host.OnWake(clock);
        Assert.Equal(before, reopened.Host.Match.ExportTowers());

        reopened.ResetProfile();
        Assert.Empty(reopened.Host.Match.Towers);
        Assert.Empty(reopened.Recorder.Profile.RunTowers);
    }

    [Fact]
    public void Restarting_the_stage_keeps_credits_without_granting_extra()
    {
        Directory.CreateDirectory(dir);
        var clock = new ManualClock();
        var td = TdComposition.Create(dir, "0.1.0", VisibleApp(), clock, new AppSettings(), _ => { }, seed: 7);
        td.Host.OnCommand(new SideWar.Core.Td.LayoutChanged(TdSanityBot.TypicalLayout()));
        long initial = td.Host.Match.Credits;
        td.StartStage(td.RunStartStage); // sem gastar: não ganha extra
        Assert.Equal(initial, td.Host.Match.Credits);
        td.Host.OnCommand(new SideWar.Core.Td.BuildTower(td.Host.Match.Content.Towers.First(t => t.MinRank <= 0).Id, 150));
        td.Host.OnWake(clock); // o comando só é aplicado no próximo tick do host
        long spent = td.Host.Match.Credits;
        Assert.True(spent < initial);
        td.StartStage(td.RunStartStage); // gastou: reiniciar = max(atuais, iniciais da fase) (2ed5a99): não devolve a torre nem concede extra
        Assert.Equal(Math.Max(spent, initial), td.Host.Match.Credits);
    }

    [Fact]
    public void Returning_to_a_reached_stage_preserves_towers_xp_and_reached_progress()
    {
        Directory.CreateDirectory(dir);
        var store = new TdProfileStore(RealFileSystem.Instance, dir, "0.1.0");
        Assert.True(store.Save(TdProfileData.Default with { Reputation = TdRank.Threshold(TdRankLevel.Junior), RunStageNumber = 2, BestStageNumber = 0 }));
        var clock = new ManualClock();
        var td = TdComposition.Create(dir, "0.1.0", VisibleApp(), clock, new AppSettings(), _ => { }, seed: 7);
        td.Host.OnCommand(new LayoutChanged(TdSanityBot.TypicalLayout()));
        td.Host.OnCommand(new BuildTower(td.Host.Match.Content.Towers.First(t => t.MinRank <= 0).Id, 150));
        td.Host.OnWake(clock);
        var towers = td.Host.Match.ExportTowers();
        Assert.Single(towers);
        Assert.True(td.Recorder.StageUnlocked(2));
        var xp = td.Recorder.Profile.Reputation;
        var rank = td.Recorder.Rank.Current;

        td.StartStage(1);
        Assert.Equal(1, td.Host.Match.StageNumber);
        Assert.Equal(towers, td.Host.Match.ExportTowers());
        Assert.Equal(xp, td.Recorder.Profile.Reputation);
        Assert.Equal(rank, td.Recorder.Rank.Current);
        Assert.True(td.Recorder.StageUnlocked(2));
        Assert.Equal(0, td.Recorder.StageStars(2));

        var reopened = TdComposition.Create(dir, "0.1.0", VisibleApp(), clock, new AppSettings(), _ => { }, seed: 8);
        Assert.True(reopened.Recorder.StageUnlocked(2));
        reopened.StartStage(2);
        Assert.Equal(2, reopened.Host.Match.StageNumber);
        Assert.Equal(towers, reopened.Host.Match.ExportTowers());
        Assert.Equal(xp, reopened.Recorder.Profile.Reputation);
    }

    [Fact]
    public void ResetProfile_wipes_progress_persists_it_and_starts_a_fresh_stage_1_match()
    {
        Directory.CreateDirectory(dir);
        var clock = new ManualClock();
        var seeded = new TdProfileStore(SideWar.Persistence.IO.RealFileSystem.Instance, dir, "0.1.0");
        Assert.True(seeded.Save(TdProfileData.Default with
        {
            RuneWeb = TdRuneWeb.ToText(0b11), RuneFragments = 9, Reputation = 500, SealsEarned = 4, BestRunWaves = 30,
            PurchasedNodes = ["defense.damage1"], Encounters = [new TdEncounterData { Id = "swarm", Count = 3 }],
            Stages = [new TdStageProgressData { StageId = 1, Unlocked = true, Stars = 3, BestScore = 99 }, new TdStageProgressData { StageId = 2, Unlocked = true }],
        }));
        var td = TdComposition.Create(dir, "0.1.0", VisibleApp(), clock, new AppSettings(), _ => { }, seed: 7);
        Assert.Equal(2, td.Host.Match.AwakenedCount);
        td.ResetProfile();
        Assert.Equal(1, td.RunStartStage);
        Assert.Equal(0, td.Host.Match.AwakenedCount);
        Assert.Equal(0, td.Host.Match.Fragments);
        var reloaded = new TdProfileStore(SideWar.Persistence.IO.RealFileSystem.Instance, dir, "0.1.0").Load().Profile;
        Assert.Equal(TdProfileData.Default with { IdleImportDone = true }, reloaded with { RunStageNumber = 0, Stages = TdProfileData.Default.Stages, PurchasedNodes = [], UnlockedTowers = [], InfiniteRuns = [], Encounters = TdProfileData.Default.Encounters });
        Assert.Equal(0, reloaded.Reputation);
        Assert.Empty(reloaded.PurchasedNodes);
        Assert.Empty(reloaded.Encounters);
        Assert.Single(reloaded.Stages);
        Assert.True(reloaded.IdleImportDone);
    }

    [Fact]
    public void Runes_are_permanent_profile_progress_across_runs_and_restarts()
    {
        Directory.CreateDirectory(dir);
        var clock = new ManualClock();
        var seeded = new TdProfileStore(SideWar.Persistence.IO.RealFileSystem.Instance, dir, "0.1.0");
        Assert.True(seeded.Save(TdProfileData.Default with { RuneWeb = TdRuneWeb.ToText(0b11), RuneFragments = 9 }));
        var td = TdComposition.Create(dir, "0.1.0", VisibleApp(), clock, new AppSettings(), _ => { }, seed: 7);
        Assert.Equal(2, td.Host.Match.AwakenedCount);
        Assert.Equal(9, td.Host.Match.Fragments);
        var node = td.Host.Match.Frontier[0];
        Assert.True(td.Host.Match.Apply(new AwakenRune(node)).IsOk);
        Assert.Equal(3, td.Host.Match.AwakenedCount);
        td.Recorder.OnMatchAbandoned(td.Host.Match); // grava a teia do perfil
        td.StartStage(1);
        Assert.Equal(3, td.Host.Match.AwakenedCount);
        Assert.Equal(td.Recorder.RuneFragments, td.Host.Match.Fragments);
        var reopened = TdComposition.Create(dir, "0.1.0", VisibleApp(), clock, new AppSettings(), _ => { }, seed: 99);
        Assert.Equal(3, reopened.Host.Match.AwakenedCount);
        Assert.True(reopened.Host.Match.IsAwakened(node));
        Assert.Equal(td.Host.Match.Fragments, reopened.Host.Match.Fragments);
    }

    [Fact]
    public void Autostart_setting_reaches_the_match()
    {
        Directory.CreateDirectory(dir);
        var clock = new ManualClock();
        var settings = new AppSettings { Td = new TdSettings { AutoStartEnabled = true, AutoStartSeconds = 20 } };
        var td = TdComposition.Create(dir, "0.1.0", VisibleApp(), clock, settings, _ => { }, seed: 7);
        td.Host.OnWake(clock);
        Assert.Equal(AutoStartMode.Countdown, td.Host.Match.AutoStart);
        Assert.Equal(20, td.Host.Match.AutoStartSeconds);
        td.ApplySettings(new AppSettings());
        td.Host.OnWake(clock);
        Assert.Equal(AutoStartMode.Immediate, td.Host.Match.AutoStart);
        td.StartStage(1);
        td.Host.OnWake(clock);
        Assert.Equal(AutoStartMode.Immediate, td.Host.Match.AutoStart);
    }

    [Fact]
    public void Wave_reputation_is_written_to_the_profile()
    {
        Directory.CreateDirectory(dir);
        var clock = new ManualClock();
        var settings = new AppSettings { Td = new TdSettings { AutoStartEnabled = true, AutoStartSeconds = 5 } };
        var td = TdComposition.Create(dir, "0.1.0", VisibleApp(), clock, settings, _ => { }, seed: 7);
        td.Host.OnCommand(new SideWar.Core.Td.LayoutChanged(TdSanityBot.TypicalLayout()));
        td.Host.OnCommand(new SideWar.Core.Td.BuildTower(td.Host.Match.Content.Towers.First(t => t.MinRank <= 0).Id, 150)); // iniciar onda exige torre posicionada
        for (int i = 0; i < 400_000 && td.Host.Match.ReputationGainedTotal == 0; i++) { clock.NowTicks += 34; td.Host.OnWake(clock); }
        Assert.True(td.Host.Match.ReputationGainedTotal > 0); // e a onda vencida grava a reputação
        var saved = new TdProfileStore(RealFileSystem.Instance, dir).Load();
        Assert.Equal(TdProfileSource.File, saved.Source);
        Assert.Contains(saved.Profile.Stages, s => s.StageId == 1 && s.Unlocked && s.Stars == 0);
        Assert.Equal(0, td.Recorder.SaveFailures);
    }

    [Fact]
    public void Profile_purchased_nodes_reach_the_match()
    {
        Directory.CreateDirectory(dir);
        var clock = new ManualClock();
        var store = new TdProfileStore(RealFileSystem.Instance, dir, "0.1.0");
        Assert.True(store.Save(new TdProfileData { IdleImportDone = true, PurchasedNodes = ["operation.snapshot"] }));
        var td = TdComposition.Create(dir, "0.1.0", VisibleApp(), clock, new AppSettings(), _ => { }, seed: 7);
        Assert.True(td.Host.Match.AbilityStatuses.First(a => a.Id == "snapshot").Unlocked);

        var dir2 = dir + "-b";
        try
        {
            Directory.CreateDirectory(dir2);
            var td2 = TdComposition.Create(dir2, "0.1.0", VisibleApp(), clock, new AppSettings(), _ => { }, seed: 7);
            Assert.False(td2.Host.Match.AbilityStatuses.First(a => a.Id == "snapshot").Unlocked);
        }
        finally { try { Directory.Delete(dir2, true); } catch (IOException) { } }
    }

    [Fact]
    public void Continuous_setting_picks_match_options_for_new_runs_and_replays_carry_them()
    {
        Directory.CreateDirectory(dir);
        var clock = new ManualClock();
        var td = TdComposition.Create(dir, "0.1.0", VisibleApp(), clock, new AppSettings(), _ => { }, seed: 7);
        Assert.True(td.Host.Match.Options.ContinuousWaves);
        Assert.True(td.BuildReplay().Options!.PersistentRun);
        td.ApplySettings(new AppSettings { Td = new TdSettings { ContinuousWaves = false } });
        Assert.True(td.Host.Match.Options.ContinuousWaves); // a corrida atual não muda
        td.StartStage(3); // a trava por patente reduz à fase 1 (281f9cf)
        Assert.True(td.Host.Match.Options.IsClassic);
        Assert.Equal(1, td.RunStartStage);
        Assert.Equal(1, td.BuildReplay().Stage);
        Assert.True(td.BuildReplay().Options!.IsClassic);
    }

    [Fact]
    public void Stage_cleared_event_records_score_crown_and_unlock_by_stage_id()
    {
        Directory.CreateDirectory(dir);
        var content = SideWar.Core.Content.Td.TdContentLoader.LoadEmbedded();
        var defs = new[] { new SideWar.Core.Content.Td.TdWaveDef(1, false, 1.0, [new SideWar.Core.Content.Td.TdWaveGroupDef("grunt", 1, 0.0, 1.0)]) };
        var stages = content.Stages.Stages.Select(s => s.Id == 1 ? s with { Waves = 1, StartingCredits = 5000 } : s).ToArray();
        var waves = content.Waves.Stages.Select(s => s.Stage == 1 ? new SideWar.Core.Content.Td.TdStageWavesDef(1, defs) : s).ToArray();
        var tiny = content with { Waves = new SideWar.Core.Content.Td.TdWavesDef(waves), Stages = content.Stages with { Stages = stages } };
        var recorder = TdProfileRecorder.Load(new TdProfileStore(RealFileSystem.Instance, dir, "0.1.0"), () => null, tiny.Meta, 6);
        var match = TdMatchState.Create(tiny, 1, SideWar.Core.Simulation.Rng.Seed(7, 54), null, TdMatchOptions.Default);
        Assert.True(match.Apply(new SideWar.Core.Td.LayoutChanged(TdSanityBot.TypicalLayout())).IsOk);
        Assert.True(match.Apply(new SetPresence(false)).IsOk);
        Assert.True(match.Apply(new SideWar.Core.Td.BuildTower("bolt", 150)).IsOk); // iniciar onda exige torre posicionada
        Assert.True(match.Apply(new SideWar.Core.Td.StartWave()).IsOk);
        for (int i = 0; i < 100_000 && match.StageResults.Count == 0; i++) match.Step();
        Assert.Single(match.StageResults);
        recorder.OnEvents(match, match.Events);
        Assert.True(recorder.StageBestScore(1) > 0);
        Assert.True(recorder.StageStars(1) >= 1);
        Assert.False(recorder.StageUnlocked(2)); // a fase 2 exige patente Júnior (281f9cf): o clear grava o desbloqueio no perfil, mas a trava por patente segue
        Assert.Contains(recorder.Profile.Stages, s => s.StageId == 2 && s.Unlocked);
        Assert.True(recorder.LastOutcome.NewRecord);
        Assert.Equal(0, recorder.SaveFailures);
        var saved = new TdProfileStore(RealFileSystem.Instance, dir).Load();
        Assert.Contains(saved.Profile.Stages, s => s.StageId == 1 && s.BestScore > 0);
    }
}
