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
}
