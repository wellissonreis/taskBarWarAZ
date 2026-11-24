using System.Reflection;
using SideWar.Core.Td;
using SideWar.Rendering.Scene;
using SideWar.Rendering.Text;
using SideWar.Runtime.Td;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Golden;

public sealed class TdSpecialBossGoldenTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BossState(bool enraged)
    {
        var (match, _, now) = TdStripScenes.Build(TdStripScene.Wave);
        var enemies = (List<TdEnemy>)typeof(TdMatchState).GetField("_enemies", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(match)!;
        enemies.Clear();
        int index = match.Content.Enemies.Enemies.Length + Array.FindIndex(match.Content.Enemies.Bosses, b => b.Id == "chefe_botnet");
        enemies.Add(new TdEnemy { Id = 1, DefIndex = index, IsBoss = true, Enraged = enraged, X = 600, Hp = enraged ? 50 : 100, MaxHp = 100 });
        using var typo = new Typography();
        using var renderer = new TdBandRenderer(typo);
        renderer.SetDpi(96);
        var (w, h) = renderer.PhysicalSize(TdStripScenes.WidthDip);
        using var bitmap = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bitmap);
        renderer.Draw(canvas, match, now, TdView.None);
        Assert.Null(GoldenImage.Verify(enraged ? "td-special-boss-enraged" : "td-special-boss-normal", bitmap));
    }
}
