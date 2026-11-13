using System.ComponentModel;
using System.Windows;
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
        ViewModel = viewModel;
        this.onClosed = onClosed;
        DataContext = viewModel;

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
        HkInteractive.TextChanged += (_, _) => viewModel.ToggleInteractive = HkInteractive.Text;
        HkVisibility.TextChanged += (_, _) => viewModel.ToggleVisibility = HkVisibility.Text;
        HkPause.TextChanged += (_, _) => viewModel.TogglePause = HkPause.Text;
        viewModel.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    public SettingsViewModel ViewModel { get; }

    private void Refresh()
    {
        HkInteractiveErr.Text = ViewModel.GetError(nameof(SettingsViewModel.ToggleInteractive)) ?? "";
        HkVisibilityErr.Text = ViewModel.GetError(nameof(SettingsViewModel.ToggleVisibility)) ?? "";
        HkPauseErr.Text = ViewModel.GetError(nameof(SettingsViewModel.TogglePause)) ?? "";
        HkConflictText.Text = ViewModel.HotkeyConflict ?? "";
        SaveErrorText.Text = ViewModel.SaveError ?? "";
        SaveButton.IsEnabled = ViewModel.IsValid;
        if (HkInteractive.Text != ViewModel.ToggleInteractive) HkInteractive.Text = ViewModel.ToggleInteractive;
        if (HkVisibility.Text != ViewModel.ToggleVisibility) HkVisibility.Text = ViewModel.ToggleVisibility;
        if (HkPause.Text != ViewModel.TogglePause) HkPause.Text = ViewModel.TogglePause;
    }

    private void OnRestoreHotkeys(object sender, RoutedEventArgs e) => ViewModel.RestoreDefaultHotkeys();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (ViewModel.TrySave()) Close();
        else Refresh();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        onClosed?.Invoke();
    }
}
