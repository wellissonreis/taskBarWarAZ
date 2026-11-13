using SideWar.Runtime.Lifecycle;

namespace SideWar.Runtime.Composition;

/// <summary>
/// Executor dos efeitos da <see cref="AppStateMachine"/> (SDD-07 RF-07.1) do lado da plataforma: janelas, save,
/// caches e EcoQoS. <c>Pause</c>/<c>Resume</c> não chegam aqui (o laço lógico lê <c>SimulationRunning</c>).
/// Chamado na T-Game, na ordem emitida pela máquina.
/// </summary>
public interface IAppEffectHandler
{
    void Show(HiddenReason cause);
    void Hide(HiddenReason cause);
    void Save(SaveTrigger trigger);
    void ReleaseCaches();
    void RestoreContext();
    void SetEcoQos(bool enabled);
}
