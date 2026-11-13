using System.IO;
using System.Reflection;

namespace SideWar.App.Windows;

/// <summary>Conteúdo da AboutWindow (SDD-15 RF-15.9): versão, licenças OFL das fontes, atribuição dos assets e pasta de dados.</summary>
public sealed record AboutInfo(string Version, string DataFolder, string AssetsAttribution, IReadOnlyList<LicenseEntry> Licenses)
{
    public static readonly string[] FontLicenseResources = ["LICENSE-Silkscreen.txt", "LICENSE-JetBrainsMono.txt"];

    public const string AssetsText =
        "Assets do design \"Azure Defense — Idle de barra\": direitos do autor do projeto (D-03). Fontes Silkscreen e JetBrains Mono sob SIL Open Font License 1.1.";

    public static AboutInfo Create(string dataFolder, Assembly? assembly = null)
    {
        assembly ??= typeof(AboutInfo).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString() ?? "0.0.0";
        var plus = version.IndexOf('+');
        if (plus > 0) version = version[..plus];
        var licenses = new List<LicenseEntry>();
        foreach (var file in FontLicenseResources)
        {
            var name = $"SideWar.App.Licenses.{file}";
            using var stream = assembly.GetManifestResourceStream(name);
            string text = "(texto da licença indisponível)";
            if (stream is not null) { using var r = new StreamReader(stream); text = r.ReadToEnd(); }
            licenses.Add(new LicenseEntry(file.Replace("LICENSE-", "").Replace(".txt", "") + " (OFL)", text));
        }
        return new AboutInfo(version, dataFolder, AssetsText, licenses);
    }
}

public sealed record LicenseEntry(string Title, string Text);
