using System.Windows;
using System.Windows.Controls;

namespace SideWar.App.Windows;

/// <summary>Sobre (ADR-015: só por ação do usuário). <c>onClosed</c> permite ao App restaurar o foco.</summary>
public partial class AboutWindow : Window
{
    private readonly Action? onClosed;

    public AboutWindow(AboutInfo info, Action? onClosed = null)
    {
        InitializeComponent();
        this.onClosed = onClosed;
        VersionText.Text = $"Versão {info.Version}";
        DataText.Text = $"Pasta de dados: {info.DataFolder}";
        AssetsText.Text = info.AssetsAttribution;
        foreach (var l in info.Licenses)
        {
            LicenseTabs.Items.Add(new TabItem
            {
                Header = l.Title,
                Content = new TextBox { Text = l.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            });
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        onClosed?.Invoke();
    }
}
