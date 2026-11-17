using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;

namespace SideWar.Core.Td;

/// <summary>
/// Teia de runas do design (TD.29a, SDD-29 §21): 32 nós fixos (4 anéis × 8 setores), 8 tipos de efeito. Cada FRAGMENTO desperta um nó
/// ALEATÓRIO da fronteira (sorteio pelo <c>Rng</c> da partida). Persiste na corrida contínua; some com a partida.
/// </summary>
public sealed partial class TdMatchState
{
    private const int WebRings = TdContentValidator.WebRings;
    private const int WebSectors = TdContentValidator.WebSectors;

    private uint _awakened;
    private bool _hasLeaked;
    private int[]? _webSlot; // anel*8+setor → índice em Content.Runes.Nodes
    private readonly Dictionary<string, int> _runeEffects = [];

    /// <summary>Fragmentos disponíveis (moeda das runas; créditos ficam só para torres).</summary>
    public long Fragments { get; private set; }

    /// <summary>Nós fixos da teia, na ordem do conteúdo (Id, Ring, Sector, Type, X/Y normalizados).</summary>
    public IReadOnlyList<TdRuneNodeDef> RuneWeb => _content.Runes.Nodes;

    private static int Slot(int ring, int sector) => ring * WebSectors + sector;

    private int NodeIndex(string id)
    {
        var nodes = _content.Runes.Nodes;
        for (int i = 0; i < nodes.Length; i++)
            if (nodes[i].Id == id) return i;
        return -1;
    }

    private int[] WebSlots()
    {
        if (_webSlot is not null) return _webSlot;
        var slots = new int[WebRings * WebSectors];
        var nodes = _content.Runes.Nodes;
        for (int i = 0; i < nodes.Length; i++) slots[Slot(nodes[i].Ring, nodes[i].Sector)] = i;
        return _webSlot = slots;
    }

    private bool AwakenedAt(int ring, int sector) => (_awakened & (1u << Slot(ring, sector))) != 0;

    /// <summary>Despertada? (id desconhecido = false)</summary>
    public bool IsAwakened(string id)
    {
        int i = NodeIndex(id);
        if (i < 0) return false;
        var n = _content.Runes.Nodes[i];
        return AwakenedAt(n.Ring, n.Sector);
    }

    /// <summary>Quantidade de runas despertadas.</summary>
    public int AwakenedCount => System.Numerics.BitOperations.PopCount(_awakened);

    /// <summary>Quantas runas despertadas do tipo <paramref name="typeId"/> (por id do tipo).</summary>
    public int AwakenedOfType(string typeId)
    {
        int n = 0;
        foreach (var node in _content.Runes.Nodes)
            if (node.Type == typeId && AwakenedAt(node.Ring, node.Sector)) n++;
        return n;
    }

    private bool InFrontier(int ring, int sector)
    {
        if (AwakenedAt(ring, sector)) return false;
        if (ring == 0) return true; // o anel interno liga ao centro
        return AwakenedAt(ring, (sector + 1) % WebSectors) || AwakenedAt(ring, (sector + WebSectors - 1) % WebSectors)
            || AwakenedAt(ring - 1, sector) || (ring < WebRings - 1 && AwakenedAt(ring + 1, sector));
    }

    /// <summary>Nós despertáveis agora: não despertados, no anel 0 ou vizinhos de um despertado (mesmo anel setor ±1, anel interno e externo no mesmo setor).</summary>
    public IReadOnlyList<string> Frontier
    {
        get
        {
            var list = new List<string>();
            var slots = WebSlots();
            for (int r = 0; r < WebRings; r++)
                for (int k = 0; k < WebSectors; k++)
                    if (InFrontier(r, k)) list.Add(_content.Runes.Nodes[slots[Slot(r, k)]].Id);
            return list;
        }
    }

    private CommandResult DoAwakenRune()
    {
        if (!Running) return Reject(CommandReason.InvalidPhase);
        if (Fragments < 1) return Reject(CommandReason.NoFragments, 1 - Fragments);
        var slots = WebSlots();
        Span<int> frontier = stackalloc int[WebRings * WebSectors];
        int count = 0;
        for (int r = 0; r < WebRings; r++)
            for (int k = 0; k < WebSectors; k++)
                if (InFrontier(r, k)) frontier[count++] = Slot(r, k);
        if (count == 0) return Reject(CommandReason.WebComplete);
        int pick = frontier[(int)(_rng.NextUInt() % (uint)count)];
        Fragments--;
        _awakened |= 1u << pick;
        RebuildRuneEffects();
        Emit(TdEventType.RuneAwakened, slots[pick], Fragments);
        return CommandResult.Ok;
    }

    private void GainFragments(int amount)
    {
        if (amount > 0) Fragments = Sat.Add(Fragments, amount);
    }

    private int Rune(string effect) => _runeEffects.GetValueOrDefault(effect);

    private void RebuildRuneEffects()
    {
        int oldIntegrity = Rune("integrity");
        _runeEffects.Clear();
        var types = _content.Runes.Types;
        foreach (var n in _content.Runes.Nodes)
        {
            if (!AwakenedAt(n.Ring, n.Sector)) continue;
            foreach (var t in types)
                if (t.Id == n.Type) _runeEffects[t.Effect] = _runeEffects.GetValueOrDefault(t.Effect) + t.Value;
        }
        int delta = Rune("integrity") - oldIntegrity;
        IntegrityMax += delta;
        Integrity = Math.Clamp(Integrity + delta, 0, IntegrityMax);
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

    private double EffectiveRange(TdTowerDef def) => def.Range * (1.0 + Rune("rangeBp") / 10000.0);
}
