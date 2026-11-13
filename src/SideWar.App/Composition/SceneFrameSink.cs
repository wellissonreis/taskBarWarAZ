using SideWar.Core.Simulation;
using SideWar.Platform.Windows.Rendering;
using SideWar.Rendering.Animation;
using SideWar.Rendering.Frame;
using SideWar.Rendering.Scene;
using SideWar.Runtime.Hosting;

namespace SideWar.App.Composition;

/// <summary>
/// Destino dos frames da <c>RenderPolicy</c> (T4.10, SDD-08 RF-08.x): <see cref="SceneRenderer"/> → DIB →
/// <c>UpdateLayeredWindow</c>. Só na T-Game. Antes de <see cref="Attach"/> (ou com a superfície liberada/vazia) não
/// desenha nada: o laço lógico segue e o próximo frame pedido redesenha tudo.
/// </summary>
internal sealed class SceneFrameSink : IGameFrameSink, IDisposable
{
    private readonly SkiaFrameCanvas canvas = new();
    private readonly Func<AnimationPolicy> policy;
    private readonly Func<StripEdgeMode?> edge;
    private readonly Func<float> barHeightDip;
    private LayeredSurface? surface;
    private SceneRenderer? renderer;

    public SceneFrameSink(Func<AnimationPolicy> policy, Func<StripEdgeMode?> edge, Func<float> barHeightDip)
    {
        this.policy = policy;
        this.edge = edge;
        this.barHeightDip = barHeightDip;
    }

    /// <summary>Frames realmente compostos e apresentados (contador de Presents, SDD-08 CA-08.4).</summary>
    public long Presents { get; private set; }

    /// <summary>Último estado apresentado (para o frame forçado antes do Show, SDD-07 RF-07.6 passo 4).</summary>
    public SimState? Last { get; private set; }

    public double LastTime { get; private set; }

    public void Attach(LayeredSurface target, SceneRenderer sceneRenderer)
    {
        surface = target;
        renderer = sceneRenderer;
    }

    public void Present(SimState state, double presentationSeconds)
    {
        Last = state;
        LastTime = presentationSeconds;
        if (surface is null || renderer is null || renderer.Dpi == 0 || surface.Size.IsEmpty) return;
        renderer.ConsumeEvents(state, presentationSeconds);
        var frame = surface.BeginFrame();
        renderer.Draw(canvas.Begin(frame), state, presentationSeconds, policy(), edge(), barHeightDip());
        canvas.End();
        surface.EndFrame(null);
        Presents++;
    }

    /// <summary>Redesenha o último estado (Show após Hide, mudança de DPI) sem esperar o próximo deadline.</summary>
    public void Redraw()
    {
        if (Last is { } state) Present(state, LastTime);
    }

    /// <summary>A superfície foi liberada ou redimensionada: o próximo BeginFrame recria o canvas.</summary>
    public void Invalidate() => canvas.Invalidate();

    public void Dispose() => canvas.Dispose();
}
