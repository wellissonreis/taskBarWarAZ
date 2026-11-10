using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class ArchitectureTests
{
    private static readonly string[] Forbidden = ["PresentationFramework", "PresentationCore", "WindowsBase", "SideWar.Platform.Windows"];

    [Fact(DisplayName = "T-01.1 Assembly references respect layer boundaries")]
    public void References_PortableAssemblies_RespectBoundaries()
    {
        foreach (var project in new[] { "Core", "Runtime", "Rendering" })
        {
            var assembly = Assembly.Load($"SideWar.{project}");
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                var name = reference.Name!;
                if (project == "Core")
                    Assert.True(name.StartsWith("System.", StringComparison.Ordinal), name);
                Assert.DoesNotContain(name, Forbidden);
                if (project == "Runtime")
                    Assert.NotEqual("SkiaSharp", name);
            }
        }
    }

    [Fact(DisplayName = "T-01.1 Declared project and package references respect the graph")]
    public void References_ProjectFiles_RespectGraph()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SideWar.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        var allowed = new Dictionary<string, string[]>
        {
            ["Core"] = [], ["Runtime"] = ["Core"],
            ["Persistence"] = ["Core", "Runtime"],
            ["Rendering"] = ["Core", "Runtime"],
            ["Platform.Windows"] = ["Runtime"],
            ["App"] = ["Core", "Runtime", "Persistence", "Rendering", "Platform.Windows"],
        };
        foreach (var (project, dependencies) in allowed)
        {
            var document = XDocument.Load(Path.Combine(root.FullName, "src", $"SideWar.{project}", $"SideWar.{project}.csproj"));
            foreach (var reference in document.Descendants("ProjectReference"))
            {
                var name = Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value)["SideWar.".Length..];
                Assert.Contains(name, dependencies);
            }
            var packages = project switch
            {
                "Rendering" => new[] { "SkiaSharp" },
                "Platform.Windows" => new[] { "SkiaSharp", "Microsoft.Windows.CsWin32" },
                _ => Array.Empty<string>(),
            };
            foreach (var reference in document.Descendants("PackageReference"))
                Assert.Contains(reference.Attribute("Include")!.Value, packages);
        }
    }
}
