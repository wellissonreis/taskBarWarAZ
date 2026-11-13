using System.Reflection;
using System.Text.RegularExpressions;
using SideWar.Platform.Windows.Diagnostics;
using Xunit;

namespace SideWar.Platform.Windows.Tests;

/// <summary>`--version` output (SDD-18 RF-18.1): pure function, no window, no process.</summary>
public sealed class DiagCommandVersionTests
{
    [Fact]
    public void Version_IsSemVerWithOptionalShortHash()
    {
        var text = DiagCommand.Version(typeof(DiagCommand).Assembly);
        Assert.Matches(new Regex(@"^0\.1\.0(\+[0-9A-Za-z]+)?$"), text); // Version=0.1.0 (+ SourceRevisionId when published)
    }

    [Fact]
    public void Version_IsSingleLineWithoutWhitespace()
    {
        var text = DiagCommand.Version(typeof(DiagCommand).Assembly);
        Assert.Equal(text.Trim(), text);
        Assert.DoesNotContain('\n', text);
    }

    [Fact]
    public void Version_MatchesInformationalVersionAttribute()
    {
        var assembly = typeof(DiagCommand).Assembly;
        Assert.Equal(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion, DiagCommand.Version(assembly));
    }
}
