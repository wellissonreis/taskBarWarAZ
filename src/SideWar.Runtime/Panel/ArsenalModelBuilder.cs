using System.Globalization;
using SideWar.Core.Content;
using SideWar.Core.Economy;
using SideWar.Core.Simulation;
using SideWar.Runtime.Localization;

namespace SideWar.Runtime.Panel;

/// <summary>
/// Monta o <see cref="ArsenalModel"/> a partir do estado (SDD-24 RF-24.5; custos de SDD-27 RF-27.5 via
/// <see cref="TowerCosts"/>). Leitura pura: não altera o estado e não aplica comandos. Roda na T-Game, por mudança
/// de dados (não por frame).
/// </summary>
public static class ArsenalModelBuilder
{
    public static ArsenalModel Build(SimState state, SimContext ctx, bool autoScaleOn)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ctx);
        var content = ctx.Content;
        var econ = content.Economy;
        var rows = new List<ArsenalRowModel>();

        var owned = new List<(TowerType Type, int X, int Id, int Level, bool Placed)>();
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
            c = a.X.CompareTo(b.X);
            return c != 0 ? c : a.Id.CompareTo(b.Id);
        });

        // 5.2: por tipo desbloqueado, na ordem do catálogo: as torres posicionadas (ou uma linha "NOVA" se não houver nenhuma).
        foreach (var def in content.Towers)
        {
            if (!SimIds.TryTowerType(def.Id, out var type) || !state.IsTowerUnlocked(ctx, type)) continue;
            bool any = false;
            foreach (var (otype, _, id, level, placed) in owned)
            {
                if (otype != type || !placed) continue;
                any = true;
                rows.Add(TowerRow(state, ctx, def, type, id, level));
            }
            if (!any) rows.Add(AvailableRow(state, ctx, def, type));
        }

        // 5.3: SEM POSIÇÃO, depois das posicionadas.
        foreach (var (type, _, id, level, placed) in owned)
        {
            if (placed) continue;
            var def = DefOf(content, type);
            if (def is null) continue;
            rows.Add(new ArsenalRowModel(ArsenalRowKind.NoPosition, type, id, def.Name.ToUpperInvariant(), Strings.ArsenalNoPositionSeal, true,
                def.AzureTheme, Stats(ctx, def, type, level), Strings.ArsenalReposition, string.Empty, ArsenalButtonStyle.Amber));
        }

        foreach (var def in content.Towers)
        {
            if (!SimIds.TryTowerType(def.Id, out var type) || state.IsTowerUnlocked(ctx, type)) continue;
            int unlock = def.UnlockStage ?? 1;
            rows.Add(new ArsenalRowModel(ArsenalRowKind.Locked, type, 0, def.Name.ToUpperInvariant(), Strings.ArsenalLockedSeal, false,
                def.AzureTheme, Strings.ArsenalUnlocksAtStage(unlock), Strings.ArsenalUnlockStage(unlock), string.Empty, ArsenalButtonStyle.Locked));
        }

        return new ArsenalModel(autoScaleOn, rows);
    }

    /// <summary>Ícone arrastável: tipo desbloqueado, créditos para construir e menos de 24 torres (DP-33.3). Título da dica = nome + custo.</summary>
    private static (bool Enabled, string Hint) Icon(SimState state, SimContext ctx, TowerDef def)
    {
        if (state.Towers.Count >= TowerStore.MaxTowers
            || !TowerCosts.TryBuildCost(def.Cost, state.Towers.Count, ctx.Content.Economy.BuildCostFactor, out long cost)
            || state.Credits < cost) return (false, string.Empty);
        return (true, Strings.ArsenalBuildHint(def.Name.ToUpperInvariant(), CreditFormatter.Format(cost)));
    }

    private static ArsenalRowModel AvailableRow(SimState state, SimContext ctx, TowerDef def, TowerType type)
    {
        var (enabled, hint) = Icon(state, ctx, def);
        return new ArsenalRowModel(ArsenalRowKind.Available, type, 0, def.Name.ToUpperInvariant(), Strings.ArsenalNewSeal, true,
            def.AzureTheme, Stats(ctx, def, type, 1), Strings.ArsenalNewButton, string.Empty, ArsenalButtonStyle.Locked, enabled, hint);
    }

    private static ArsenalRowModel TowerRow(SimState state, SimContext ctx, TowerDef def, TowerType type, int id, int level)
    {
        var econ = ctx.Content.Economy;
        var (enabled, hint) = Icon(state, ctx, def);
        string label = Strings.ArsenalUpgrade, costText = string.Empty;
        var style = ArsenalButtonStyle.Locked;
        if (level >= econ.MaxTowerLevel) label = Strings.ArsenalMaxLevel;
        else if (TowerCosts.TryUpgradeCost(ctx.Tower(type).Cost, level, econ.UpgradeCostFactor, econ.UpgradeCostGrowth, out long cost))
        {
            costText = CreditFormatter.Format(cost);
            style = ArsenalButtonStyle.Amber;
        }
        else costText = CreditFormatter.Unreachable;
        return new ArsenalRowModel(ArsenalRowKind.Tower, type, id, def.Name.ToUpperInvariant(), Strings.ArsenalLevel(level), false,
            def.AzureTheme, Stats(ctx, def, type, level), label, costText, style, enabled, hint);
    }

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
