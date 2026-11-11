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

    private CommandResult Build(SimContext ctx, BuildTower cmd)
    {
        if (!_layout.HasSlot(cmd.Slot)) return CommandResult.Reject(CommandReason.SlotMissing);
        if (!FindTowerAtSlot(cmd.Slot).IsNone) return CommandResult.Reject(CommandReason.SlotOccupied);
        if (cmd.Type == TowerType.None || !IsTowerUnlocked(ctx, cmd.Type)) return CommandResult.Reject(CommandReason.TowerLocked);
        if (Towers.Count >= Towers.Capacity) return CommandResult.Reject(CommandReason.TowerStoreFull);
        // n inclui torres SEM SLOT (ADR-032).
        if (!TowerCosts.TryBuildCost(ctx.Tower(cmd.Type).Cost, Towers.Count, ctx.Content.Economy.BuildCostFactor, out long cost))
            return CommandResult.Reject(CommandReason.Unreachable);
        if (Credits < cost) return CommandResult.Reject(CommandReason.InsufficientCredits, cost - Credits);

        Credits -= cost;
        Towers.Add(new Tower { Id = TakeId(), Type = cmd.Type, SlotIndex = cmd.Slot, Level = 1, Mode = Targeting.First });
        Events.Add(new SimEvent(SimEventType.TowerBuilt, _layout.SlotXAt(cmd.Slot), 0f, cost, (int)cmd.Type));
        return CommandResult.Ok;
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
        float x = TowerHasSlot(in tower) ? _layout.SlotXAt(tower.SlotIndex) : 0f;
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
        Events.Add(new SimEvent(SimEventType.LayoutChanged, cmd.Layout.SpawnX, 0f, cmd.Layout.SlotCount, 0));
        return CommandResult.Ok;
    }
}
