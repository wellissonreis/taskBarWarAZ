using SideWar.Core.Simulation;
using SideWar.Platform.Windows.Rendering;
using SideWar.Rendering.Animation;
using SideWar.Rendering.Frame;
using SideWar.Rendering.Scene;
using SideWar.Runtime;
using SideWar.Runtime.Diagnostics;
using SideWar.Runtime.Hosting;
using SideWar.Runtime.Power;
using SideWar.Runtime.Presence;

namespace SideWar.App.Bench;

/// <summary>
/// Destino dos frames do <c>--bench</c> de jogo (T4.11): a mesma cadeia da faixa de produção (<c>SceneRenderer</c> →
/// DIB → <c>UpdateLayeredWindow</c>), mas com compose e present medidos em separado (RF-16.2) e a opacidade global
/// 128 em Ausente / 255 em Presente (SDD-22). Só na T-Game.
/// </summary>
internal sealed class BenchFrameSink(
    LayeredSurface surface, SceneRenderer renderer, BenchStats stats, IClock clock,
    Func<PresenceState> presence, Func<float> barHeightDip) : IGameFrameSink, IDisposable
{
    private readonly SkiaFrameCanvas canvas = new();
    private SimState? last;
    private double lastTime;

    /// <summary>Frames compostos e apresentados.</summary>
    public long Presents { get; private set; }

    public void Present(SimState state, double presentationSeconds)
    {
        last = state;
        lastTime = presentationSeconds;
        if (renderer.Dpi == 0 || surface.Size.IsEmpty) return;
        var edge = presence() switch
        {
            PresenceState.Presente => StripEdgeMode.Present,
            PresenceState.Ausente => StripEdgeMode.Away,
            _ => (StripEdgeMode?)null,
        };
        byte opacity = edge == StripEdgeMode.Away ? (byte)128 : (byte)255;
        if (surface.Opacity != opacity) surface.SetOpacity(opacity);

        long t0 = clock.NowTicks;
        renderer.ConsumeEvents(state, presentationSeconds);
        var frame = surface.BeginFrame();
        renderer.Draw(canvas.Begin(frame), state, presentationSeconds, new AnimationPolicy(false, PowerProfile.Normal), edge, barHeightDip());
        canvas.End();
        long t1 = clock.NowTicks;
        surface.EndFrame(null);
        long t2 = clock.NowTicks;
        double perMs = 1000.0 / clock.TicksPerSecond;
        stats.RecordFrame((t1 - t0) * perMs, (t2 - t1) * perMs);
        Presents++;
    }

    /// <summary>Redesenha o último estado (mudança de DPI/tamanho) sem esperar o próximo deadline.</summary>
    public void Redraw()
    {
        if (last is { } state) Present(state, lastTime);
    }

    public void Invalidate() => canvas.Invalidate();

    public void Dispose() => canvas.Dispose();
}
