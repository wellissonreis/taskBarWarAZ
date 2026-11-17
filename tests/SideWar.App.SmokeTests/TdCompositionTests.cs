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
        Assert.Equal(AutoStartMode.Off, td.Host.Match.AutoStart);
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
        Assert.Equal(AutoStartMode.Off, td.Host.Match.AutoStart);
    }

    [Fact]
    public void Match_result_is_written_to_the_profile()
    {
        Directory.CreateDirectory(dir);
        var clock = new ManualClock();
        var settings = new AppSettings { Td = new TdSettings { AutoStartEnabled = true, AutoStartSeconds = 5 } };
        var td = TdComposition.Create(dir, "0.1.0", VisibleApp(), clock, settings, _ => { }, seed: 7);
        for (int i = 0; i < 400_000 && td.Host.Match.Phase != MatchPhase.Derrota; i++) { clock.NowTicks += 34; td.Host.OnWake(clock); }
        Assert.Equal(MatchPhase.Derrota, td.Host.Match.Phase); // sem torres, as ondas vazam
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
        td.StartStage(3);
        Assert.True(td.Host.Match.Options.IsClassic);
        Assert.Equal(3, td.RunStartStage);
        Assert.Equal(3, td.BuildReplay().Stage);
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
        Assert.True(match.Apply(new SideWar.Core.Td.StartWave()).IsOk);
        for (int i = 0; i < 100_000 && match.StageResults.Count == 0; i++) match.Step();
        Assert.Single(match.StageResults);
        recorder.OnEvents(match, match.Events);
        Assert.True(recorder.StageBestScore(1) > 0);
        Assert.True(recorder.StageStars(1) >= 1);
        Assert.True(recorder.StageUnlocked(2));
        Assert.True(recorder.LastOutcome.NewRecord);
        Assert.Equal(0, recorder.SaveFailures);
        var saved = new TdProfileStore(RealFileSystem.Instance, dir).Load();
        Assert.Contains(saved.Profile.Stages, s => s.StageId == 1 && s.BestScore > 0);
    }
}
