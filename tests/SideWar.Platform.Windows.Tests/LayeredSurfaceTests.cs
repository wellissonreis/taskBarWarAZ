using System.Runtime.InteropServices;
using SideWar.Platform.Windows.Rendering;
using SideWar.Platform.Windows.Threading;
using SideWar.Rendering.Frame;
using SideWar.Runtime;
using SideWar.Runtime.Geometry;
using Xunit;

namespace SideWar.Platform.Windows.Tests;

[Trait("Env", "WindowsInteractive")]
public sealed class LayeredSurfaceTests
{
    private static Task<T> OnGame<T>(GameThread game, Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        game.Post(() => { try { tcs.SetResult(func()); } catch (Exception e) { tcs.SetException(e); } });
        return tcs.Task;
    }

    private sealed class StopwatchClock : IClock
    {
        public long NowTicks => System.Diagnostics.Stopwatch.GetTimestamp();
        public double TicksPerSecond => System.Diagnostics.Stopwatch.Frequency;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Surface_ExposesTopDownBgraBitsAndPresentsWithPartialAndFullUpdates()
    {
        using var host = new TestSceneHost();
        var game = new GameThread(host, e => throw e, _ => { });
        await game.StartAsync();
        try
        {
            var result = await OnGame(game, () =>
            {
                game.Scene.SetBounds(new PixelRect(0, 0, 320, 48));
                game.Scene.Show();
                using var surface = new LayeredSurface(game.Scene.Handle);
                surface.Resize(new PixelSize(320, 48));
                var frame = surface.BeginFrame();
                Assert.Equal((320, 48, 1280), (frame.Width, frame.Height, frame.RowBytes));
                Assert.NotEqual(0, frame.Pixels);
                Marshal.WriteInt32(frame.Pixels, 0, unchecked((int)0x80400000)); // Top-left.
                Marshal.WriteInt32(frame.Pixels, (320 * 47 + 319) * 4, unchecked((int)0xFF112233)); // Bottom-right: top-down layout.
                surface.EndFrame(null);
                var again = surface.BeginFrame();
                Assert.Equal(frame.Pixels, again.Pixels); // Content kept across frames (RF-08.1).
                Assert.Equal(unchecked((int)0x80400000), Marshal.ReadInt32(again.Pixels, 0));
                Assert.Equal(unchecked((int)0xFF112233), Marshal.ReadInt32(again.Pixels, (320 * 47 + 319) * 4));
                surface.EndFrame(new PixelRect(0, 0, 10, 10)); // Partial update.
                surface.SetOpacity(128); // Re-present without redraw.
                surface.Resize(new PixelSize(160, 24)); // Release + lazy recreate.
                Assert.False(surface.HasBuffer);
                var small = surface.BeginFrame();
                Assert.Equal((160, 24), (small.Width, small.Height));
                surface.EndFrame(null);
                surface.Release();
                Assert.False(surface.HasBuffer);
                return surface.Opacity;
            }).WaitAsync(TimeSpan.FromSeconds(5), Token);
            Assert.Equal(128, result);
        }
        finally { await game.StopAsync(); }
    }

    [Fact]
    public async Task TestScene_AnimatesAt30FpsOnTheSceneWindow()
    {
        using var host = new TestSceneHost();
        var game = new GameThread(host, e => throw e, _ => { });
        await game.StartAsync();
        LayeredSurface? surface = null;
        try
        {
            await OnGame(game, () =>
            {
                game.Scene.SetBounds(new PixelRect(0, 0, 1280, 96));
                surface = new LayeredSurface(game.Scene.Handle);
                surface.Resize(new PixelSize(1280, 96));
                game.Scene.Show();
                host.Attach(surface, new StopwatchClock());
                return 0;
            }).WaitAsync(TimeSpan.FromSeconds(5), Token);
            await Task.Delay(500, Token);
            long start = host.Timing.Frames;
            await Task.Delay(3000, Token);
            double fps = (host.Timing.Frames - start) / 3.0;
            Assert.InRange(fps, 27, 32);
            var timing = host.Timing;
            Assert.True(timing.ComposeMs > 0 && timing.PresentMs >= 0);

            // Premultiplied alpha: no pixel may have a color channel above its alpha (halo-free edges).
            var verdict = await OnGame(game, () =>
            {
                var frame = surface!.BeginFrame();
                bool valid = true, partial = false;
                for (int i = 0; i < frame.Width * frame.Height; i++)
                {
                    uint px = unchecked((uint)Marshal.ReadInt32(frame.Pixels, i * 4));
                    byte a = (byte)(px >> 24);
                    valid &= (byte)px <= a && (byte)(px >> 8) <= a && (byte)(px >> 16) <= a;
                    partial |= a is > 0 and < 255;
                }
                surface.EndFrame(null);
                return (valid, partial);
            }).WaitAsync(TimeSpan.FromSeconds(5), Token);
            Assert.True(verdict.valid);
            Assert.True(verdict.partial);
            var line = FormattableString.Invariant($"TestScene 30fps: measured {fps:F1} fps, compose {timing.ComposeMs:F3} ms (avg {timing.ComposeTotalMs / timing.Frames:F3}), present {timing.PresentMs:F3} ms (avg {timing.PresentTotalMs / timing.Frames:F3}), frames {timing.Frames}");
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "sidewar-testscene.txt"), line);
        }
        finally
        {
            await game.StopAsync();
            await Task.Yield();
        }
    }
}
