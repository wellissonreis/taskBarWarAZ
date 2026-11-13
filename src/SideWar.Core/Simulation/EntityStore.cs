namespace SideWar.Core.Simulation;

/// <summary>
/// Array de structs de capacidade fixa com slots estáveis, free list e geração (SDD-11 RF-11.2).
/// Consulta por handle O(1), validando a geração; sem alocação após a construção.
/// Os índices livres saem em ordem crescente na partida limpa; após remoções, o último liberado é o próximo (LIFO).
/// </summary>
public abstract class EntityStore<T> where T : struct, IEntity
{
    private readonly T[] _items;
    private readonly bool[] _alive;
    private readonly int[] _generation;
    private readonly int[] _free;
    private int _freeCount;

    protected EntityStore(int capacity)
    {
        if (capacity is < 1 or > Handle.MaxCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _items = new T[capacity];
        _alive = new bool[capacity];
        _generation = new int[capacity];
        _free = new int[capacity];
        Reset();
    }

    public int Capacity => _items.Length;
    public int Count { get; private set; }

    private void Reset()
    {
        for (int i = 0; i < _items.Length; i++)
        {
            _generation[i] = 1;
            _free[i] = _items.Length - 1 - i; // topo da pilha = índice 0
        }
        _freeCount = _items.Length;
        Count = 0;
    }

    /// <summary>Esvazia o store e reinicia as gerações (restore/novo jogo). Handles antigos deixam de valer.</summary>
    public void Clear()
    {
        Array.Clear(_items);
        Array.Clear(_alive);
        Reset();
    }

    /// <summary>Insere; devolve <see cref="Handle.None"/> se o store está cheio.</summary>
    public Handle Add(in T value)
    {
        if (_freeCount == 0) return Handle.None;
        int index = _free[--_freeCount];
        _items[index] = value;
        _alive[index] = true;
        Count++;
        return Handle.Make(index, _generation[index]);
    }

    public bool IsAlive(int index) => (uint)index < (uint)_alive.Length && _alive[index];

    public bool IsValid(Handle handle)
    {
        int i = handle.Index;
        return !handle.IsNone && _alive[i] && _generation[i] == handle.Generation;
    }

    /// <summary>Referência à entidade; lança se o handle for inválido ou obsoleto.</summary>
    public ref T Get(Handle handle)
    {
        if (!IsValid(handle)) throw new InvalidOperationException("Handle inválido ou obsoleto.");
        return ref _items[handle.Index];
    }

    public bool TryGet(Handle handle, out T value)
    {
        if (IsValid(handle)) { value = _items[handle.Index]; return true; }
        value = default;
        return false;
    }

    /// <summary>Acesso por índice de slot (iteração <c>for i in 0..Capacity-1 if IsAlive(i)</c>).</summary>
    public ref T At(int index)
    {
        if (!IsAlive(index)) throw new InvalidOperationException("Slot vazio.");
        return ref _items[index];
    }

    public Handle HandleAt(int index)
    {
        if (!IsAlive(index)) throw new InvalidOperationException("Slot vazio.");
        return Handle.Make(index, _generation[index]);
    }

    /// <summary>Remove; incrementa a geração (volta a 1 ao saturar 2²⁴). Devolve false se o handle é inválido.</summary>
    public bool Remove(Handle handle)
    {
        if (!IsValid(handle)) return false;
        int i = handle.Index;
        _alive[i] = false;
        _items[i] = default;
        _generation[i] = _generation[i] >= Handle.MaxGeneration ? 1 : _generation[i] + 1;
        _free[_freeCount++] = i;
        Count--;
        return true;
    }

    /// <summary>Escreve os índices vivos em ordem crescente de <c>Id</c> (insertion sort, sem alocação). Devolve a quantidade.</summary>
    public int CopyIndicesByIdAscending(Span<int> destination)
    {
        if (destination.Length < Count) throw new ArgumentException("Destino pequeno.", nameof(destination));
        int n = 0;
        for (int i = 0; i < _items.Length; i++)
        {
            if (!_alive[i]) continue;
            int id = _items[i].Id;
            int j = n++;
            while (j > 0 && _items[destination[j - 1]].Id > id) { destination[j] = destination[j - 1]; j--; }
            destination[j] = i;
        }
        return n;
    }
}

/// <summary>Capacidade 64 (limite de campo 44 + margem; SDD-11 RF-11.2).</summary>
public sealed class EnemyStore() : EntityStore<Enemy>(CapacityConst)
{
    public const int CapacityConst = 64;
}

/// <summary>Capacidade 32; limite de jogo de 24 torres, inclusive as SEM POSIÇÃO (SDD-11 RF-11.2, DP-33.3).</summary>
public sealed class TowerStore() : EntityStore<Tower>(CapacityConst)
{
    public const int CapacityConst = 32;
    public const int MaxTowers = 24;
}
