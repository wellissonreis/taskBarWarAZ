namespace SideWar.Runtime.Timing;

public readonly record struct RenderDecision(bool RenderNow, double? NextDeadline);
