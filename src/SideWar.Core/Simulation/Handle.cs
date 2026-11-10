namespace SideWar.Core.Simulation;

/// <summary>
/// Referência estável a uma entidade (SDD-11 RF-11.2): bits 0..7 = índice (até 256), bits 8..31 = geração.
/// Gerações começam em 1; o valor 0 é <see cref="None"/>. Não é persistido.
/// </summary>
public readonly record struct Handle(int Value)
{
    public static readonly Handle None = new(0);
    public const int MaxCapacity = 256;
    public const int MaxGeneration = (1 << 24) - 1;

    public int Index => Value & 0xFF;
    public int Generation => Value >>> 8;
    public bool IsNone => Value == 0;

    public static Handle Make(int index, int generation) => new((generation << 8) | index);
}
