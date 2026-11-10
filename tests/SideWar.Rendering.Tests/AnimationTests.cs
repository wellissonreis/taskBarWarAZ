using SideWar.Rendering.Animation;
using SideWar.Runtime.Power;
using Xunit;

namespace SideWar.Rendering.Tests;

public sealed class AnimationTests
{
    [Fact]
    public void MarchIsLinearWrapsAndHonorsDelay()
    {
        Assert.Equal(0, AdMarch.OffsetDip(0, 0, 4), 9);
        Assert.Equal(-590, AdMarch.OffsetDip(13, 0, 4), 9);
        Assert.Equal(-1180.0 * 6.5 / 26, AdMarch.OffsetDip(0, 1, 4), 9); // delay −i×26/n
        Assert.Equal(AdMarch.OffsetDip(1, 2, 5), AdMarch.OffsetDip(27, 2, 5), 9);
        Assert.Equal(-1180.0 / 26 * 1, AdMarch.OffsetDip(1, 0, 1), 9); // ≈ 45,4 DIP/s
        Assert.Throws<ArgumentOutOfRangeException>(() => AdMarch.OffsetDip(0, 0, 0));
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(0.274, 1.0)]
    [InlineData(0.276, 0.725)]
    [InlineData(0.55, 0.45)]
    [InlineData(0.826, 0.725)]
    [InlineData(1.1, 1.0)]
    [InlineData(2.2 + 0.6, 0.45)]
    public void BlinkHasFourDiscreteStates(double t, double expected) =>
        Assert.Equal(expected, AdBlink.Opacity(t), 9);

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.124, 0)]
    [InlineData(0.125, -1)]
    [InlineData(0.25, -2)]
    [InlineData(0.375, -1)]
    [InlineData(0.5, 0)]
    public void BobSequence(double t, int expected) => Assert.Equal(expected, AdBob.OffsetDip(t));

    [Fact]
    public void BobScalesToPixels()
    {
        Assert.Equal(-3, AdBob.OffsetPx(0.25, 1.5) , 9);
        Assert.Equal(-2, AdBob.OffsetPx(0.125, 1.5), 9); // round(−1,5) away from zero
        Assert.Equal(-4, AdBob.OffsetPx(0.25, 2.0), 9);
    }

    [Fact]
    public void PulseRingCurve()
    {
        var a = AdPulse.Ring(0);
        Assert.Equal(0, a.SpreadDip, 9); Assert.Equal(0.7, a.Alpha, 9);
        var m = AdPulse.Ring(0.6);
        Assert.Equal(6, m.SpreadDip, 9); Assert.Equal(0, m.Alpha, 9);
        var h = AdPulse.Ring(0.3); // u = .5 → e = .5
        Assert.Equal(3, h.SpreadDip, 9); Assert.Equal(0.35, h.Alpha, 9);
        var back = AdPulse.Ring(0.9);
        Assert.Equal(h.SpreadDip, back.SpreadDip, 9);
        Assert.Equal(AdPulse.Ring(0.1).SpreadDip, AdPulse.Ring(1.3).SpreadDip, 9);
    }

    [Theory]
    [InlineData(true, PowerProfile.Normal)]
    [InlineData(false, PowerProfile.Saver)]
    [InlineData(true, PowerProfile.Saver)]
    public void ReducedMotionOrSaverFreezeAmbientAnimations(bool reduced, PowerProfile profile)
    {
        var p = new AnimationPolicy(reduced, profile);
        Assert.False(p.AmbientEnabled);
        for (double t = 0; t < 3; t += 0.05)
        {
            Assert.Equal(1.0, p.Blink(t));
            Assert.Equal(0.0, p.Bob(t, 2.0));
            Assert.Equal(AdPulse.StaticRing, p.Pulse(t));
        }
        Assert.Null(p.BlinkChangeIntervalSeconds);
        Assert.Null(p.BobChangeIntervalSeconds);
        Assert.Equal(0, p.PulseFps(30));
    }

    [Fact]
    public void NormalPolicyAnimatesAndCapsFps()
    {
        var p = new AnimationPolicy(false, PowerProfile.Normal);
        Assert.True(p.AmbientEnabled);
        Assert.Equal(0.45, p.Blink(0.55), 9);
        Assert.Equal(-2, p.Bob(0.25));
        Assert.Equal(30, p.SceneFps(30));
        Assert.Equal(15, p.PulseFps(30));
        Assert.Equal(8, p.PulseFps(8));
        Assert.Equal(0.275, p.BlinkChangeIntervalSeconds);
        Assert.Equal(0.125, p.BobChangeIntervalSeconds);
        Assert.True(p.MarchInterpolates);
    }

    [Theory]
    [InlineData(60, 15)]
    [InlineData(30, 15)]
    [InlineData(20, 15)]
    [InlineData(8, 8)]
    [InlineData(4, 4)]
    public void SaverNeverExceeds15Fps(int state, int expected)
    {
        var p = new AnimationPolicy(false, PowerProfile.Saver);
        Assert.Equal(expected, p.SceneFps(state));
        Assert.True(p.MarchInterpolates); // Saver: marcha a ≤ 15 fps
    }

    [Fact]
    public void ReducedMotionKeepsStateFpsAndMarchOnlyByModelTicks()
    {
        var p = new AnimationPolicy(true, PowerProfile.Normal);
        Assert.Equal(30, p.SceneFps(30));
        Assert.False(p.MarchInterpolates);
    }
}
