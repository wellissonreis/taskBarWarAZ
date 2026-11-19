using SideWar.App.Windows;
using SideWar.Persistence.Settings;
using SideWar.Platform.Windows.Startup;
using Xunit;

namespace SideWar.App.SmokeTests;

public class StartupSettingsTests
{
    private sealed class FakeStartup : IStartupService
    {
        public bool IsPackaged { get; set; } = true;
        public StartupStatus Status { get; set; }
        public int Reads { get; private set; }
        public int Changes { get; private set; }
        public bool Fail { get; set; }
        public Task<StartupStatus> GetStatusAsync()
        {
            Reads++;
            if (Fail) throw new InvalidOperationException();
            return Task.FromResult(Status);
        }
        public Task<StartupStatus> SetEnabledAsync(bool enabled)
        {
            Changes++;
            if (Fail) throw new InvalidOperationException();
            Status = enabled ? StartupStatus.Enabled : StartupStatus.Disabled;
            return Task.FromResult(Status);
        }
    }

    [Fact]
    public async Task Unpackaged_hides_option_and_never_calls_startup_task()
    {
        var service = new FakeStartup { IsPackaged = false };
        var vm = new SettingsViewModel(AppSettings.Default, _ => true, service);
        await vm.LoadStartupAsync();
        Assert.False(vm.StartupVisible);
        Assert.True(await vm.TrySaveAsync());
        Assert.Equal(0, service.Reads);
        Assert.Equal(0, service.Changes);
    }

    [Theory]
    [InlineData(StartupStatus.DisabledByUser)]
    [InlineData(StartupStatus.DisabledByPolicy)]
    [InlineData(StartupStatus.EnabledByPolicy)]
    public async Task Blocked_states_are_explained_and_cannot_be_changed(StartupStatus status)
    {
        var service = new FakeStartup { Status = status };
        var vm = new SettingsViewModel(AppSettings.Default, _ => true, service);
        await vm.LoadStartupAsync();
        Assert.True(vm.StartupVisible);
        Assert.False(vm.StartupAvailable);
        Assert.NotNull(vm.StartupMessage);
        vm.StartWithWindows = true;
        Assert.True(await vm.TrySaveAsync());
        Assert.Equal(0, service.Changes);
    }

    [Fact]
    public async Task Changes_are_applied_only_on_save_and_reflect_windows_state()
    {
        var service = new FakeStartup();
        var vm = new SettingsViewModel(AppSettings.Default, _ => true, service);
        await vm.LoadStartupAsync();
        Assert.False(vm.StartWithWindows);
        vm.StartWithWindows = true;
        Assert.Equal(0, service.Changes);
        Assert.True(await vm.TrySaveAsync());
        Assert.Equal(StartupStatus.Enabled, service.Status);
        Assert.Equal(1, service.Changes);
        Assert.True(await vm.TrySaveAsync());
        Assert.Equal(1, service.Changes);
        vm.StartWithWindows = false;
        Assert.True(await vm.TrySaveAsync());
        Assert.Equal(StartupStatus.Disabled, service.Status);
    }

    [Fact]
    public async Task Failure_keeps_window_open_and_explains_error()
    {
        var service = new FakeStartup();
        var vm = new SettingsViewModel(AppSettings.Default, _ => true, service);
        await vm.LoadStartupAsync();
        vm.StartWithWindows = true;
        service.Fail = true;
        Assert.False(await vm.TrySaveAsync());
        Assert.NotNull(vm.SaveError);
    }

    [Fact]
    public async Task Startup_failure_still_persists_settings_and_reports_error()
    {
        var service = new FakeStartup();
        int saves = 0;
        var vm = new SettingsViewModel(AppSettings.Default, _ => { saves++; return true; }, service);
        await vm.LoadStartupAsync();
        vm.StartWithWindows = true;
        service.Fail = true;
        Assert.False(await vm.TrySaveAsync());
        Assert.Equal(1, saves);
        Assert.Contains("gravadas", vm.SaveError);
    }
}
