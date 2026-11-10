using SideWar.Runtime.Power;
using SideWar.Runtime.Timing;

namespace SideWar.Rendering.Animation;

/// <summary>Política de animação (SDD-20 RF-20.6; SDD-22 RF-22.10). Pura e sem alocação.</summary>
public readonly record struct AnimationPolicy(bool ReducedMotion, PowerProfile Profile)
{
    /// <summary>Limite de fps do pulso (SDD-22 RF-22.10).</summary>
    public const int PulseMaxFps = 15;

    /// <summary>Equivale a <c>animationsEnabled = !reducedMotion &amp;&amp; !Saver</c> (SDD-20).</summary>
    public bool AmbientEnabled => !ReducedMotion && Profile != PowerProfile.Saver;

    /// <summary>fps da cena: no Saver, <c>min(fps, 15)</c>; <c>reducedMotion</c> não altera o fps do estado.</summary>
    public int SceneFps(int stateFps) =>
        Profile == PowerProfile.Saver ? Math.Min(stateFps, LoopConstants.SaverActiveFps) : stateFps;

    /// <summary>fps do pulso: <c>min(fps da cena, 15)</c>; 0 quando o pulso é estático.</summary>
    public int PulseFps(int stateFps) => AmbientEnabled ? Math.Min(SceneFps(stateFps), PulseMaxFps) : 0;

    /// <summary>Intervalo entre mudanças discretas do piscar (275 ms) ou null se estático.</summary>
    public double? BlinkChangeIntervalSeconds => AmbientEnabled ? AdBlink.StateSeconds : null;

    /// <summary>Intervalo entre passos do bob (125 ms) ou null se estático.</summary>
    public double? BobChangeIntervalSeconds => AmbientEnabled ? AdBob.StateSeconds : null;

    /// <summary>
    /// Com <c>reducedMotion</c> a marcha só muda nos ticks do modelo, sem render intermediário (DP-20.3);
    /// no Saver roda no máximo a 15 fps. Retorna false quando só ticks do modelo movem a marcha.
    /// </summary>
    public bool MarchInterpolates => !ReducedMotion;

    public double Blink(double t) => AmbientEnabled ? AdBlink.Opacity(t) : 1.0;
    public double Bob(double t, double scale = 1.0) => AmbientEnabled ? AdBob.OffsetPx(t, scale) : 0.0;
    public PulseRing Pulse(double t) => AmbientEnabled ? AdPulse.Ring(t) : AdPulse.StaticRing;
}
