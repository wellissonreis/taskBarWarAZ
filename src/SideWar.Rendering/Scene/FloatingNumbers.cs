using SideWar.Core.Simulation;

namespace SideWar.Rendering.Scene;

/// <summary>Cor/estilo de um número flutuante (SDD-22 RF-22.5).</summary>
public enum FloatKind : byte { Bolt, Cannon, Slow }

/// <summary>
/// Pool fixo de 24 números flutuantes (SDD-22 RF-22.5; SDD-08 RF-08.5): com o pool cheio, o mais antigo é substituído.
/// Sem alocação em regime: os textos são cacheados (<see cref="TextCache"/>). Puro, sem Skia.
/// </summary>
public sealed class FloatingNumbers
{
    public const int PoolSize = 24;

    public struct Item
    {
        public bool Active { get; set; }
        public float X { get; set; }
        public double Start { get; set; }
        public string Text { get; set; }
        public FloatKind Kind { get; set; }
    }

    private readonly Item[] _items = new Item[PoolSize];

    public int ActiveCount { get; private set; }
    public ReadOnlySpan<Item> Items => _items;

    public void Add(float x, double now, string text, FloatKind kind)
    {
        int slot = -1;
        double oldest = double.MaxValue;
        int oldestIdx = 0;
        for (int i = 0; i < _items.Length; i++)
        {
            if (!_items[i].Active) { slot = i; break; }
            if (_items[i].Start < oldest) { oldest = _items[i].Start; oldestIdx = i; }
        }
        if (slot < 0) slot = oldestIdx; // pool cheio: substitui o mais antigo
        _items[slot] = new Item { Active = true, X = x, Start = now, Text = text, Kind = kind };
        Recount();
    }

    /// <summary>Libera os números cuja animação (1,4 s) terminou.</summary>
    public void Expire(double now)
    {
        for (int i = 0; i < _items.Length; i++)
            if (_items[i].Active && now - _items[i].Start >= SceneGeometry.FloatSeconds) _items[i].Active = false;
        Recount();
    }

    public void Clear()
    {
        Array.Clear(_items);
        ActiveCount = 0;
    }

    private void Recount()
    {
        int n = 0;
        foreach (ref readonly var it in _items.AsSpan()) if (it.Active) n++;
        ActiveCount = n;
    }

    /// <summary>Progresso linear 0..1 da animação.</summary>
    public static float Progress(double now, double start) =>
        (float)Math.Clamp((now - start) / SceneGeometry.FloatSeconds, 0.0, 1.0);
}

/// <summary>Cache de textos pequenos ("14", "-96"): aloca só na primeira vez de cada valor.</summary>
public sealed class TextCache
{
    private readonly string?[] _levels = new string?[256];
    private readonly string?[] _damage = new string?[1024];

    /// <summary>Rótulo de nível: só o número ("14"), sem "LV" (ADR-034, DP-33.9).</summary>
    public string Number(int n)
    {
        if ((uint)n >= (uint)_levels.Length) return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return _levels[n] ??= n.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Dano inteiro com sinal: "-96".</summary>
    public string Damage(double dmg)
    {
        int v = (int)Math.Round(dmg, MidpointRounding.AwayFromZero);
        if ((uint)v >= (uint)_damage.Length) return "-" + v;
        return _damage[v] ??= "-" + v;
    }
}
