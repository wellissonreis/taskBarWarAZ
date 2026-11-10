using Xunit;
using System.Text;
using SideWar.Persistence.IO;

namespace SideWar.Persistence.Tests;

public class AtomicFileWriterTests
{
    private const string P = "/d/x.json";
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void NewFile_UsesMove_NoBackup()
    {
        var fs = new FakeFileSystem();
        Assert.True(new AtomicFileWriter(fs).Write(P, B("a")));
        Assert.Equal("a", Encoding.UTF8.GetString(fs.Files[P]));
        Assert.False(fs.Files.ContainsKey(P + ".bak"));
        Assert.False(fs.Files.ContainsKey(P + ".tmp"));
    }

    [Fact]
    public void ExistingFile_UsesReplace_AndKeepsBak()
    {
        var fs = new FakeFileSystem();
        var w = new AtomicFileWriter(fs);
        w.Write(P, B("a"));
        w.Write(P, B("b"));
        Assert.Equal("b", Encoding.UTF8.GetString(fs.Files[P]));
        Assert.Equal("a", Encoding.UTF8.GetString(fs.Files[P + ".bak"]));
        Assert.Contains("replace:" + P, fs.Calls);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(PlatformNotSupportedException))]
    public void ReplaceFailure_FallsBackToCopyAndMove(Type ex)
    {
        var fs = new FakeFileSystem();
        var w = new AtomicFileWriter(fs);
        w.Write(P, B("a"));
        fs.Fault = (op, _) => { if (op == "replace") throw (Exception)Activator.CreateInstance(ex)!; };
        Assert.True(w.Write(P, B("b")));
        Assert.Equal("b", Encoding.UTF8.GetString(fs.Files[P]));
        Assert.Equal("a", Encoding.UTF8.GetString(fs.Files[P + ".bak"]));
    }

    [Fact]
    public void SharingViolation_RetriesTwice_With50ms_ThenSucceeds()
    {
        var fs = new FakeFileSystem();
        var sleeps = new List<TimeSpan>();
        var n = 0;
        fs.Fault = (op, _) => { if (op == "write" && n++ < 2) throw new IOException("busy"); };
        Assert.True(new AtomicFileWriter(fs, sleep: sleeps.Add).Write(P, B("a")));
        Assert.Equal([TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)], sleeps);
    }

    [Fact]
    public void PersistentFailure_LogsErrorAndReturnsFalse_AfterThreeAttempts()
    {
        var fs = new FakeFileSystem();
        var writes = 0;
        var logs = new List<string>();
        fs.Fault = (op, _) => { if (op == "write") { writes++; throw new IOException("busy"); } };
        Assert.False(new AtomicFileWriter(fs, logs.Add, _ => { }).Write(P, B("a")));
        Assert.Equal(3, writes);
        Assert.Single(logs);
    }

    [Fact]
    public void UnauthorizedAccess_ReturnsFalse_WithoutThrowing()
    {
        var fs = new FakeFileSystem { Fault = (op, _) => { if (op == "write") throw new UnauthorizedAccessException(); } };
        Assert.False(new AtomicFileWriter(fs, sleep: _ => { }).Write(P, B("a")));
    }

    [Fact]
    public void Failure_KeepsOriginalFileIntact()
    {
        var fs = new FakeFileSystem();
        var w = new AtomicFileWriter(fs, sleep: _ => { });
        w.Write(P, B("a"));
        fs.Fault = (op, _) => { if (op == "write") throw new IOException(); };
        Assert.False(w.Write(P, B("b")));
        Assert.Equal("a", Encoding.UTF8.GetString(fs.Files[P]));
    }

    [Fact]
    public void RealFileSystem_RoundTrip_AndBak()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sw-t24-" + Guid.NewGuid().ToString("N"));
        try
        {
            var w = new AtomicFileWriter(RealFileSystem.Instance);
            var p = Path.Combine(dir, "sub", "f.json");
            Assert.True(w.Write(p, B("one")));
            Assert.True(w.Write(p, B("two")));
            Assert.Equal("two", File.ReadAllText(p));
            Assert.Equal("one", File.ReadAllText(p + ".bak"));
            Assert.False(File.Exists(p + ".tmp"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
