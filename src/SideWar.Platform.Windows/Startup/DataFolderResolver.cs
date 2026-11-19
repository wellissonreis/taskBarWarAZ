using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SideWar.Platform.Windows.Startup;

/// <summary>Identidade de pacote do processo (MSIX). Abstração para teste.</summary>
public interface IPackageIdentity
{
    /// <summary>Package Family Name quando empacotado; null quando não empacotado.</summary>
    string? PackageFamilyName { get; }
}

/// <summary>Identidade real, via GetCurrentPackageFamilyName (mesma detecção de <see cref="StartupService"/>).</summary>
public sealed class WindowsPackageIdentity : IPackageIdentity
{
    private const int AppModelErrorNoPackage = 15700;
    private const int ErrorInsufficientBuffer = 122;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFamilyName(ref uint length, [Out] char[]? name);

    public string? PackageFamilyName
    {
        get
        {
            uint length = 0;
            int result = GetCurrentPackageFamilyName(ref length, null);
            if (result == AppModelErrorNoPackage) return null;
            if (result != ErrorInsufficientBuffer) throw new Win32Exception(result);
            var buffer = new char[length];
            result = GetCurrentPackageFamilyName(ref length, buffer);
            if (result != 0) throw new Win32Exception(result);
            return new string(buffer, 0, (int)length - 1);
        }
    }
}

/// <summary>Onde os dados ficam e por quê.</summary>
public readonly record struct DataFolder(string Path, bool IsPackaged, string? PackageFamilyName);

/// <summary>
/// Único resolvedor da pasta de dados (settings, saves, logs, crash). Empacotado (MSIX): armazenamento privado do
/// pacote, %LOCALAPPDATA%\Packages\&lt;PFN&gt;\LocalState, nunca %LOCALAPPDATA%\SideWar (evita herdar dados do ZIP/dev
/// pela virtualização de AppData). Não empacotado: %LOCALAPPDATA%\SideWar.
/// </summary>
public static class DataFolderResolver
{
    public static DataFolder Resolve(IPackageIdentity identity, string localAppData)
    {
        var pfn = identity.PackageFamilyName;
        return string.IsNullOrEmpty(pfn)
            ? new DataFolder(Path.Combine(localAppData, "SideWar"), false, null)
            : new DataFolder(Path.Combine(localAppData, "Packages", pfn, "LocalState"), true, pfn);
    }

    public static DataFolder Resolve() => Resolve(new WindowsPackageIdentity(), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
}
