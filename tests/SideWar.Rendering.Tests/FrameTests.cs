using System.Runtime.InteropServices;
using SideWar.Rendering.Frame;
using SideWar.Runtime;
using SideWar.Runtime.Geometry;
using SideWar.Runtime.Rendering;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests;

public sealed class FrameTests
{
    private sealed class FakeClock : IClock
    {
        public long NowTicks { get; set; }
        public double TicksPerSecond => 1000;
    }

    /// <summary>Pinned managed buffer posing as a DIB.</summary>
    private sealed class MemorySurface : IOverlaySurface, IDisposable
    {
        private readonly byte[] data;
        private readonly GCHandle pin;
        public MemorySurface(int w, int h) { Size = new(w, h); data = new byte[w * h * 4]; pin = GCHandle.Alloc(data, GCHandleType.Pinned); }
        public PixelSize Size { get; }
        public float Scale => 1f;
        public int Presents;
        public byte[] Data => data;
        public OverlayFrame BeginFrame() => new(pin.AddrOfPinnedObject(), Size.Width, Size.Height, Size.Width * 4);
        public void EndFrame(PixelRect? dirty) => Presents++;
        public void SetOpacity(byte constantAlpha) { }
        public void Dispose() => pin.Free();
    }

    [Fact]
    public void SkiaFrameCanvas_DrawsPremultipliedBgraIntoTheBuffer()
    {
        using var surface = new MemorySurface(8, 8);
        using var adapter = new SkiaFrameCanvas();
        var canvas = adapter.Begin(surface.BeginFrame());
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = new SKColor(255, 0, 0, 128) };
        canvas.DrawRect(0, 0, 8, 8, paint);
        adapter.End();
        // BGRA premultiplied: B=0, G=0, R=alpha*1, A=128.
        Assert.Equal(0, surface.Data[0]);
        Assert.Equal(0, surface.Data[1]);
        Assert.InRange(surface.Data[2], 127, 129);
        Assert.Equal(128, surface.Data[3]);
    }

    [Fact]
    public void SkiaFrameCanvas_CachesTheSurfacePerBufferAndAllocatesNothingInSteadyState()
    {
        using var surface = new MemorySurface(16, 16);
        using var adapter = new SkiaFrameCanvas();
        var first = adapter.Begin(surface.BeginFrame());
        Assert.Same(first, adapter.Begin(surface.BeginFrame()));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) adapter.Begin(surface.BeginFrame());
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        using var other = new MemorySurface(16, 16);
        Assert.NotSame(first, adapter.Begin(other.BeginFrame()));
    }

    [Fact]
    public void TestScene_ProducesOnlyValidPremultipliedPixelsWithPartialAlpha()
    {
        using var surface = new MemorySurface(480, 48);
        using var adapter = new SkiaFrameCanvas();
        using var renderer = new TestSceneRenderer();
        var frame = surface.BeginFrame();
        renderer.Draw(adapter.Begin(frame), frame.Width, frame.Height, 1f, 1.0, 0, 0, 0);
        adapter.End();
        bool partial = false;
        for (int i = 0; i < surface.Data.Length; i += 4)
        {
            byte b = surface.Data[i], g = surface.Data[i + 1], r = surface.Data[i + 2], a = surface.Data[i + 3];
            Assert.True(r <= a && g <= a && b <= a, $"Not premultiplied at {i / 4}: {b},{g},{r},{a}");
            partial |= a is > 0 and < 255;
        }
        Assert.True(partial);
    }

    [Fact]
    public void TestSceneHost_PresentsAt30FpsWithoutBurstingAndRecordsTimings()
    {
        using var surface = new MemorySurface(240, 48);
        using var host = new TestSceneHost();
        var clock = new FakeClock();
        Assert.Null(host.NextDeadline);
        host.OnWake(clock);
        Assert.Equal(0, surface.Presents);
        host.Attach(surface, clock);
        // 2 s wall clock polled every 5 ms.
        for (clock.NowTicks = 0; clock.NowTicks <= 2000; clock.NowTicks += 5) host.OnWake(clock);
        Assert.InRange(surface.Presents, 58, 61);
        Assert.Equal(surface.Presents, host.Timing.Frames);
        // A long stall must not trigger catch-up bursts.
        int before = surface.Presents;
        clock.NowTicks += 1000;
        host.OnWake(clock);
        host.OnWake(clock);
        Assert.Equal(before + 1, surface.Presents);
        host.Shutdown();
        Assert.Null(host.NextDeadline);
    }
}
