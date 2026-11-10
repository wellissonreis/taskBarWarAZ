namespace SideWar.Core.Simulation;

/// <summary>
/// Comando <c>LayoutChanged</c> (SDD-11 RF-11.3/RF-11.10, ADR-026): substitui o layout inteiro.
/// Aplicado só entre ticks, via <see cref="SimState.ApplyLayout"/>. A validação de comando e o evento ficam na T3.7.
/// </summary>
public sealed record LayoutChanged(StripLayout Layout) : IGameCommand;
