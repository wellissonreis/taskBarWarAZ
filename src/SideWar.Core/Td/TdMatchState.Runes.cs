using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;

namespace SideWar.Core.Td;

public sealed partial class TdMatchState
{
    private ulong _ownedRunes;
    private bool _hasLeaked;
    private readonly Dictionary<(string Sector, string Kind), int> _runeEffects = [];

    public bool OwnsRune(string id)
    {
        int i = Array.FindIndex(_content.Runes.Nodes, n => n.Id == id);
        return i >= 0 && (_ownedRunes & (1UL << i)) != 0;
    }

    public IReadOnlyList<string> OwnedRunes => _content.Runes.Nodes.Where(n => OwnsRune(n.Id)).Select(n => n.Id).ToArray();

    private bool Linked(int a, int b) => _content.Runes.Nodes[a].Links.Contains(_content.Runes.Nodes[b].Id, StringComparer.Ordinal)
        || _content.Runes.Nodes[b].Links.Contains(_content.Runes.Nodes[a].Id, StringComparer.Ordinal);

    private CommandResult DoBuyRune(BuyRune cmd)
    {
        if (!Running) return Reject(CommandReason.InvalidPhase);
        int i = Array.FindIndex(_content.Runes.Nodes, n => n.Id == cmd.RuneId);
        if (i < 0) return Reject(CommandReason.UnknownRune);
        if (OwnsRune(cmd.RuneId)) return Reject(CommandReason.RuneOwned);
        bool adjacent = false;
        for (int j = 0; j < _content.Runes.Nodes.Length; j++)
            if ((_ownedRunes & (1UL << j)) != 0 && Linked(i, j)) adjacent = true;
        if (!adjacent) return Reject(CommandReason.RuneNotAdjacent);
        int cost = _content.Runes.Nodes[i].Cost;
        if (Credits < cost) return Reject(CommandReason.InsufficientCredits, cost - Credits);
        Credits -= cost;
        _ownedRunes |= 1UL << i;
        RebuildRuneEffects();
        return CommandResult.Ok;
    }

    private CommandResult DoRefundRune(RefundRune cmd)
    {
        if (!Running) return Reject(CommandReason.InvalidPhase);
        int i = Array.FindIndex(_content.Runes.Nodes, n => n.Id == cmd.RuneId);
        if (i < 0) return Reject(CommandReason.UnknownRune);
        if (!OwnsRune(cmd.RuneId)) return Reject(CommandReason.RuneNotOwned);
        if (cmd.RuneId == _content.Runes.CenterId) return Reject(CommandReason.NotAvailable);
        ulong remaining = _ownedRunes & ~(1UL << i);
        int center = Array.FindIndex(_content.Runes.Nodes, n => n.Id == _content.Runes.CenterId);
        ulong connected = 1UL << center;
        bool changed;
        do
        {
            changed = false;
            for (int a = 0; a < _content.Runes.Nodes.Length; a++)
            {
                ulong bit = 1UL << a;
                if ((remaining & bit) == 0 || (connected & bit) != 0) continue;
                for (int b = 0; b < _content.Runes.Nodes.Length; b++)
                    if ((connected & (1UL << b)) != 0 && Linked(a, b))
                    { connected |= bit; changed = true; break; }
            }
        } while (changed);
        for (int a = 0; a < _content.Runes.Nodes.Length; a++)
            if ((_ownedRunes & ~connected & (1UL << a)) != 0)
                Credits = Sat.Add(Credits, _content.Runes.Nodes[a].Cost);
        _ownedRunes = connected;
        RebuildRuneEffects();
        return CommandResult.Ok;
    }

    private int Rune(string sector, string kind) => _runeEffects.GetValueOrDefault((sector, kind));
    private double RuneFactor(string sector, string kind) => 1.0 + Rune(sector, kind) / 10000.0;

    private void RebuildRuneEffects()
    {
        int oldIntegrity = Rune("general", "integrity");
        _runeEffects.Clear();
        foreach (var n in _content.Runes.Nodes)
            if (OwnsRune(n.Id))
                foreach (var e in n.Effects)
                {
                    var key = (e.Target ?? (n.Sector == "border" ? "general" : n.Sector), e.Kind);
                    _runeEffects[key] = _runeEffects.GetValueOrDefault(key) + e.Value;
                }
        int delta = Rune("general", "integrity") - oldIntegrity;
        IntegrityMax += delta;
        Integrity = Math.Clamp(Integrity + delta, 0, IntegrityMax);
        if (Integrity == 0 && Running)
        {
            Phase = MatchPhase.Derrota;
            CrateLeaveWave();
            Stars = 0;
            Emit(TdEventType.Defeat, WaveNumber, IntegrityLost);
        }
        // Atualiza efeitos dos nós existentes, preservando vida e temporizadores em curso.
        for (int i = 0; i < _nodes.Count; i++)
        {
            var n = _nodes[i];
            int ti = FindTower(n.TowerId);
            if (ti < 0) continue;
            var t = _towers[ti];
            var def = _content.Towers[t.DefIndex];
            n.Dps = Stat(t, def, "nodeDamagePerSecond", 0);
            n.Holds = (int)Stat(t, def, "nodeHolds", 1);
            n.Flags = (byte)(Stat(t, def, "explodeOnEnd", 0) > 0 ? 1 : 0);
            _nodes[i] = n;
        }
    }

    private int DistinctTowerTypes()
    {
        int count = 0;
        for (int i = 0; i < _content.Towers.Length; i++)
            if (TowerCount(i) > 0) count++;
        return count;
    }

    private void RefreshHeldTargets()
    {
        _held.Clear();
        foreach (var n in _nodes)
        {
            int count = 0;
            foreach (var e in _enemies)
            {
                if (count >= n.Holds) break;
                if (e.Flying || e.Hp <= 0 || Dist(e.X, n.X) > NodeCatchRadius || !_held.Add(e.Id)) continue;
                count++;
            }
        }
    }

    private double EffectiveRange(TdTowerDef def) => def.Range * RuneFactor(def.Id, "rangeBp");
}
