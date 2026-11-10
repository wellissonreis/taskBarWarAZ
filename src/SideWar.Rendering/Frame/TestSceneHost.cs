using SideWar.Core;
using SideWar.Runtime;
using SideWar.Runtime.Rendering;

namespace SideWar.Rendering.Frame;

/// <summary>Composition/present timings of the test frame (CA-08.2); seconds.</summary>
public readonly record struct FrameTiming(long Frames, double ComposeMs, double PresentMs, double ComposeTotalMs, double PresentTotalMs);

/// <summary>
/// Minimal <see cref="IGameLoopHost"/> that presents <see cref="TestSceneRenderer"/> at a fixed 30 fps (RF-08.9,
/// SDD-03 RF-03.4). All members run on T-Game. Call <see cref="Attach"/> on T-Game once the surface exists.
/// </summary>
public sealed class TestSceneHost : IGameLoopHost, IDisposable
{
    public const int Fps = 30;
    private readonly SkiaFrameCanvas canvas = new();
    private readonly TestSceneRenderer renderer = new();
    private IOverlaySurface? surface;
    private long? nextFrame;
    private long startTicks;
    private long frames;
    private double composeMs, presentMs, composeTotal, presentTotal;

    public long? NextDeadline => nextFrame;
    public FrameTiming Timing => new(Volatile.Read(ref frames), composeMs, presentMs, composeTotal, presentTotal);

    public void Attach(IOverlaySurface target, IClock clock)
    {
        surface = target;
        startTicks = clock.NowTicks;
        nextFrame = startTicks;
    }

    public void OnWake(IClock clock)
    {
        if (surface is null || nextFrame is not { } due) return;
        long now = clock.NowTicks;
        if (now < due) return;
        double perMs = 1000.0 / clock.TicksPerSecond;
        var frame = surface.BeginFrame();
        var skia = canvas.Begin(frame);
        renderer.Draw(skia, frame.Width, frame.Height, surface.Scale, (now - startTicks) / clock.TicksPerSecond,
            frames, composeMs, presentMs);
        canvas.End();
        long composed = clock.NowTicks;
        surface.EndFrame(null);
        long presented = clock.NowTicks;
        composeMs = (composed - now) * perMs;
        presentMs = (presented - composed) * perMs;
        composeTotal += composeMs;
        presentTotal += presentMs;
        Interlocked.Increment(ref frames);
        long step = (long)(clock.TicksPerSecond / Fps);
        nextFrame = Math.Max(due + step, now + step / 2); // Never burst to catch up (no 2x, ADR-031).
    }

    public void OnCommand(IGameCommand command) { }

    public void Shutdown()
    {
        surface = null;
        nextFrame = null;
        Dispose();
    }

    public void Dispose()
    {
        canvas.Dispose();
        renderer.Dispose();
    }
}
