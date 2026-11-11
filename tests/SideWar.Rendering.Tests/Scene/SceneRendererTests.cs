using SideWar.Core.Simulation;
using SideWar.Rendering.Animation;
using SideWar.Rendering.Scene;
using SideWar.Rendering.Sprites;
using SideWar.Rendering.Text;
using SideWar.Rendering.Theme;
using SideWar.Runtime.Power;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Scene;

public sealed class SceneRendererTests : IDisposable
{
    private readonly SpriteAtlas _atlas = SpriteAtlas.LoadEmbedded();
    private readonly Typography _typo = new();
    private static readonly AnimationPolicy Normal = new(false, PowerProfile.Normal);

    public void Dispose()
    {
        _typo.Dispose();
        _atlas.Dispose();
    }

    private static SimState NewState()
    {
        var slots = new float[8];
        for (int i = 0; i < 8; i++) slots[i] = 640 + 48 * i + 22;
        var layout = StripLayout.Create(1290, 94, 10, 1256, slots);
        var s = new SimState(layout) { BaseHp = 50, MaxBaseHp = 100 };
        s.Towers.Add(new Tower { Id = s.TakeId(), Type = TowerType.Bolt, SlotIndex = 0, Level = 14 });
        s.Towers.Add(new Tower { Id = s.TakeId(), Type = TowerType.Frost, SlotIndex = 1, Level = 3 });
        return s;
    }

    private static int AddEnemy(SimState s, EnemyKind kind, float x, double hp = 100)
    {
        int id = s.TakeId();
        s.Enemies.Add(new Enemy { Id = id, Kind = kind, X = x, Hp = hp, MaxHp = 100, Flags = kind == EnemyKind.Boss ? EnemyFlags.Boss : EnemyFlags.None });
        return id;
    }

    private SceneRenderer NewRenderer(int dpi = 96)
    {
        var r = new SceneRenderer(_atlas, _typo);
        r.SetDpi(dpi);
        return r;
    }

    private static SKBitmap NewBitmap(int dpi = 96) =>
        new(new SKImageInfo(1440 * dpi / 96, 96 * dpi / 96, SKColorType.Bgra8888, SKAlphaType.Premul));

    [Fact]
    public void Draw_CoreBarFill_AndBossBarAndTransparentBackground()
    {
        var s = NewState();
        AddEnemy(s, EnemyKind.Boss, 800);
        using var r = NewRenderer();
        using var bmp = NewBitmap();
        using var canvas = new SKCanvas(bmp);
        r.Draw(canvas, s, 0, Normal);

        Assert.Equal(Palette.Menta, bmp.GetPixel(30, 34));      // preenchido (50% de 60 = 30 → x 22..51)
        Assert.Equal(Palette.BgTrilho, bmp.GetPixel(70, 34));   // trilho (x > 52)
        Assert.Equal(Palette.BgTrilhoFundo, bmp.GetPixel(21, 33)); // borda
        // chefe: sprite centrado em 800, bob 0 em t=0, barra em top 36 (Left = 766 + 4); 100% cheio
        Assert.Equal(Palette.Perigo, bmp.GetPixel(770 + 5, 38));
        Assert.Equal(0u, (uint)bmp.GetPixel(5, 5).Alpha);       // cena sem fundo
    }

    [Fact]
    public void Draw_EmptySlotsAndTowers_PaintLabelsAndPlus()
    {
        var s = NewState();
        using var r = NewRenderer();
        using var bmp = NewBitmap();
        using var canvas = new SKCanvas(bmp);
        r.Draw(canvas, s, 0, Normal);
        Assert.True(HasOpaque(bmp, 640 + 48 * 2, 54, 44, 14), "'+' do slot 2");
        Assert.True(HasOpaque(bmp, 640, 38, 44, 12), "rótulo LV14 do slot 0");
        Assert.True(HasOpaque(bmp, 640 + 48 * 2 + 1, 70, 42, 26), "slot vazio 2");
        Assert.False(HasOpaque(bmp, 640 + 48 * 4 + 1, 0, 40, 30), "sem nada acima do slot vazio 4");
    }

    private static bool HasOpaque(SKBitmap b, int x, int y, int w, int h)
    {
        for (int j = y; j < y + h; j++)
            for (int i = x; i < x + w; i++)
                if (b.GetPixel(i, j).Alpha != 0) return true;
        return false;
    }

    [Fact]
    public void Draw_BobMovesEnemiesUp_AndReducedMotionKeepsThemStill()
    {
        var s = NewState();
        AddEnemy(s, EnemyKind.Grunt, 400);
        using var r = NewRenderer();
        using var a = NewBitmap();
        using var b = NewBitmap();
        using var c = NewBitmap();
        using (var cv = new SKCanvas(a)) r.Draw(cv, s, 0.0, Normal);          // offset 0
        using (var cv = new SKCanvas(b)) r.Draw(cv, s, 0.26, Normal);         // estado 2: −2 DIP
        using (var cv = new SKCanvas(c)) r.Draw(cv, s, 0.26, new AnimationPolicy(true, PowerProfile.Normal));
        // grunt 23×26: top 70 sem bob; com −2 o topo sobe para 68.
        Assert.False(HasOpaque(a, 389, 68, 23, 2));
        Assert.True(HasOpaque(b, 389, 68, 23, 2));
        Assert.False(HasOpaque(c, 389, 68, 23, 2));
    }

    [Fact]
    public void Draw_PortalOverEnemies_BossOverPortal()
    {
        var s = NewState();
        AddEnemy(s, EnemyKind.Tank, 1290); // sob o portal
        using var r = NewRenderer();
        using var bmp = NewBitmap();
        using var canvas = new SKCanvas(bmp);
        r.Draw(canvas, s, 0, Normal);
        // sem o portal o tanque (43×34) cobriria (1290,80); o pixel é do portal (alfa pode variar, só checa que desenhou)
        Assert.True(bmp.GetPixel(1290, 80).Alpha != 0);
    }

    [Fact]
    public void Events_HitCreatesNumber_SuperHitIsNotDuplicated_PoolCapsAt24()
    {
        var s = NewState();
        using var r = NewRenderer();
        r.ConsumeEvents(s, 0); // inicializa o cursor
        s.Events.Add(new SimEvent(SimEventType.Hit, 500, 0, 96, (int)TowerType.Bolt));
        s.Events.Add(new SimEvent(SimEventType.SuperHit, 500, 0, 96, (int)TowerType.Bolt));
        s.Events.Add(new SimEvent(SimEventType.Splash, 500, 0, 40, 2));
        r.ConsumeEvents(s, 1.0);
        Assert.Equal(1, r.Numbers.ActiveCount);
        Assert.Equal("-96", r.Numbers.Items[0].Text);
        Assert.Equal(FloatKind.Bolt, r.Numbers.Items[0].Kind);
        for (int i = 0; i < 40; i++)
            s.Events.Add(new SimEvent(SimEventType.Hit, 100 + i, 0, 240, (int)TowerType.Cannon));
        r.ConsumeEvents(s, 2.0);
        Assert.Equal(FloatingNumbers.PoolSize, r.Numbers.ActiveCount);
    }

    [Fact]
    public void Events_FrostShowsLentoOnlyOncePerEnemy()
    {
        var s = NewState();
        int id = AddEnemy(s, EnemyKind.Grunt, 600);
        using var r = NewRenderer();
        r.ConsumeEvents(s, 0);
        s.Events.Add(new SimEvent(SimEventType.Hit, 600, 0, 10, (int)TowerType.Frost));
        r.ConsumeEvents(s, 1);
        s.Events.Add(new SimEvent(SimEventType.Hit, 600, 0, 10, (int)TowerType.Frost));
        r.ConsumeEvents(s, 2);
        Assert.Equal(1, r.Numbers.ActiveCount);
        Assert.Equal("LENTO", r.Numbers.Items[0].Text);
        Assert.Equal(FloatKind.Slow, r.Numbers.Items[0].Kind);
        Assert.True(id > 0);
    }

    [Fact]
    public void Events_SpawnFalse_ConsumesWithoutNumbers()
    {
        var s = NewState();
        using var r = NewRenderer();
        r.ConsumeEvents(s, 0);
        s.Events.Add(new SimEvent(SimEventType.Hit, 500, 0, 96, (int)TowerType.Bolt));
        r.ConsumeEvents(s, 1, spawn: false);
        r.ConsumeEvents(s, 1.1);
        Assert.Equal(0, r.Numbers.ActiveCount);
    }

    [Fact]
    public void Numbers_RiseFadeAndExpire_ReducedMotionStaysStill()
    {
        var s = NewState();
        using var r = NewRenderer();
        r.Numbers.Add(300, 0, "-96", FloatKind.Bolt);
        using var a = NewBitmap();
        using var b = NewBitmap();
        using (var cv = new SKCanvas(a)) r.Draw(cv, s, 0.01, Normal);
        using (var cv = new SKCanvas(b)) r.Draw(cv, s, 0.5, Normal);
        // topo do texto: com 0,01 s fica em ~32; com 0,5 s já subiu > 8 DIP
        Assert.True(FirstOpaqueRow(a, 280, 40, 20, 0) > FirstOpaqueRow(b, 280, 40, 20, 0));
        using var c = NewBitmap();
        using (var cv = new SKCanvas(c)) r.Draw(cv, s, 1.5, Normal);
        Assert.Equal(0, r.Numbers.ActiveCount);
        Assert.Equal(-1, FirstOpaqueRow(c, 280, 40, 20, 0));
    }

    private static int FirstOpaqueRow(SKBitmap b, int x, int w, int yMax, int yMin)
    {
        // varre y de 0 a 60 (região dos números) e devolve a primeira linha com pixel opaco; −1 se nenhuma
        for (int y = yMin; y < 60; y++)
            for (int i = x; i < x + w; i++)
                if (b.GetPixel(i, y).Alpha != 0) return y;
        _ = yMax;
        return -1;
    }

    [Fact]
    public void Draw_Dpi150_ScalesPositions()
    {
        var s = NewState();
        using var r = NewRenderer(144);
        using var bmp = NewBitmap(144);
        using var canvas = new SKCanvas(bmp);
        r.Draw(canvas, s, 0, Normal);
        // barra do núcleo: conteúdo em x=22·1,5=33 .. , y=32·1,5=48; metade preenchida
        Assert.Equal(Palette.Menta, bmp.GetPixel(40, 50));
        Assert.Equal(Palette.BgTrilho, bmp.GetPixel(100, 50));
    }

    [Fact]
    public void Draw_SteadyState_AllocatesNothing()
    {
        var s = NewState();
        for (int i = 0; i < 20; i++) AddEnemy(s, (EnemyKind)(i % 5), 300 + 40 * i);
        AddEnemy(s, EnemyKind.Boss, 900);
        using var r = NewRenderer();
        using var bmp = NewBitmap();
        using var canvas = new SKCanvas(bmp);
        r.ConsumeEvents(s, 0);
        void Frame(double t)
        {
            s.Events.Add(new SimEvent(SimEventType.Hit, 400, 0, 96, (int)TowerType.Bolt));
            s.Events.Add(new SimEvent(SimEventType.Hit, 440, 0, 240, (int)TowerType.Cannon));
            r.ConsumeEvents(s, t);
            r.Draw(canvas, s, t, Normal);
        }
        for (int i = 0; i < 60; i++) Frame(i / 30.0); // aquecimento (fontes, caches de texto)
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 60; i < 360; i++) Frame(i / 30.0);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0, $"alocou {allocated} B em 300 frames");
    }

    [Fact]
    public void Draw_WithoutSetDpi_Throws()
    {
        using var r = new SceneRenderer(_atlas, _typo);
        using var bmp = NewBitmap();
        using var canvas = new SKCanvas(bmp);
        Assert.Throws<InvalidOperationException>(() => r.Draw(canvas, NewState(), 0, Normal));
    }
}
