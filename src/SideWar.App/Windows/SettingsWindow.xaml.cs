using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using SideWar.Rendering.Theme;
using SideWar.Persistence.Settings;

namespace SideWar.App.Windows;

/// <summary>
/// Configurações (ADR-031). Abrir somente por ação do usuário (ADR-015). Fechar (e não ocultar) ao sair;
/// <c>onClosed</c> é chamado uma vez ao fechar para o App restaurar o foco (ForegroundRestorer, T5.8).
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly Action? onClosed;

    public SettingsWindow(SettingsViewModel viewModel, Action? onClosed = null)
    {
        InitializeComponent();
        Resources["SettingsErrorBrush"] = Brush(Palette.Perigo);
        Resources["SettingsMutedBrush"] = Brush(Palette.BordaForte);
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width);
        Height = Math.Min(Height, area.Height);
        Left = area.Right - Width;
        Top = area.Bottom - Height;
        ViewModel = viewModel;
        this.onClosed = onClosed;
        DataContext = viewModel;

        ModeCombo.ItemsSource = new[] { ("Tower Defense (novo)", GameMode.Td), ("Clássico (idle)", GameMode.Idle) };
        ModeCombo.DisplayMemberPath = "Item1";
        ModeCombo.SelectedValuePath = "Item2";
        ModeCombo.SelectedValue = viewModel.GameMode;
        ModeCombo.SelectionChanged += (_, _) => { if (ModeCombo.SelectedValue is GameMode g) viewModel.GameMode = g; };
        HideCombo.ItemsSource = Enum.GetValues<HideOnFullscreenMode>();
        HideCombo.SelectedItem = viewModel.HideOnFullscreen;
        HideCombo.SelectionChanged += (_, _) => viewModel.HideOnFullscreen = (HideOnFullscreenMode)HideCombo.SelectedItem;
        PresentCombo.ItemsSource = SettingsViewModel.PresentFpsChoices;
        PresentCombo.SelectedItem = viewModel.PresentFps;
        PresentCombo.SelectionChanged += (_, _) => { if (PresentCombo.SelectedItem is int v) viewModel.PresentFps = v; };
        AbsentCombo.ItemsSource = SettingsViewModel.AbsentFpsChoices;
        AbsentCombo.SelectedItem = viewModel.AbsentFps;
        AbsentCombo.SelectionChanged += (_, _) => { if (AbsentCombo.SelectedItem is int v) viewModel.AbsentFps = v; };
        LogCombo.ItemsSource = Enum.GetValues<LogLevelSetting>();
        LogCombo.SelectedItem = viewModel.LogLevel;
        LogCombo.SelectionChanged += (_, _) => viewModel.LogLevel = (LogLevelSetting)LogCombo.SelectedItem;
        PresenceText.Text = SettingsViewModel.PresenceInfo;

        HkInteractive.Text = viewModel.ToggleInteractive;
        HkVisibility.Text = viewModel.ToggleVisibility;
        HkPause.Text = viewModel.TogglePause;
        HkStartWave.Text = viewModel.StartWave;
        HkStartWave.TextChanged += (_, _) => viewModel.StartWave = HkStartWave.Text;
        HkInteractive.TextChanged += (_, _) => viewModel.ToggleInteractive = HkInteractive.Text;
        HkVisibility.TextChanged += (_, _) => viewModel.ToggleVisibility = HkVisibility.Text;
        HkPause.TextChanged += (_, _) => viewModel.TogglePause = HkPause.Text;
        viewModel.PropertyChanged += (_, _) => Refresh();
        Refresh();
        Loaded += async (_, _) => await viewModel.LoadStartupAsync();
    }

    private static SolidColorBrush Brush(SkiaSharp.SKColor color) =>
        new(Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));

    public SettingsViewModel ViewModel { get; }

    private void Refresh()
    {
        HkInteractiveErr.Text = ViewModel.GetError(nameof(SettingsViewModel.ToggleInteractive)) ?? "";
        HkVisibilityErr.Text = ViewModel.GetError(nameof(SettingsViewModel.ToggleVisibility)) ?? "";
        HkPauseErr.Text = ViewModel.GetError(nameof(SettingsViewModel.TogglePause)) ?? "";
        HkStartWaveErr.Text = ViewModel.GetError(nameof(SettingsViewModel.StartWave)) ?? "";
        HkConflictText.Text = ViewModel.HotkeyConflict ?? "";
        SaveErrorText.Text = ViewModel.SaveError ?? "";
        SaveButton.IsEnabled = ViewModel.IsValid && !ViewModel.StartupBusy;
        if (HkInteractive.Text != ViewModel.ToggleInteractive) HkInteractive.Text = ViewModel.ToggleInteractive;
        if (HkVisibility.Text != ViewModel.ToggleVisibility) HkVisibility.Text = ViewModel.ToggleVisibility;
        if (HkPause.Text != ViewModel.TogglePause) HkPause.Text = ViewModel.TogglePause;
        if (HkStartWave.Text != ViewModel.StartWave) HkStartWave.Text = ViewModel.StartWave;
    }

    private void OnRestoreHotkeys(object sender, RoutedEventArgs e) => ViewModel.RestoreDefaultHotkeys();

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (await ViewModel.TrySaveAsync()) Close();
        else Refresh();
    }

    private void OnSpeed1(object sender, RoutedEventArgs e) => ViewModel.TdGameSpeed = 1;
    private void OnSpeed2(object sender, RoutedEventArgs e) => ViewModel.TdGameSpeed = 2;
    private void OnSpeed3(object sender, RoutedEventArgs e) => ViewModel.TdGameSpeed = 3;
    private void OnCornerSmall(object sender, RoutedEventArgs e) => ViewModel.TdCornerUiScale = 100;
    private void OnCornerMedium(object sender, RoutedEventArgs e) => ViewModel.TdCornerUiScale = 150;
    private void OnCornerLarge(object sender, RoutedEventArgs e) => ViewModel.TdCornerUiScale = 200;
    private void OnBandSmall(object sender, RoutedEventArgs e) => ViewModel.TdBandHeight = TdSettings.SmallBandHeightDip;
    private void OnBandMedium(object sender, RoutedEventArgs e) => ViewModel.TdBandHeight = TdSettings.DefaultBandHeightDip;
    private void OnBandLarge(object sender, RoutedEventArgs e) => ViewModel.TdBandHeight = TdSettings.LargeBandHeightDip;

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        onClosed?.Invoke();
    }
}
