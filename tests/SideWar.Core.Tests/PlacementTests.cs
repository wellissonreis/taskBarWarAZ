using SideWar.Core.Content;
using SideWar.Core.Simulation;
using Xunit;

namespace SideWar.Core.Tests;

/// <summary>ADR-034: <c>PlacementValidator</c> 1D, comandos de posição livre, reavaliação por <c>Id</c> e migração (T-11.10, T-11.13, T-21.4).</summary>
public sealed class PlacementTests
{
    private static readonly GameContent Content = ContentLoader.LoadEmbedded();
    private static readonly SimContext Ctx = new(Content);

    private static StripLayout Layout(int width = 1000, params FreeZone[] zones) =>
        StripLayout.Create(width, width - 46f, 88f, 10f, width - 80f, TowerHost.Taskbar, zones);

    private static SimState NewState(StripLayout layout)
    {
        var s = SimState.CreateInitial(Content, layout, Rng.Seed(42, 54));
        s.Credits = 1_000_000_000_000;
        return s;
    }

    private static readonly FreeZone[] TwoZones = [new(100, 300), new(500, 700)];

    private static int Id(SimState s, int index) => s.Towers.At(index).Id;
    private static Tower ById(SimState s, int id) => s.Towers.Get(s.FindTowerById(id));

    // ---------------- PlacementValidator (RF-11.9) ----------------

    [Fact(DisplayName = "T-11.13 Validator: caixa de 44 DIP inteira numa zona; bordas exatas valem, 1 DIP a menos não")]
    public void Validator_Zone_Edges()
    {
        var l = Layout(1000, TwoZones);
        var towers = new TowerStore();
        Assert.Equal(PlacementReason.None, PlacementValidator.Validate(122, l, towers));   // 100 = 122 − 22
        Assert.Equal(PlacementReason.OutOfZone, PlacementValidator.Validate(121, l, towers));
        Assert.Equal(PlacementReason.None, PlacementValidator.Validate(278, l, towers));   // 300 = 278 + 22
        Assert.Equal(PlacementReason.OutOfZone, PlacementValidator.Validate(279, l, towers));
        Assert.Equal(PlacementReason.OutOfZone, PlacementValidator.Validate(400, l, towers)); // entre zonas
        Assert.Equal(PlacementReason.OutOfZone, PlacementValidator.Validate(-5, l, towers));
        Assert.Equal(PlacementReason.None, PlacementValidator.Validate(600, l, towers));
    }

    [Fact(DisplayName = "T-11.13 Validator: zona mais estreita que 44 DIP nunca comporta torre")]
    public void Validator_Narrow_Zone()
    {
        var l = Layout(1000, new FreeZone(100, 143));
        for (int x = 90; x < 160; x++) Assert.Equal(PlacementReason.OutOfZone, PlacementValidator.Validate(x, l, new TowerStore()));
    }

    [Fact(DisplayName = "T-11.13 Validator: encostar (44 DIP entre centros) é permitido, 43 é Overlap; só torres posicionadas contam")]
    public void Validator_Overlap()
    {
        var s = NewState(Layout(1000, new FreeZone(100, 500)));
        Assert.True(s.Apply(Ctx, new BuildTower(TowerType.Bolt, 200)).IsOk);
        int id = Id(s, 0);
        var l = s.Layout;
        Assert.Equal(PlacementReason.None, PlacementValidator.Validate(244, l, s.Towers));
        Assert.Equal(PlacementReason.None, PlacementValidator.Validate(156, l, s.Towers));
        Assert.Equal(PlacementReason.Overlap, PlacementValidator.Validate(243, l, s.Towers));
        Assert.Equal(PlacementReason.Overlap, PlacementValidator.Validate(157, l, s.Towers));
        Assert.Equal(PlacementReason.Overlap, PlacementValidator.Validate(200, l, s.Towers));
        Assert.Equal(PlacementReason.None, PlacementValidator.Validate(200, l, s.Towers, ignoreId: id));
        s.Towers.At(0).Placed = false;                                   // SEM POSIÇÃO não ocupa espaço
        Assert.Equal(PlacementReason.None, PlacementValidator.Validate(200, l, s.Towers));
    }

    [Fact(DisplayName = "T-11.13 Validator: OutOfZone vem antes de Overlap; sem alocação")]
    public void Validator_Order_And_NoAlloc()
    {
        var s = NewState(Layout(1000, new FreeZone(100, 300)));
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, 278)); // caixa [256, 300) encosta na borda
        Assert.Equal(PlacementReason.OutOfZone, PlacementValidator.Validate(290, s.Layout, s.Towers)); // também sobrepõe, mas sai da zona
        PlacementValidator.Validate(150, s.Layout, s.Towers);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++) PlacementValidator.Validate(150 + i % 100, s.Layout, s.Towers);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    // ---------------- BuildTower(type, x) (RF-11.10) ----------------

    [Fact(DisplayName = "T-11.13 BuildTower: âncora Start se x < W/2, End caso contrário (DP-33.2); offsets inteiros")]
    public void Build_Anchors()
    {
        var zones = new[] { new FreeZone(100, 300), new FreeZone(470, 530), new FreeZone(700, 900) };
        var s = NewState(Layout(1000, zones));
        Assert.True(s.Apply(Ctx, new BuildTower(TowerType.Bolt, 150)).IsOk);
        Assert.True(s.Apply(Ctx, new BuildTower(TowerType.Bolt, 800)).IsOk);
        var a = ById(s, Id(s, 0)); var d = ById(s, Id(s, 1));
        Assert.Equal((TowerAnchor.Start, 150, true), (a.Anchor, a.OffsetDip, a.Placed));
        Assert.Equal((TowerAnchor.End, 200), (d.Anchor, d.OffsetDip));
        var below = NewState(Layout(1000, zones));
        Assert.True(below.Apply(Ctx, new BuildTower(TowerType.Bolt, 499)).IsOk);   // < W/2: Start
        var bt = ById(below, Id(below, 0));
        Assert.Equal((TowerAnchor.Start, 499), (bt.Anchor, bt.OffsetDip));
        var half = NewState(Layout(1000, zones));
        Assert.True(half.Apply(Ctx, new BuildTower(TowerType.Bolt, 500)).IsOk);    // = W/2: End
        var ct = ById(half, Id(half, 0));
        Assert.Equal((TowerAnchor.End, 500), (ct.Anchor, ct.OffsetDip));
        Assert.Equal(800, s.TowerCenterX(in d));
        Assert.Equal(1, d.Level);
        Assert.Equal(Targeting.First, d.Mode);
    }

    [Fact(DisplayName = "T-11.13 BuildTower: OutOfZone e Overlap rejeitam sem debitar; evento TowerBuilt em X")]
    public void Build_Rejections_And_Event()
    {
        var s = NewState(Layout(1000, TwoZones));
        long c = s.Events.Written;
        Assert.True(s.Apply(Ctx, new BuildTower(TowerType.Bolt, 150)).IsOk);
        Assert.True(s.Events.TryRead(ref c, out var e));
        Assert.Equal((SimEventType.TowerBuilt, 150f, 45.0, (int)TowerType.Bolt), (e.Type, e.X, e.A, e.B));
        long credits = s.Credits;
        Assert.Equal(CommandReason.OutOfZone, s.Apply(Ctx, new BuildTower(TowerType.Bolt, 400)).Reason);
        Assert.Equal(CommandReason.Overlap, s.Apply(Ctx, new BuildTower(TowerType.Bolt, 170)).Reason);
        Assert.Equal(credits, s.Credits);
        Assert.Equal(1, s.Towers.Count);
    }

    [Fact(DisplayName = "T-21.4 BuildTower: limite de 24 torres (TowerLimit), inclusive SEM POSIÇÃO; custo conta todas")]
    public void Build_Limit_24()
    {
        var s = NewState(Layout(1200, new FreeZone(0, 1200)));
        for (int i = 0; i < 24; i++) Assert.True(s.Apply(Ctx, new BuildTower(TowerType.Bolt, 22 + 44 * i)).IsOk, $"torre {i}");
        Assert.Equal(24, s.Towers.Count);
        long credits = s.Credits;
        Assert.Equal(CommandReason.TowerLimit, s.Apply(Ctx, new BuildTower(TowerType.Bolt, 22 + 44 * 24)).Reason);
        Assert.Equal(credits, s.Credits);
        // posição inválida é checada antes do limite (ordem de RF-11.10)
        Assert.Equal(CommandReason.OutOfZone, s.Apply(Ctx, new BuildTower(TowerType.Bolt, 5000)).Reason);
        // SEM POSIÇÃO também conta no limite
        s.ApplyLayout(Layout(1200, new FreeZone(0, 44)));
        Assert.Equal(1, Enumerable.Range(0, s.Towers.Capacity).Count(i => s.Towers.IsAlive(i) && s.Towers.At(i).Placed));
        s.ApplyLayout(Layout(1200, new FreeZone(0, 1200)));
        Assert.Equal(24, Enumerable.Range(0, s.Towers.Capacity).Count(i => s.Towers.IsAlive(i) && s.Towers.At(i).Placed));
        Assert.Equal(TowerStore.CapacityConst, 32);
        Assert.Equal(TowerStore.MaxTowers, 24);
    }

    [Fact(DisplayName = "T-11.13 BuildTower: n do custo inclui as SEM POSIÇÃO (ADR-032)")]
    public void Build_Cost_Counts_Unpositioned()
    {
        var s = NewState(Layout(1000, new FreeZone(100, 300)));
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, 150));
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, 250));
        s.ApplyLayout(Layout(1000, new FreeZone(100, 200)));         // a 2ª torre fica fora
        Assert.False(ById(s, Id(s, 1)).Placed);
        long before = s.Credits;
        Assert.Equal(CommandReason.OutOfZone, s.Apply(Ctx, new BuildTower(TowerType.Bolt, 650)).Reason);
        s.ApplyLayout(Layout(1000, new FreeZone(100, 200), new FreeZone(600, 700)));
        Assert.Equal(before, s.Credits);                             // reavaliar é sem custo
        Assert.True(s.Apply(Ctx, new BuildTower(TowerType.Bolt, 650)).IsOk);
        Assert.Equal(before - 102, s.Credits);                       // n = 2 → ceil(45×1,5²) = 102
    }

    // ---------------- LayoutChanged: reavaliação por Id (RF-11.3) ----------------

    [Fact(DisplayName = "T-11.10 LayoutChanged: torre que não cabe vira SEM POSIÇÃO e volta sozinha, sem custo, mantendo a posição gravada")]
    public void Layout_Shrink_And_Return()
    {
        var s = NewState(Layout(1000, TwoZones));
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, 150));
        s.Apply(Ctx, new BuildTower(TowerType.Frost, 600));
        int a = Id(s, 0), b = Id(s, 1);
        long credits = s.Credits;
        long c = s.Events.Written;

        Assert.True(s.Apply(Ctx, new LayoutChanged(Layout(1000, new FreeZone(100, 300)))).IsOk); // zona direita some
        Assert.True(ById(s, a).Placed);
        Assert.False(ById(s, b).Placed);
        Assert.Equal(600, s.TowerCenterX(ById(s, b)));                      // posição gravada preservada
        Assert.Equal(2, s.Towers.Count);

        s.ApplyLayout(Layout(1000, new FreeZone(100, 300), new FreeZone(560, 660))); // reaparece, parcialmente
        Assert.True(ById(s, b).Placed);
        Assert.Equal(credits, s.Credits);
        Assert.True(ById(s, b).Level == 1 && ById(s, b).Type == TowerType.Frost);

        // alargar/encolher várias vezes: o resultado depende só do layout atual
        for (int i = 0; i < 5; i++)
        {
            s.ApplyLayout(Layout(1000, new FreeZone(100, 300)));
            Assert.False(ById(s, b).Placed);
            s.ApplyLayout(Layout(1000, TwoZones));
            Assert.True(ById(s, b).Placed);
        }
        Assert.True(s.Events.TryRead(ref c, out var e));
        Assert.Equal(SimEventType.LayoutChanged, e.Type);
        Assert.Equal(1.0, e.A);                                              // nº de zonas livres
    }

    [Fact(DisplayName = "T-11.10 LayoutChanged: reavalia por Id crescente, independente do índice no store; menor Id vence a sobreposição")]
    public void Layout_Reevaluation_By_Id()
    {
        var s = NewState(Layout(1000, new FreeZone(100, 500)));
        // Ids crescem 1,2,3; remove a primeira e recria para que o menor Id fique no maior índice do store.
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, 150));   // Id 1, índice 0
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, 250));   // Id 2, índice 1
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, 350));   // Id 3, índice 2
        // força sobreposição futura: Id 3 em 350 e Id 2 em 250 não se sobrepõem hoje; edita offsets gravados
        s.Towers.At(1).OffsetDip = 340;                       // Id 2 agora a 10 DIP de Id 3 (350)
        s.ApplyLayout(Layout(1000, new FreeZone(100, 500)));
        Assert.True(ById(s, 1).Placed);
        Assert.True(ById(s, 2).Placed);                       // Id 2 reavaliado antes de Id 3
        Assert.False(ById(s, 3).Placed);                      // Id 3 sobrepõe o Id 2 já reavaliado
        // o mesmo resultado com a ordem de índices invertida
        var r = NewState(Layout(1000, new FreeZone(100, 500)));
        var t3 = new Tower { Id = 3, Type = TowerType.Bolt, Anchor = TowerAnchor.Start, OffsetDip = 350, Placed = true, Level = 1 };
        var t2 = new Tower { Id = 2, Type = TowerType.Bolt, Anchor = TowerAnchor.Start, OffsetDip = 340, Placed = true, Level = 1 };
        r.Towers.Add(t3); r.Towers.Add(t2);                   // índice 0 = Id 3, índice 1 = Id 2
        r.ApplyLayout(Layout(1000, new FreeZone(100, 500)));
        Assert.True(ById(r, 2).Placed);
        Assert.False(ById(r, 3).Placed);
    }

    [Fact(DisplayName = "T-11.10 LayoutChanged: X recalculado pela âncora com o novo Width (End acompanha a borda direita)")]
    public void Layout_Width_Change_Moves_End_Anchored()
    {
        var s = NewState(Layout(1000, new FreeZone(700, 900)));
        Assert.True(s.Apply(Ctx, new BuildTower(TowerType.Bolt, 800)).IsOk);   // End, offset 200
        Assert.Equal(800, s.TowerCenterX(ById(s, Id(s, 0))));
        s.ApplyLayout(Layout(1200, new FreeZone(900, 1100)));                  // mesma distância da borda direita
        var t = ById(s, Id(s, 0));
        Assert.Equal((TowerAnchor.End, 200, true), (t.Anchor, t.OffsetDip, t.Placed));
        Assert.Equal(1000, s.TowerCenterX(in t));
        s.ApplyLayout(Layout(1000, new FreeZone(100, 300)));                   // novo X = 800, fora da zona
        Assert.False(ById(s, Id(s, 0)).Placed);

        var st = NewState(Layout(1000, new FreeZone(100, 300)));               // Start não se move com o Width
        st.Apply(Ctx, new BuildTower(TowerType.Bolt, 200));
        st.ApplyLayout(Layout(1500, new FreeZone(100, 300)));
        Assert.Equal(200, st.TowerCenterX(ById(st, Id(st, 0))));
        Assert.True(ById(st, Id(st, 0)).Placed);
    }

    [Fact(DisplayName = "T-11.10 LayoutChanged: aplicado só entre ticks; SEM POSIÇÃO não atira; torre End atira do X recalculado")]
    public void Layout_NotDuringTick_And_Targeting_Uses_Anchor()
    {
        var s = NewState(Layout(1000, new FreeZone(700, 900)));
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, 800));
        s.StartWave(Ctx);
        s.WaveClock = -1000f;
        var e = s.Enemies.Add(new Enemy { Id = s.TakeId(), Kind = EnemyKind.Grunt, X = 800f, Hp = 100, MaxHp = 100, Speed = 0f, SpawnSpeed = 0f });
        s.BeginTick();
        Assert.Throws<InvalidOperationException>(() => s.ApplyLayout(Layout(1000, new FreeZone(700, 900))));
        s.EndTick();
        s.Step(Ctx);
        Assert.True(s.Enemies.Get(e).Hp < 100);                                // atira de X = 800
        s.ApplyLayout(Layout(1000, new FreeZone(100, 300)));                   // SEM POSIÇÃO
        double hp = s.Enemies.Get(e).Hp;
        for (int i = 0; i < 60; i++) s.Step(Ctx);
        Assert.Equal(hp, s.Enemies.Get(e).Hp);
    }

    // ---------------- PlaceTower(id, x) (DP-33.1) ----------------

    [Fact(DisplayName = "T-11.13 PlaceTower: só para SEM POSIÇÃO, sem custo, valida a posição, emite TowerPlaced")]
    public void PlaceTower_Rules()
    {
        var s = NewState(Layout(1000, new FreeZone(100, 200), new FreeZone(240, 300)));
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, 150));
        s.Apply(Ctx, new BuildTower(TowerType.Frost, 270));
        int a = Id(s, 0), b = Id(s, 1);
        Assert.Equal(CommandReason.TowerNotFound, s.Apply(Ctx, new PlaceTower(999, 600)).Reason);
        Assert.Equal(CommandReason.NotUnpositioned, s.Apply(Ctx, new PlaceTower(a, 600)).Reason);

        s.ApplyLayout(Layout(1000, new FreeZone(100, 200), new FreeZone(500, 700)));  // b perde a zona
        Assert.False(ById(s, b).Placed);
        long credits = s.Credits; long c = s.Events.Written;
        Assert.Equal(CommandReason.OutOfZone, s.Apply(Ctx, new PlaceTower(b, 300)).Reason);
        Assert.Equal(CommandReason.OutOfZone, s.Apply(Ctx, new PlaceTower(b, 100)).Reason);   // caixa [78, 122)
        Assert.Equal(CommandReason.Overlap, s.Apply(Ctx, new PlaceTower(b, 170)).Reason);     // a está em 150
        Assert.False(ById(s, b).Placed);
        Assert.Equal(0L, s.Events.Written - c);

        Assert.True(s.Apply(Ctx, new PlaceTower(b, 600)).IsOk);
        var t = ById(s, b);
        Assert.Equal((TowerAnchor.End, 400, true), (t.Anchor, t.OffsetDip, t.Placed));
        Assert.Equal(credits, s.Credits);                                                    // sem custo
        Assert.True(s.Events.TryRead(ref c, out var e));
        Assert.Equal((SimEventType.TowerPlaced, 600f, (double)b, (int)TowerType.Frost), (e.Type, e.X, e.A, e.B));
        Assert.Equal(CommandReason.NotUnpositioned, s.Apply(Ctx, new PlaceTower(b, 650)).Reason);
        Assert.Equal(2, s.Towers.Count);
    }

    // ---------------- AutoPlaceUnpositioned() (DP-33.7) ----------------

    private static readonly int[] PlacedIds = [2, 3, 4, 5, 6];
    private static readonly float[] PlacedXs = [166f, 210f, 254f, 522f, 566f];

    private static SimState Migrated()
    {
        var s = NewState(Layout(1000, new FreeZone(0, 1000)));
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, 122));                    // Id 1, ocupa o primeiro passo
        for (int i = 0; i < 7; i++) s.Apply(Ctx, new BuildTower(TowerType.Bolt, 640 + 44 * i)); // Ids 2..8 em 640, 684, ...
        s.ApplyLayout(Layout(1000, new FreeZone(100, 300), new FreeZone(500, 600)));  // zonas finais: 640+ ficam fora
        return s;
    }

    [Fact(DisplayName = "T-11.13 AutoPlaceUnpositioned: sem pendência é rejeitado e não muda nada")]
    public void AutoPlace_Requires_Pending()
    {
        var s = Migrated();
        ulong h = s.ComputeStateHash();
        var r = s.Apply(Ctx, new AutoPlaceUnpositioned());
        Assert.Equal((CommandStatus.Rejected, CommandReason.NotAvailable), (r.Status, r.Reason));
        Assert.Equal(h, s.ComputeStateHash());
    }

    [Fact(DisplayName = "T-11.13 AutoPlaceUnpositioned: por Id, primeira posição válida da esquerda, passo de 44 a partir de X0+22; sobras ficam SEM POSIÇÃO; limpa a pendência")]
    public void AutoPlace_FirstFit()
    {
        var s = Migrated();
        Assert.Equal(1, Enumerable.Range(0, s.Towers.Capacity).Count(i => s.Towers.IsAlive(i) && s.Towers.At(i).Placed)); // só o Id 1 em 122
        s.MigratedSlotsPending = true;
        long c = s.Events.Written;
        Assert.True(s.Apply(Ctx, new AutoPlaceUnpositioned()).IsOk);
        Assert.False(s.MigratedSlotsPending);

        // zona [100,300): 122 (ocupado pelo Id 1), 166, 210, 254; zona [500,600): 522, 566. Posição 298 não cabe (caixa até 320).
        int[] expected = [122, 166, 210, 254, 522, 566];
        for (int k = 0; k < expected.Length; k++)
        {
            var t = ById(s, 1 + k);
            Assert.True(t.Placed, $"Id {1 + k}");
            Assert.Equal(expected[k], s.TowerCenterX(in t));
        }
        Assert.False(ById(s, 7).Placed);
        Assert.False(ById(s, 8).Placed);
        var five = ById(s, 5);                                                    // 522 ≥ W/2 → End
        Assert.Equal((TowerAnchor.End, 478), (five.Anchor, five.OffsetDip));
        var two = ById(s, 2);
        Assert.Equal((TowerAnchor.Start, 166), (two.Anchor, two.OffsetDip));

        var placedEvents = new List<SimEvent>();
        while (s.Events.TryRead(ref c, out var e)) if (e.Type == SimEventType.TowerPlaced) placedEvents.Add(e);
        Assert.Equal(PlacedIds, placedEvents.Select(e => (int)e.A).ToArray());
        Assert.Equal(PlacedXs, placedEvents.Select(e => e.X).ToArray());

        Assert.Equal(CommandReason.NotAvailable, s.Apply(Ctx, new AutoPlaceUnpositioned()).Reason); // só uma vez
    }

    [Fact(DisplayName = "T-11.13 AutoPlaceUnpositioned: determinístico (mesmo estado → mesmo hash) e a pendência entra no hash")]
    public void AutoPlace_Deterministic()
    {
        ulong Run()
        {
            var s = Migrated();
            s.MigratedSlotsPending = true;
            s.Apply(Ctx, new AutoPlaceUnpositioned());
            for (int i = 0; i < 100; i++) s.Step(Ctx);
            return s.ComputeStateHash();
        }
        ulong first = Run();
        for (int i = 0; i < 4; i++) Assert.Equal(first, Run());
    }

    [Fact(DisplayName = "T-11.10 Sem torres posicionadas: sequência completa de comandos não altera Credits além dos custos")]
    public void No_Hidden_Costs()
    {
        var s = NewState(Layout(1000, new FreeZone(100, 300)));
        s.Apply(Ctx, new BuildTower(TowerType.Bolt, 150));
        long credits = s.Credits;
        for (int i = 0; i < 10; i++)
        {
            s.ApplyLayout(Layout(1000, new FreeZone(500, 700)));
            s.ApplyLayout(Layout(1000, new FreeZone(100, 300)));
        }
        Assert.Equal(credits, s.Credits);
        Assert.True(ById(s, Id(s, 0)).Placed);
    }
}
