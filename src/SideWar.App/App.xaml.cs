using System.Diagnostics;
using System.Windows;
using SideWar.App.Composition;
using SideWar.Platform.Windows.Instance;

namespace SideWar.App;

/// <summary>Lifetime WPF (T-UI) do App de produção: monta a raiz de composição única (T4.10) no Startup.</summary>
public partial class App : Application
{
    /// <summary>Instância única adquirida pelo <see cref="Program"/> (a composição escuta o evento de ativação).</summary>
    internal SingleInstance? Instance { get; init; }

    /// <summary>Pasta de dados (padrão <c>%LOCALAPPDATA%\SideWar</c>).</summary>
    internal string DataDirectory { get; init; } = CrashHandler.DefaultDataDirectory;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var composition = new AppComposition(this, Instance, DataDirectory, static m => Trace.WriteLine("SideWar: " + m));
        Exit += (_, _) => composition.Dispose(); // idempotente: CloseUi já parou a T-UI no encerramento normal
        try
        {
            await composition.StartAsync();
        }
        catch (Exception ex)
        {
            CrashHandler.Fatal("AppComposition.StartAsync", ex);
        }
    }
}
