using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;

namespace SideWar.Core.Td;

/// <summary>
/// Teia de runas do design (TD.29a, SDD-29 §21; ampliada 4x): 128 nós fixos (8 anéis × 16 setores), 8 tipos de efeito. Despertar o nó
/// ESCOLHIDO da fronteira custa fragmentos conforme o anel (<c>RingCosts</c>) (TD.30d: sem sorteio, sem consumo de <c>Rng</c>). Persiste na corrida contínua; some com a partida.
/// </summary>
public sealed partial class TdMatchState
{
    private const int WebRings = TdContentValidator.WebRings;
    private const int WebSectors = TdContentValidator.WebSectors;

    private UInt128 _awakened;
    private bool _hasLeaked;
    private int[]? _webSlot; // anel*setores+setor → índice em Content.Runes.Nodes
    private Dictionary<string, int> _runeEffects = [];

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

    private static UInt128 Bit(int ring, int sector) => UInt128.One << Slot(ring, sector);

    private bool AwakenedAt(int ring, int sector) => (_awakened & Bit(ring, sector)) != UInt128.Zero;

    /// <summary>Custo em fragmentos para despertar o nó <paramref name="id"/> (por anel); 0 se o id é desconhecido.</summary>
    public int RuneCost(string id)
    {
        int i = NodeIndex(id);
        return i < 0 ? 0 : _content.Runes.RingCosts[_content.Runes.Nodes[i].Ring];
    }

    /// <summary>Despertada? (id desconhecido = false)</summary>
    public bool IsAwakened(string id)
    {
        int i = NodeIndex(id);
        if (i < 0) return false;
        var n = _content.Runes.Nodes[i];
        return AwakenedAt(n.Ring, n.Sector);
    }

    /// <summary>Quantidade de runas despertadas.</summary>
    public int AwakenedCount => (int)UInt128.PopCount(_awakened);

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

    /// <summary>Há nó na fronteira que os fragmentos atuais já pagam? (alerta e botão de runas)</summary>
    public bool CanAwakenAny
    {
        get
        {
            if (Fragments < 1) return false;
            foreach (var id in Frontier)
                if (Fragments >= RuneCost(id)) return true;
            return false;
        }
    }

    /// <summary>Bitset dos nós despertados (slot = anel*16+setor): o que o perfil persiste.</summary>
    public UInt128 AwakenedBits => _awakened;

    /// <summary>Carrega a teia permanente do perfil (nós + fragmentos) na partida nova; não consome <c>Rng</c>.</summary>
    private void ApplyPersistedRunes()
    {
        if (_options.IsClassic) return;
        _awakened = _options.InitialAwakened; // 8 anéis × 16 setores = 128 slots = todos os bits do UInt128
        if (_options.InitialFragments > 0) Fragments = _options.InitialFragments;
        if (_awakened != UInt128.Zero) RebuildRuneEffects();
    }

    private CommandResult DoAwakenRune(string nodeId)
    {
        if (!Running) return Reject(CommandReason.InvalidPhase);
        int ni = nodeId is null ? -1 : NodeIndex(nodeId);
        if (ni < 0) return Reject(CommandReason.UnknownRuneNode);
        var slots = WebSlots();
        var node = _content.Runes.Nodes[ni];
        int cost = _content.Runes.RingCosts[node.Ring];
        if (AwakenedCount >= WebRings * WebSectors) return Reject(CommandReason.WebComplete);
        if (!InFrontier(node.Ring, node.Sector)) return Reject(CommandReason.RuneNotInFrontier);
        if (Fragments < cost) return Reject(CommandReason.NoFragments, cost - Fragments);
        int pick = Slot(node.Ring, node.Sector);
        Fragments -= cost;
        _awakened |= Bit(node.Ring, node.Sector);
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
        _runeEffects.Clear();
        var types = _content.Runes.Types;
        foreach (var n in _content.Runes.Nodes)
        {
            if (!AwakenedAt(n.Ring, n.Sector)) continue;
            foreach (var t in types)
                if (t.Id == n.Type) _runeEffects[t.Effect] = _runeEffects.GetValueOrDefault(t.Effect) + t.Value * n.Power;
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
