using SideWar.Persistence.IO;
using SideWar.Persistence.Session;

namespace SideWar.KillTarget;

/// <summary>Snapshot sintético, mas com o schema real. Tick identifica a gravação; muitos inimigos alongam a escrita.</summary>
public static class KillSnapshots
{
    public const string ContentHash = "kill-target-content";

    public static OfflineSnapshot Build(long tick) => new()
    {
        SavedUtc = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc),
        GameContentHash = ContentHash,
        StateHash = "0123456789abcdef",
        Presence = new PresenceData(),
        Layout = new LayoutData { SpawnX = 1500f, LeakX = 94f, CoreX = 40f, PortalX = 1560f, SlotX = [150f, 300f, 450f, 600f] },
        Sim = new SimData
        {
            Tick = tick,
            Credits = tick,
            Enemies = Enumerable.Range(0, 400).Select(i => new EnemyData { Id = i + 1, Kind = "grunt", X = i, Hp = 10, MaxHp = 10, Speed = 30f, SpawnSpeed = 30f }).ToArray(),
        },
    };
}

/// <summary>Processo headless (sem janela, sem UI) que só grava session.json em loop com o SessionStore real, até ser morto.</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 2 || !long.TryParse(args[1], out var start)) { Console.Error.WriteLine("uso: SideWar.KillTarget <pasta-de-dados> <tick-inicial>"); return 2; }
        var store = new SessionStore(RealFileSystem.Instance, args[0], appVersion: "kill-target");
        for (var tick = start; ; tick++)
        {
            if (!store.Save(KillSnapshots.Build(tick))) return 3;
            if (tick == start) { Console.Out.WriteLine("READY"); Console.Out.Flush(); }
        }
    }
}
