using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Rendering.Scene;
using SideWar.Rendering.Text;
using SideWar.Runtime.Td;
using SkiaSharp;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using FreeZone = SideWar.Core.Simulation.FreeZone;
using TowerHost = SideWar.Core.Simulation.TowerHost;

namespace SideWar.Rendering.Tests.Golden;

public enum TdStripScene
{
    Wave,
    PrepRange,
    Unpositioned,
    /// <summary>TD.16b: modo posicionar (borda e véu na faixa, fantasma seguindo o mouse).</summary>
    Placing,
    AllTowers,
    NewEnemies,
}

/// <summary>
/// Cenas da faixa TD (TD.7, 1200 × 32 DIP: 1 unidade lógica = 1 DIP) montadas pela API pública do Core com um conteúdo de teste que
/// fixa os números (créditos, custos, velocidades, ondas): os goldens não dependem do balanceamento do JSON. Compostas
/// sobre o fundo do desktop (#1b2230) e uma barra de 48 DIP sintética logo abaixo (só referência visual; o jogo não a desenha).
/// </summary>
public static class TdStripScenes
{
    public const int WidthDip = 1200;
    public const int BarDip = 48;
    private static readonly SKColor Backdrop = new(0x1b, 0x22, 0x30);
    private static readonly TdContent Embedded = TdContentLoader.LoadEmbedded();

    public static string Name(TdStripScene scene) => scene switch
    {
        TdStripScene.Wave => "td-strip-wave",
        TdStripScene.PrepRange => "td-strip-prep-range",
        TdStripScene.Placing => "td-strip-placing",
        TdStripScene.AllTowers => "td-strip-all-towers",
        TdStripScene.NewEnemies => "td-strip-new-enemies",
        _ => "td-strip-unpositioned",
    };

    public static string BandGoldenName(TdStripScene scene, int bandHeightDip) => $"td-band-{Name(scene)["td-strip-".Length..]}-h{bandHeightDip}-100";

    public static string GoldenName(TdStripScene scene, int dpi) => $"{Name(scene)}-{dpi * 100 / 96}";

    private static TdContent TestContent(bool newEnemies = false)
    {
        var rules = Embedded.Stages.Rules;
        var stage = Embedded.Stages.Stages.First(s => s.Id == 1) with { Waves = 1, StartingCredits = 1_000_000 };
        TdWaveGroupDef G(string id, int count, double offset) => new(id, count, offset, 1.5);
        var wave = new TdWaveDef(1, false, 1.0, [G("grunt", 1, 0), G("runner", 1, 1), G("flyer", 1, 2), G("armored", 1, 3), G("regen", 1, 4), G("leech", 1, 5), G("ransomware", 1, 6)]);
        if (newEnemies) wave = new TdWaveDef(1, false, 1.0, [G("fork", 1, 0), G("trojan", 1, 1), G("phishing", 1, 2)]);
        var enemies = Embedded.Enemies with
        {
            Enemies = [.. Embedded.Enemies.Enemies.Select(e => e with { Speed = 40, Hp = 100 })],
            Bosses = [.. Embedded.Enemies.Bosses.Select(b => b with { Speed = 40, Hp = 1500 })],
        };
        return Embedded with
        {
            Stages = new TdStagesDef(rules, [stage]),
            Waves = new TdWavesDef([new TdStageWavesDef(1, [wave])]),
            Enemies = enemies,
            Towers = [.. Embedded.Towers.Select(t => t with { Cost = 10, UnlockStage = 1 })],
        };
    }

    private static void Do(TdMatchState m, IGameCommand c)
    {
        var r = m.Apply(c);
        if (!r.IsOk) throw new InvalidOperationException($"{c}: {r}");
    }

    private static void Build(TdMatchState m, string tower, int x) => Do(m, new BuildTower(tower, x));

    private static TdMatchState NewMatch(int width = WidthDip, bool newEnemies = false)
    {
        var m = TdMatchState.Create(TestContent(newEnemies), 1, Rng.Seed(42, 54));
        Do(m, new LayoutChanged(StripLayout.Create(width, width - 46, 88, 4, width - 80, TowerHost.Band, [new FreeZone(32, width - 32)])));
        return m;
    }

    private static void Steps(TdMatchState m, int ticks)
    {
        for (int i = 0; i < ticks; i++) m.Step();
        m.ClearEvents();
    }

    public static (TdMatchState Match, TdView View, double Now) Build(TdStripScene scene)
    {
        var m = NewMatch(newEnemies: scene == TdStripScene.NewEnemies);
        switch (scene)
        {
            case TdStripScene.AllTowers:
                for (int i = 0; i < Embedded.Towers.Length; i++) Build(m, Embedded.Towers[i].Id, 80 + i * 72);
                return (m, TdView.None, 100.0);
            case TdStripScene.NewEnemies:
                Do(m, new StartWave());
                Steps(m, 150);
                return (m, TdView.None, 100.0);
            case TdStripScene.Wave:
                Build(m, "bolt", 150);
                Build(m, "frost", 260);
                Build(m, "cannon", 370);
                Build(m, "balancer", 480);
                Build(m, "scaleset", 590);
                Build(m, "bolt", 700);
                Do(m, new StartWave());
                Steps(m, 30 * 14);
                return (m, TdView.None, 100.0);
            case TdStripScene.Placing:
                Build(m, "bolt", 330);
                Build(m, "frost", 440);
                return (m, new TdView(0, 0) { PlacingMode = true, GhostX = 650, GhostValid = true, GhostDef = 0 }, 100.0);
            case TdStripScene.PrepRange:
                Build(m, "bolt", 330);
                Build(m, "frost", 440);
                Build(m, "cannon", 880);
                return (m, new TdView(m.Towers[0].Id, 0), 100.0);
            default:
                Build(m, "bolt", 150);
                Build(m, "frost", 260);
                Build(m, "cannon", 700);
                // a zona esquerda some: as duas primeiras ficam SEM POSIÇÃO
                Do(m, new LayoutChanged(StripLayout.Create(WidthDip, WidthDip - 46, 88, 4, WidthDip - 80, TowerHost.Band, [new FreeZone(600, WidthDip - 32)])));
                return (m, TdView.None, 100.0);
        }
    }

    /// <summary>Renderiza a cena (<paramref name="dpi"/> 96 = 100%, 144 = 150%) como bitmap opaco: faixa de 32 DIP + barra sintética.</summary>
    public static SKBitmap Render(TdStripScene scene, int dpi, int bandHeightDip = TdBand.HeightDip)
    {
        var (match, view, now) = Build(scene);
        using var typo = new Typography();
        using var renderer = new TdBandRenderer(typo);
        renderer.SetDpi(dpi);
        renderer.SetBandHeight(bandHeightDip); // TD.14: arte do espaço de 32 DIP escalada por altura / 32
        var (bw, bh) = renderer.PhysicalSize(WidthDip);
        using var band = new SKBitmap(new SKImageInfo(bw, bh, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var bc = new SKCanvas(band)) renderer.Draw(bc, match, now, view);

        int barPx = BarDip * dpi / 96;
        var bmp = new SKBitmap(new SKImageInfo(bw, bh + barPx, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(Backdrop);
        using var bar = new SKPaint { Color = new SKColor(0x20, 0x21, 0x24) };
        canvas.DrawRect(0, bh, bw, barPx, bar);
        GoldenImage.Blit(canvas, band, 0, 0);
        return bmp;
    }
}
