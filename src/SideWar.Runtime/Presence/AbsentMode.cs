using SideWar.Runtime.Timing;

namespace SideWar.Runtime.Presence;

/// <summary>
/// Regras puras do modo Ausente (SDD-25 RF-25.4). A simulação NÃO muda: segue a 30 Hz; só o render cai para
/// <c>AbsentFps</c> (consumido por <see cref="RenderPolicy"/> / T4.10). Sem pausa.
/// </summary>
public static class AbsentMode
{
    /// <summary>Ticks de simulação em <paramref name="seconds"/> (30 Hz fixos, igual em Presente e Ausente).</summary>
    public static long SimTicksFor(double seconds) => (long)Math.Round(seconds * LoopConstants.SimHz);

    /// <summary>Fps de render para a presença: Ausente usa <paramref name="absentFps"/> (4/8); Presente, <paramref name="activeFps"/>.</summary>
    public static int RenderFps(PresenceState presence, int activeFps = LoopConstants.DefaultActiveFps, int absentFps = LoopConstants.AmbientFps) =>
        presence == PresenceState.Ausente ? absentFps : activeFps;

    /// <summary>
    /// Onda que a simulação deve jogar quando a próxima é <paramref name="nextWave"/>. Em Ausente, se a próxima é de chefe,
    /// repete <c>w − 1</c> em loop (a <c>ondaMax</c> não avança); caso contrário segue normal. Presente: sempre <paramref name="nextWave"/>.
    /// </summary>
    public static int WaveToPlay(PresenceState presence, int nextWave, bool nextIsBoss) =>
        presence == PresenceState.Ausente && nextIsBoss && nextWave > 1 ? nextWave - 1 : nextWave;

    /// <summary>Eventos de SDD-26 (pico, caixa, chefe) só existem em Presente (RF-25.4).</summary>
    public static bool LiveEventsAllowed(PresenceState presence) => presence == PresenceState.Presente;

    /// <summary>EcoQoS ligado em Ausente (DP-25.6).</summary>
    public static bool EcoQosOn(PresenceState presence) => presence == PresenceState.Ausente;
}
