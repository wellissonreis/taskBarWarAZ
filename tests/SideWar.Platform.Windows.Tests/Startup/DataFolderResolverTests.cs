using SideWar.Platform.Windows.Startup;
using Xunit;

namespace SideWar.Platform.Windows.Tests.Startup;

public sealed class DataFolderResolverTests
{
    private sealed class Fake(string? pfn) : IPackageIdentity { public string? PackageFamilyName => pfn; }

    [Fact]
    public void NotPackagedUsesLocalAppDataSideWar()
    {
        var r = DataFolderResolver.Resolve(new Fake(null), Path.Combine("root", "Local"));
        Assert.False(r.IsPackaged);
        Assert.Null(r.PackageFamilyName);
        Assert.Equal(Path.Combine("root", "Local", "SideWar"), r.Path);
    }

    [Fact]
    public void PackagedUsesPackageLocalState()
    {
        var r = DataFolderResolver.Resolve(new Fake("SideWar_abc123"), Path.Combine("root", "Local"));
        Assert.True(r.IsPackaged);
        Assert.Equal("SideWar_abc123", r.PackageFamilyName);
        Assert.Equal(Path.Combine("root", "Local", "Packages", "SideWar_abc123", "LocalState"), r.Path);
        Assert.DoesNotContain(Path.Combine("Local", "SideWar") + Path.DirectorySeparatorChar, r.Path + Path.DirectorySeparatorChar);
    }

    [Fact]
    public void EmptyFamilyNameCountsAsNotPackaged() =>
        Assert.False(DataFolderResolver.Resolve(new Fake(""), "x").IsPackaged);
}
