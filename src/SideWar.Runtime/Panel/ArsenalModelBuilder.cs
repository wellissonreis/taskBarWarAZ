using System.Globalization;
using SideWar.Core.Content;
using SideWar.Core.Economy;
using SideWar.Core.Simulation;

namespace SideWar.Runtime.Panel;

/// <summary>
/// Monta o <see cref="ArsenalModel"/> a partir do estado (SDD-24 RF-24.5; custos de SDD-27 RF-27.5 via
/// <see cref="TowerCosts"/>). Leitura pura: não altera o estado e não aplica comandos. Roda na T-Game, por mudança
/// de dados (não por frame).
/// </summary>
public static class ArsenalModelBuilder
{
    /// <param name="focusSlot">Slot vazio focado por <c>OpenArsenal(slotIndex)</c>; só gera linhas CONSTRUIR se o slot existe e está livre.</param>
    public static ArsenalModel Build(SimState state, SimContext ctx, bool autoScaleOn, int? focusSlot = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ctx);
        var content = ctx.Content;
        var econ = content.Economy;
        var rows = new List<ArsenalRowModel>();

        int? buildSlot = null;
        // ADR-034 (compilação mínima; UI real é TA.11): "slot" focado passa a ser o X da posição livre.
        if (focusSlot is { } fs && PlacementValidator.Validate(fs, state.Layout, state.Towers) == PlacementReason.None) buildSlot = fs;

        if (buildSlot is { } slot)
        {
            foreach (var def in content.Towers)
            {
                if (!SimIds.TryTowerType(def.Id, out var type) || !state.IsTowerUnlocked(ctx, type)) continue;
                bool ok = TowerCosts.TryBuildCost(def.Cost, state.Towers.Count, econ.BuildCostFactor, out long cost);
                rows.Add(new ArsenalRowModel(ArsenalRowKind.Build, type, 0, slot, def.Name.ToUpperInvariant(), "LV 1", false,
                    def.AzureTheme, Stats(ctx, def, type, 1), "CONSTRUIR", ok ? CreditFormatter.Format(cost) : CreditFormatter.Unreachable,
                    ok ? ArsenalButtonStyle.Amber : ArsenalButtonStyle.Locked));
            }
        }

        var owned = new List<(TowerType Type, int Slot, int Id, int Level, bool Placed)>();
        for (int i = 0; i < state.Towers.Capacity; i++)
        {
            if (!state.Towers.IsAlive(i)) continue;
            ref readonly var t = ref state.Towers.At(i);
            owned.Add((t.Type, state.TowerCenterX(in t), t.Id, t.Level, t.Placed));
        }
        owned.Sort(static (a, b) =>
        {
            int c = a.Type.CompareTo(b.Type);
            if (c != 0) return c;
            c = a.Slot.CompareTo(b.Slot);
            return c != 0 ? c : a.Id.CompareTo(b.Id);
        });

        foreach (bool withSlot in new[] { true, false })
        {
            foreach (var (type, tslot, id, level, placed) in owned)
            {
                if (placed != withSlot) continue;
                var def = DefOf(content, type);
                if (def is null) continue;
                rows.Add(withSlot ? TowerRow(ctx, def, type, id, tslot, level) : NoSlotRow(ctx, def, type, id, tslot, level));
            }
        }

        foreach (var def in content.Towers)
        {
            if (!SimIds.TryTowerType(def.Id, out var type) || state.IsTowerUnlocked(ctx, type)) continue;
            int unlock = def.UnlockStage ?? 1;
            rows.Add(new ArsenalRowModel(ArsenalRowKind.Locked, type, 0, -1, def.Name.ToUpperInvariant(), "BLOQ.", false,
                def.AzureTheme, $"Desbloqueia na fase {unlock:00}", $"FASE {unlock:00}", string.Empty, ArsenalButtonStyle.Locked));
        }

        return new ArsenalModel(autoScaleOn, buildSlot, rows);
    }

    private static ArsenalRowModel TowerRow(SimContext ctx, TowerDef def, TowerType type, int id, int slot, int level)
    {
        var econ = ctx.Content.Economy;
        if (level >= econ.MaxTowerLevel)
            return Row(ctx, def, type, id, slot, level, ArsenalRowKind.Tower, "NÍVEL MÁX.", string.Empty, ArsenalButtonStyle.Locked);
        bool ok = TowerCosts.TryUpgradeCost(ctx.Tower(type).Cost, level, econ.UpgradeCostFactor, econ.UpgradeCostGrowth, out long cost);
        return Row(ctx, def, type, id, slot, level, ArsenalRowKind.Tower, "MELHORAR",
            ok ? CreditFormatter.Format(cost) : CreditFormatter.Unreachable, ok ? ArsenalButtonStyle.Amber : ArsenalButtonStyle.Locked);
    }

    private static ArsenalRowModel NoSlotRow(SimContext ctx, TowerDef def, TowerType type, int id, int slot, int level) =>
        Row(ctx, def, type, id, slot, level, ArsenalRowKind.NoSlot, "SEM SLOT", string.Empty, ArsenalButtonStyle.Locked);

    private static ArsenalRowModel Row(SimContext ctx, TowerDef def, TowerType type, int id, int slot, int level, ArsenalRowKind kind,
        string label, string cost, ArsenalButtonStyle style) =>
        new(kind, type, id, slot, def.Name.ToUpperInvariant(), kind == ArsenalRowKind.NoSlot ? "SEM SLOT" : $"LV {level}", kind == ArsenalRowKind.NoSlot,
            def.AzureTheme, Stats(ctx, def, type, level), label, cost, style);

    private static TowerDef? DefOf(GameContent content, TowerType type)
    {
        foreach (var t in content.Towers)
            if (SimIds.TryTowerType(t.Id, out var tt) && tt == type) return t;
        return null;
    }

    /// <summary>Estatísticas do design: Raio <c>Dano · Alc · Cad</c>; Gelo <c>Lentidão · Alc</c>; Canhão <c>Dano em área · Cad</c>; Scale Set por analogia (DP-24.5).</summary>
    public static string Stats(SimContext ctx, TowerDef def, TowerType type, int level)
    {
        string dmg = CreditFormatter.Format(ctx.BaseDamage(type, level));
        string cad = Decimal(def.Interval);
        return type switch
        {
            TowerType.Frost => $"Lentidão {(int)Math.Round((1.0 - ctx.Rules.FrostSlowFactor) * 100.0, MidpointRounding.AwayFromZero)}% · Alc {def.RangeSlots}",
            TowerType.Cannon => $"Dano {dmg} em área · Cad {cad}s",
            _ => $"Dano {dmg} · Alc {def.RangeSlots} · Cad {cad}s",
        };
    }

    private static string Decimal(double v) => v.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',');
}
