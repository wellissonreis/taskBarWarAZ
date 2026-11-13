using SideWar.Core.Content;

namespace SideWar.Core.Simulation;

/// <summary>Comandos aplicados entre ticks (SDD-11 RF-11.10, SDD-27 RF-27.5, ADR-026, ADR-032).</summary>
public sealed partial class SimState
{
    /// <summary>Melhorias manuais (missão de RF-27.11); melhorias da auto-escala (SDD-25) não contam. Entra no hash.</summary>
    public long ManualUpgrades { get; set; }

    /// <summary>
    /// Aplica um comando <b>entre ticks</b> (lança durante um tick). Debita créditos só em caso de sucesso.
    /// Comando desconhecido ou nulo → <see cref="CommandReason.UnknownCommand"/>.
    /// </summary>
    public CommandResult Apply(SimContext ctx, IGameCommand? command)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (InTick) throw new InvalidOperationException("Comandos só podem ser aplicados entre ticks.");
        return command switch
        {
            BuildTower b => Build(ctx, b),
            PlaceTower p => Place(p),
            AutoPlaceUnpositioned => AutoPlace(),
            UpgradeTower u => Upgrade(ctx, u),
            SetTargeting s => SetMode(s),
            LayoutChanged l => ApplyLayoutCommand(l),
            ClickPeakBanner => ClickPeak(),
            OpenBossQuestion => OpenBoss(),
            AnswerBossQuestion a => AnswerBoss(ctx, a),
            ClickSupplyCrate => ClickCrate(ctx),
            MigrateTo m => Prestige.PrestigeService.Migrate(this, ctx, m.RegionIndex),
            _ => CommandResult.Reject(CommandReason.UnknownCommand),
        };
    }

    /// <summary>Handle da torre de <c>Id</c> dado, ou <see cref="Handle.None"/>.</summary>
    public Handle FindTowerById(int id)
    {
        for (int i = 0; i < Towers.Capacity; i++)
            if (Towers.IsAlive(i) && Towers.At(i).Id == id) return Towers.HandleAt(i);
        return Handle.None;
    }

    /// <summary>Torre desbloqueada: sem <c>unlockStage</c> ou fase atual ≥ ele (a trava por migração/Reservas é da progressão, T3.8+).</summary>
    public bool IsTowerUnlocked(SimContext ctx, TowerType type)
    {
        var def = FindTowerDef(ctx.Content, type);
        return def is not null && (def.UnlockStage is null || Stage >= def.UnlockStage.Value);
    }

    private static TowerDef? FindTowerDef(GameContent content, TowerType type)
    {
        foreach (var t in content.Towers)
            if (SimIds.TryTowerType(t.Id, out var tt) && tt == type) return t;
        return null;
    }

    private static CommandReason ToReason(PlacementReason r)
        => r == PlacementReason.OutOfZone ? CommandReason.OutOfZone : CommandReason.Overlap;

    private CommandResult Build(SimContext ctx, BuildTower cmd)
    {
        var placement = PlacementValidator.Validate(cmd.X, _layout, Towers);
        if (placement != PlacementReason.None) return CommandResult.Reject(ToReason(placement));
        if (Towers.Count >= TowerStore.MaxTowers) return CommandResult.Reject(CommandReason.TowerLimit);
        if (cmd.Type == TowerType.None || !IsTowerUnlocked(ctx, cmd.Type)) return CommandResult.Reject(CommandReason.TowerLocked);
        // n inclui torres SEM POSIÇÃO (ADR-032).
        if (!TowerCosts.TryBuildCost(ctx.Tower(cmd.Type).Cost, Towers.Count, ctx.Content.Economy.BuildCostFactor, out long cost))
            return CommandResult.Reject(CommandReason.Unreachable);
        if (Credits < cost) return CommandResult.Reject(CommandReason.InsufficientCredits, cost - Credits);

        Credits -= cost;
        // DP-33.2: âncora pela borda externa mais próxima, decidida na construção.
        var (anchor, offset) = AnchorFor(cmd.X);
        Towers.Add(new Tower { Id = TakeId(), Type = cmd.Type, Anchor = anchor, OffsetDip = offset, Placed = true, Level = 1, Mode = Targeting.First });
        Events.Add(new SimEvent(SimEventType.TowerBuilt, cmd.X, 0f, cost, (int)cmd.Type));
        return CommandResult.Ok;
    }

    /// <summary><c>Start</c> se <c>x &lt; W/2</c>, senão <c>End</c> (DP-33.2); deslocamento inteiro ≥ 0 (zonas ficam dentro de [0, W]).</summary>
    private (TowerAnchor Anchor, int Offset) AnchorFor(int x)
        => 2L * x < _layout.Width ? (TowerAnchor.Start, x) : (TowerAnchor.End, _layout.Width - x);

    private CommandResult Place(PlaceTower cmd)
    {
        var h = FindTowerById(cmd.TowerId);
        if (h.IsNone) return CommandResult.Reject(CommandReason.TowerNotFound);
        ref var tower = ref Towers.Get(h);
        if (tower.Placed) return CommandResult.Reject(CommandReason.NotUnpositioned);
        var placement = PlacementValidator.Validate(cmd.X, _layout, Towers, tower.Id);
        if (placement != PlacementReason.None) return CommandResult.Reject(ToReason(placement));
        PlaceAt(ref tower, cmd.X);
        return CommandResult.Ok;
    }

    private void PlaceAt(ref Tower tower, int x)
    {
        var (anchor, offset) = AnchorFor(x);
        tower.Anchor = anchor;
        tower.OffsetDip = offset;
        tower.Placed = true;
        Events.Add(new SimEvent(SimEventType.TowerPlaced, x, 0f, tower.Id, (int)tower.Type));
    }

    /// <summary>
    /// DP-33.7: para cada torre SEM POSIÇÃO em ordem de <c>Id</c>, a primeira posição válida varrendo as zonas da esquerda
    /// para a direita em passos de 44 DIP a partir de <c>X0 + 22</c>; limpa a pendência. As que não couberem continuam SEM POSIÇÃO.
    /// </summary>
    private CommandResult AutoPlace()
    {
        if (!MigratedSlotsPending) return CommandResult.Reject(CommandReason.NotAvailable);
        Span<int> order = stackalloc int[TowerStore.CapacityConst];
        int n = Towers.CopyIndicesByIdAscending(order);
        for (int k = 0; k < n; k++)
        {
            ref var t = ref Towers.At(order[k]);
            if (t.Placed) continue;
            if (TryFindFirstFree(t.Id, out int x)) PlaceAt(ref t, x);
        }
        MigratedSlotsPending = false;
        return CommandResult.Ok;
    }

    private bool TryFindFirstFree(int ignoreId, out int found)
    {
        foreach (var z in _layout.FreeZones)
        {
            for (int x = z.X0 + PlacementValidator.HalfBox; x + PlacementValidator.HalfBox <= z.X1; x += PlacementValidator.BoxWidth)
            {
                if (PlacementValidator.Validate(x, _layout, Towers, ignoreId) == PlacementReason.None) { found = x; return true; }
            }
        }
        found = 0;
        return false;
    }

    private CommandResult Upgrade(SimContext ctx, UpgradeTower cmd)
    {
        var h = FindTowerById(cmd.TowerId);
        if (h.IsNone) return CommandResult.Reject(CommandReason.TowerNotFound);
        ref var tower = ref Towers.Get(h);
        var econ = ctx.Content.Economy;
        if (tower.Level >= econ.MaxTowerLevel) return CommandResult.Reject(CommandReason.MaxLevel);
        if (!TowerCosts.TryUpgradeCost(ctx.Tower(tower.Type).Cost, tower.Level, econ.UpgradeCostFactor, econ.UpgradeCostGrowth, out long cost))
            return CommandResult.Reject(CommandReason.Unreachable);
        if (Credits < cost) return CommandResult.Reject(CommandReason.InsufficientCredits, cost - Credits);

        Credits -= cost;
        tower.Level++;
        ManualUpgrades++;
        float x = tower.Placed ? TowerCenterX(in tower) : 0f;
        Events.Add(new SimEvent(SimEventType.TowerUpgraded, x, 0f, cost, tower.Level));
        return CommandResult.Ok;
    }

    private CommandResult SetMode(SetTargeting cmd)
    {
        var h = FindTowerById(cmd.TowerId);
        if (h.IsNone) return CommandResult.Reject(CommandReason.TowerNotFound);
        if (!Enum.IsDefined(cmd.Mode)) return CommandResult.Reject(CommandReason.UnknownCommand);
        Towers.Get(h).Mode = cmd.Mode;
        return CommandResult.Ok;
    }

    private CommandResult ApplyLayoutCommand(LayoutChanged cmd)
    {
        if (cmd.Layout is null) return CommandResult.Reject(CommandReason.InvalidLayout);
        ApplyLayout(cmd.Layout);
        Events.Add(new SimEvent(SimEventType.LayoutChanged, cmd.Layout.SpawnX, 0f, cmd.Layout.FreeZoneCount, 0));
        return CommandResult.Ok;
    }
}
