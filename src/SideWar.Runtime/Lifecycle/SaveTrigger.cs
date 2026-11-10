namespace SideWar.Runtime.Lifecycle;

/// <summary>Why a save is requested. <see cref="Suspended"/> demands a synchronous save (RF-07.7).</summary>
public enum SaveTrigger { Hidden, Suspended }
