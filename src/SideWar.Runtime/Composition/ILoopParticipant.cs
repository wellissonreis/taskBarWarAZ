namespace SideWar.Runtime.Composition;

/// <summary>
/// Serviço acordado pelo laço da T-Game ao lado do <c>GameHost</c> (SDD-01 RF-01.5): barra de tarefas, fullscreen,
/// widget. Só na T-Game. <see cref="NextDeadline"/> em ticks do <see cref="IClock"/>; null = nada agendado.
/// </summary>
public interface ILoopParticipant
{
    long? NextDeadline { get; }
    void OnWake(IClock clock);
}
