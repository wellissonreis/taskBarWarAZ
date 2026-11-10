using SideWar.Core.Economy;
using Xunit;

namespace SideWar.Core.Tests;

public sealed class RateMeterTests
{
    private static void Ticks(RateMeter meter, int count)
    {
        for (var i = 0; i < count; i++) meter.Tick();
    }

    [Fact(DisplayName = "T-27.12 Empty meter is zero")]
    public void Empty_IsZero()
    {
        var meter = new RateMeter();
        Assert.Equal(0, meter.DisplayedRate);
        Assert.Equal(0, meter.IncomeRate);
    }

    [Fact(DisplayName = "T-27.12 Rate is sum over 10 s window")]
    public void Rate_IsSumDividedByTen()
    {
        var meter = new RateMeter();
        meter.Record(100, 40);
        Assert.Equal(10, meter.DisplayedRate);
        Assert.Equal(4, meter.IncomeRate);
    }

    [Fact(DisplayName = "T-27.12 Gains leave the window after 10 s")]
    public void Window_Expires()
    {
        var meter = new RateMeter();
        meter.Record(100, 100);
        Ticks(meter, 9 * 30 + 29);          // ainda no 10º segundo: balde mantido
        Assert.Equal(10, meter.DisplayedRate);
        meter.Tick();                        // fecha 10 s: balde reciclado
        Assert.Equal(0, meter.DisplayedRate);
        Assert.Equal(0, meter.IncomeRate);
    }

    [Fact(DisplayName = "T-27.12 Bucket boundary at 30 ticks")]
    public void Buckets_RotateEvery30Ticks()
    {
        var meter = new RateMeter();
        meter.Record(10, 10);
        Ticks(meter, 30);
        meter.Record(20, 20);
        Assert.Equal(3, meter.DisplayedRate);
        Ticks(meter, 8 * 30 + 29);
        Assert.Equal(3, meter.DisplayedRate);
        meter.Tick();                        // balde do primeiro registro expira
        Assert.Equal(2, meter.DisplayedRate);
    }

    [Fact(DisplayName = "T-27.12 Two independent series")]
    public void Series_AreIndependent()
    {
        var meter = new RateMeter();
        meter.Record(25, 100);               // Ausente: exibido ×0,25, renda bruta
        Assert.Equal(2.5, meter.DisplayedRate);
        Assert.Equal(10, meter.IncomeRate);
    }

    [Fact(DisplayName = "T-27.12 Paused simulation does not advance")]
    public void NoTicks_NoAdvance()
    {
        var meter = new RateMeter();
        meter.Record(50, 50);
        Assert.Equal(5, meter.DisplayedRate);
        Assert.Equal(5, meter.DisplayedRate);
    }

    [Fact(DisplayName = "T-27.12 Steady income converges to the per-second rate")]
    public void Steady_ConvergesToPerSecond()
    {
        var meter = new RateMeter();
        for (var t = 0; t < 30 * 12; t++)
        {
            meter.Record(1, 2);
            meter.Tick();
        }
        // 12 s de 30/s: janela de 10 s completa
        Assert.Equal(27, meter.DisplayedRate, 6);
        Assert.Equal(54, meter.IncomeRate, 6);
    }

    [Fact(DisplayName = "T-27.12 Reset clears")]
    public void Reset_Clears()
    {
        var meter = new RateMeter();
        meter.Record(10, 10);
        meter.Reset();
        Assert.Equal(0, meter.DisplayedRate);
    }
}
