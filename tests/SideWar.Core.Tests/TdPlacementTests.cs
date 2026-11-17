using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using Xunit;
using StartWave = SideWar.Core.Td.StartWave;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using PlaceTower = SideWar.Core.Td.PlaceTower;
using SellTower = SideWar.Core.Td.SellTower;

namespace SideWar.Core.Tests;

/// <summary>TD.4: mundo lógico 1.200, mapa lógico → DIP, snap de 44 DIP, sobreposição, SEM POSIÇÃO e LayoutChanged.</summary>
public sealed class TdPlacementTests
{
    private static readonly TdContent Content = TdMechanicsContent.Load();

    private static TdContent Rich()
    {
        var stage1 = Content.Stages.Stages[0] with { StartingCredits = 1_000_000 };
        return Content with { Stages = Content.Stages with { Stages = [stage1, .. Content.Stages.Stages.Skip(1)] } };
    }

    private static StripLayout L(int width, params (int X0, int X1)[] zones) =>
        StripLayout.Create(width, width, 0f, 0f, width, TowerHost.Taskbar, zones.Select(z => new FreeZone(z.X0, z.X1)).ToArray());

    private static TdMatchState New(StripLayout? layout, ulong seed = 7)
    {
        var s = TdMatchState.Create(Rich(), 1, Rng.Seed(seed, 54));
        if (layout is not null) Assert.True(s.Apply(new LayoutChanged(layout)).IsOk);
        return s;
    }

    private static int Build(TdMatchState s, int x, string tower = "bolt")
    {
        Assert.True(s.Apply(new BuildTower(tower, x)).IsOk);
        return s.Towers[^1].Id;
    }

    private static TdTower Tw(TdMatchState s, int id) => s.Towers.First(t => t.Id == id);

    [Fact(DisplayName = "RF-28.20 / DP-28.3 Snap: the requested X lands on the 44 DIP grid of the zone (X0 + 22 + 44k)")]
    public void Snap_ToGrid()
    {
        var s = New(L(1200, (0, 1200)));
        Assert.Equal(110, Tw(s, Build(s, 100)).X);   // (100-0)/44 = 2 → 22 + 88
        Assert.Equal(154, Tw(s, Build(s, 154)).X);   // encostar (44) não é sobreposição
        Assert.Equal(1166, Tw(s, Build(s, 1200)).X); // última célula que cabe
        Assert.All(s.Towers, t => Assert.True(t.Placed));

        var z = New(L(1200, (300, 500)));
        Assert.Equal(322, Tw(z, Build(z, 300)).X);   // início da zona: centro em X0 + 22
        Assert.Equal(366, Tw(z, Build(z, 360)).X);   // (360-300)/44 = 1 → 300 + 22 + 44
    }

    [Fact(DisplayName = "RF-28.15 / DP-28.2 Logical world is fixed: X is in logical units and the DIP position scales with the bar width")]
    public void LogicalWorld_ScalesWithWidth()
    {
        var s = New(L(1800, (0, 1800)));
        int id = Build(s, 600);                      // 600 un → 900 DIP; snap 900/44 = 20 → 902 DIP → 601 un
        Assert.Equal(601, Tw(s, id).X);
        Assert.Equal(902, s.TowerDip(Tw(s, id)));
        Assert.Equal(1200, (int)TdWorld.PathLength);

        var narrow = New(L(600, (0, 600)));
        int n = Build(narrow, 600);                  // 300 DIP → célula 6 → 286 DIP → 572 un
        Assert.Equal(286, narrow.TowerDip(Tw(narrow, n)));
        Assert.Equal(572, Tw(narrow, n).X);
    }

    [Fact(DisplayName = "RF-28.20 Overlap: two towers closer than 44 DIP are rejected without changing the state")]
    public void Overlap_Rejected()
    {
        var s = New(L(1200, (0, 1200)));
        Build(s, 100);
        long credits = s.Credits;
        var hash = s.ComputeStateHash();
        var r = s.Apply(new BuildTower("bolt", 120)); // snap → 110, a mesma célula
        Assert.Equal(CommandReason.Overlap, r.Reason);
        Assert.Equal(credits, s.Credits);
        Assert.Equal(hash, s.ComputeStateHash());
        Assert.Single(s.Towers);
        Assert.Equal(CommandReason.None, s.ValidatePlacement(154, 0, out int snapped));
        Assert.Equal(154, snapped);
        Assert.Equal(CommandReason.None, s.ValidatePlacement(110, Tw(s, 1).Id, out _)); // a própria torre não conta
    }

    [Fact(DisplayName = "RF-28.20 Outside a free zone (or in a zone narrower than 44 DIP) the build is rejected")]
    public void OutsideZone_Rejected()
    {
        var s = New(L(1200, (0, 300), (600, 900), (1000, 1030)));
        var hash = s.ComputeStateHash();
        Assert.Equal(CommandReason.OutOfZone, s.Apply(new BuildTower("bolt", 450)).Reason);
        Assert.Equal(CommandReason.OutOfZone, s.Apply(new BuildTower("bolt", 1010)).Reason); // zona de 30 DIP
        Assert.Equal(CommandReason.OutOfZone, s.Apply(new BuildTower("bolt", 1100)).Reason);
        Assert.Equal(CommandReason.OutOfZone, s.Apply(new BuildTower("bolt", -1)).Reason);
        Assert.Equal(CommandReason.OutOfZone, s.Apply(new BuildTower("bolt", 1201)).Reason);
        Assert.Equal(hash, s.ComputeStateHash());
        Assert.Equal(666, Tw(s, Build(s, 650)).X);   // (650-600)/44 = 1 → 600 + 22 + 44
    }

    [Fact(DisplayName = "SDD-29 §5.3 technical cap of 48 towers applies, and unplaced towers count against it")]
    public void TechnicalLimit_CountsUnplaced()
    {
        var rich = Rich();
        rich = rich with { Stages = rich.Stages with { Stages = [rich.Stages.Stages[0] with { StartingCredits = 4_000_000_000L }, .. rich.Stages.Stages.Skip(1)] } };
        var s = TdMatchState.Create(rich, 1, Rng.Seed(7, 54));
        Assert.True(s.Apply(new LayoutChanged(L(7680, (0, 7680)))).IsOk);
        for (int x = 4; s.Towers.Count < TdMatchState.TechnicalTowerLimit && x < 1200; x += 8) s.Apply(new BuildTower("bolt", x));
        Assert.Equal(TdMatchState.TechnicalTowerLimit, s.Towers.Count);
        Assert.Equal(CommandReason.TowerLimit, s.Apply(new BuildTower("bolt", 1100)).Reason);
        Assert.True(s.Apply(new LayoutChanged(L(7680, (7000, 7680)))).IsOk);
        Assert.Equal(TdMatchState.TechnicalTowerLimit, s.UnplacedCount);
        Assert.Equal(CommandReason.TowerLimit, s.Apply(new BuildTower("bolt", 1150)).Reason);
    }

    [Fact(DisplayName = "RF-28.15 / T-28.9 LayoutChanged: towers whose zone vanished become SEM POSIÇÃO, in Id order; the rest keep their logical X")]
    public void LayoutChanged_Reevaluates()
    {
        var s = New(L(1200, (0, 1200)));
        int a = Build(s, 100), b = Build(s, 500), c = Build(s, 900);
        var ax = Tw(s, a).X; var bx = Tw(s, b).X;
        Assert.True(s.Apply(new LayoutChanged(L(1200, (0, 300), (800, 1200)))).IsOk);
        Assert.True(Tw(s, a).Placed);
        Assert.False(Tw(s, b).Placed);
        Assert.True(Tw(s, c).Placed);
        Assert.Equal(ax, Tw(s, a).X);
        Assert.Equal(bx, Tw(s, b).X);                // a posição gravada é mantida
        Assert.Equal(1, s.UnplacedCount);
        Assert.Contains(s.Events, e => e.Type == TdEventType.TowerUnplaced && e.A == b);

        // encolher a barra para 600 DIP junta as torres (escala 0,5): a de Id maior perde a posição
        var t = New(L(1200, (0, 1200)));
        int t1 = Build(t, 100), t2 = Build(t, 150);   // 110 e 154 → 55 e 77 DIP no novo layout
        Assert.True(t.Apply(new LayoutChanged(L(600, (0, 600)))).IsOk);
        Assert.True(Tw(t, t1).Placed);
        Assert.False(Tw(t, t2).Placed);

        Assert.Equal(CommandReason.InvalidLayout, s.Apply(new LayoutChanged(null!)).Reason);
    }

    [Fact(DisplayName = "RF-28.20 PlaceTower/MoveTower: free reposition of an unplaced tower; placed towers reject PlaceTower; sell works")]
    public void Place_Move_Sell()
    {
        var s = New(L(1200, (0, 1200)));
        int a = Build(s, 100), b = Build(s, 500);
        Assert.True(s.Apply(new LayoutChanged(L(1200, (0, 300)))).IsOk);
        Assert.False(Tw(s, b).Placed);
        long credits = s.Credits;

        Assert.Equal(CommandReason.NotUnpositioned, s.Apply(new PlaceTower(a, 200)).Reason);
        Assert.Equal(CommandReason.TowerNotFound, s.Apply(new PlaceTower(99, 200)).Reason);
        var hash = s.ComputeStateHash();
        Assert.Equal(CommandReason.OutOfZone, s.Apply(new PlaceTower(b, 700)).Reason);
        Assert.Equal(CommandReason.Overlap, s.Apply(new PlaceTower(b, 100)).Reason);
        Assert.Equal(hash, s.ComputeStateHash());

        Assert.True(s.Apply(new PlaceTower(b, 250)).IsOk);
        Assert.True(Tw(s, b).Placed);
        Assert.Equal(242, Tw(s, b).X);               // (250-0)/44 = 5 → 22 + 220
        Assert.Equal(credits, s.Credits);            // sem custo

        Assert.Equal(CommandReason.NotUnpositioned, s.Apply(new MoveTower(a, 20)).Reason); // TD.3b: só torre SEM POSIÇÃO
        Assert.Equal(CommandReason.NotUnpositioned, s.Apply(new MoveTower(b, 20)).Reason); // b acabou de ser posicionada
        Assert.Equal(credits, s.Credits);

        Assert.True(s.Apply(new LayoutChanged(L(1200, (900, 1200)))).IsOk);
        Assert.Equal(2, s.UnplacedCount);
        long before = s.Credits;
        Assert.True(s.Apply(new SellTower(a)).IsOk);  // pode vender SEM POSIÇÃO
        Assert.True(s.Credits > before);
        Assert.Single(s.Towers);
    }

    [Fact(DisplayName = "RF-28.15 LayoutChanged in mid-wave: an unplaced tower stops shooting and recovers when its zone returns")]
    public void LayoutChanged_MidWave_TowerLosesAndRecovers()
    {
        var full = L(1200, (0, 1200));
        var s = New(full);
        int id = Build(s, 600);
        Assert.True(s.Apply(new StartWave()).IsOk);
        int guard = 0;
        while (Tw(s, id).ShotCount == 0 && guard++ < 6000) s.Step();
        Assert.True(Tw(s, id).ShotCount > 0);

        Assert.True(s.Apply(new LayoutChanged(L(1200, (0, 200)))).IsOk);
        Assert.False(Tw(s, id).Placed);
        int shots = Tw(s, id).ShotCount;
        for (int i = 0; i < 150; i++) s.Step();       // 5 s com inimigos no alcance e a torre parada
        Assert.Equal(shots, Tw(s, id).ShotCount);
        Assert.Equal(MatchPhase.Onda, s.Phase);

        Assert.True(s.Apply(new LayoutChanged(full)).IsOk); // a zona volta: reposicionada sem custo
        Assert.True(Tw(s, id).Placed);
        Assert.Contains(s.Events, e => e.Type == TdEventType.TowerPlaced && e.A == id);
        guard = 0;
        while (Tw(s, id).ShotCount == shots && guard++ < 6000 && s.Phase == MatchPhase.Onda) s.Step();
        Assert.True(Tw(s, id).ShotCount > shots);
    }

    private static TdReplayScript Script() => new(1, 42, 54,
    [
        new TdLogEntry(0, new LayoutChanged(L(1920, (0, 800), (1100, 1920)))),
        new TdLogEntry(0, new BuildTower("bolt", 100)),
        new TdLogEntry(0, new BuildTower("bolt", 105)),   // sobreposição: rejeitado
        new TdLogEntry(0, new BuildTower("bolt", 600)),   // fora da zona: rejeitado
        new TdLogEntry(0, new BuildTower("bolt", 1000)),
        new TdLogEntry(1, new StartWave()),
        new TdLogEntry(400, new LayoutChanged(L(1920, (1100, 1920)))), // as duas torres perdem a zona
        new TdLogEntry(700, new PlaceTower(1, 1100)),
        new TdLogEntry(900, new LayoutChanged(L(1366, (0, 1366)))),
        new TdLogEntry(1000, new MoveTower(2, 300)),
    ], 2400);

    [Fact(DisplayName = "RNF-28.1 / ADR-026 Layout and placement commands in the log: same log + seed → same StateHash in 5 runs (golden)")]
    public void Replay_Deterministic5Runs()
    {
        var first = TdReplayRunner.Run(Rich(), Script());
        for (int i = 0; i < 4; i++) Assert.Equal(first.StateHash, TdReplayRunner.Run(Rich(), Script()).StateHash);
        Assert.Equal(3, first.CommandsRejected); // build sobreposto, build fora da zona e MoveTower de torre posicionada (TD.3b)
        Assert.Equal(Golden, first.StateHash); // TD.20: preço crescente + torre sem Level/Invested/Branch no hash
    }

    private const ulong Golden = 15168844986091215698UL;
}
