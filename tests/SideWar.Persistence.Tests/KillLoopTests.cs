using System.Diagnostics;
using SideWar.KillTarget;
using SideWar.Persistence;
using SideWar.Persistence.Session;
using Xunit;

namespace SideWar.Persistence.Tests;

/// <summary>
/// SDD-14 CA-14.2 / T-14.3: matar o processo (Kill = TerminateProcess, igual a Stop-Process -Force) durante gravações em loop,
/// 200 vezes, nunca deixa session.json ilegível. Headless: processo filho sem janela, sem UI, sem foco, sem cursor.
/// </summary>
public class KillLoopTests
{
    private const int Iterations = 200;
    private readonly Xunit.ITestOutputHelper _out;

    public KillLoopTests(Xunit.ITestOutputHelper output) => _out = output;

    [Fact]
    [Trait("Env", "WindowsHeadless")]
    public void KillDuringSaveLoop_200Times_SessionJsonNeverUnreadable_CA14_2()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "CA-14.2 é medido no Windows (File.Replace/NTFS).");
        var seedText = Environment.GetEnvironmentVariable("SIDEWAR_KILL_SEED");
        var seed = seedText is null ? Environment.TickCount : int.Parse(seedText, System.Globalization.CultureInfo.InvariantCulture);
        _out.WriteLine($"KILL-SEED={seed}");
        var rng = new Random(seed);
        var dll = Path.Combine(AppContext.BaseDirectory, "SideWar.KillTarget.dll");
        Assert.True(File.Exists(dll), dll);

        var dir = Path.Combine(Path.GetTempPath(), "sidewar-kill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var sw = Stopwatch.StartNew();
        try
        {
            var store = new SessionStore(SideWar.Persistence.IO.RealFileSystem.Instance, dir);
            Assert.True(store.Save(KillSnapshots.Build(0)));
            long lastTick = 0;
            int fromMain = 0, fromBackup = 0, killedMidWrite = 0;
            for (var i = 1; i <= Iterations; i++)
            {
                var delayMs = rng.Next(0, 120);
                using (var p = Launch(dll, dir, lastTick + 1))
                {
                    try
                    {
                        Assert.Equal("READY", p.StandardOutput.ReadLine());
                        Thread.Sleep(delayMs);
                    }
                    finally
                    {
                        try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                        p.WaitForExit();
                    }
                }
                if (File.Exists(Path.Combine(dir, "session.json.tmp"))) killedMidWrite++;
                var r = store.Load(KillSnapshots.ContentHash);
                Assert.True(r.Status == SessionLoadStatus.Restored, $"iteração {i} (seed {seed}, delay {delayMs} ms): session.json e .bak ilegíveis ({r.Status})");
                if (r.Source == DocumentSource.File) fromMain++; else fromBackup++;
                Assert.True(r.Snapshot!.Sim.Tick >= lastTick + 1, $"iteração {i}: tick regrediu (esperado ≥ último+1) {r.Snapshot.Sim.Tick} < {lastTick}");
                Assert.Equal(r.Snapshot.Sim.Tick, r.Snapshot.Sim.Credits);
                lastTick = r.Snapshot.Sim.Tick;
                Assert.False(Directory.Exists(Path.Combine(dir, "corrupt")), $"iteração {i} (seed {seed}): principal ilegível foi para corrupt\\");
            }
            _out.WriteLine($"OK {Iterations}/{Iterations} seed={seed} principal={fromMain} bak={fromBackup} tmpResidual={killedMidWrite} ticks={lastTick} tempo={sw.Elapsed.TotalSeconds:F1}s");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    private static Process Launch(string dll, string dir, long startTick)
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" \"{dir}\" {startTick}")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        return Process.Start(psi)!;
    }
}
