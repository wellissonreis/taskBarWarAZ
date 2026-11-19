using SideWar.Rendering.Scene;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Golden;

public sealed class TdNewTowerGoldenTests
{
    [Fact]
    public void NewTowersHaveDistinctVectorSprites()
    {
        string[] ids = ["dns", "frontdoor", "shield", "waf", "eventgrid", "bastion", "firewall"];
        var type = typeof(TdArtMap).Assembly.GetType("SideWar.Rendering.Scene.TdVectorSprites")!;
        var bake = type.GetMethod("Bake")!;
        using var bitmap = new SKBitmap(7 * 64, 72);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(20, 25, 32));
        for (int i = 0; i < ids.Length; i++)
        {
            using var sprite = (SKImage)bake.Invoke(null, [ids[i], 56f, false])!;
            canvas.DrawImage(sprite, i * 64 + 6, 8, new SKSamplingOptions(SKFilterMode.Nearest));
        }
        Assert.Null(GoldenImage.Verify("td-torres-39c", bitmap));
    }
}
