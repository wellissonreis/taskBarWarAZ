namespace SideWar.Runtime.Hosting;

/// <summary>
/// Estado da partida (SDD-10 RF-10.5, rev2): <c>NoRun</c> é só o transitório até o Runtime criar ou restaurar a partida;
/// a simulação avança e os comandos valem somente em <c>InStage</c>.
/// </summary>
public enum RunState { NoRun, InStage }
