using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Rendering.Scene;
using SideWar.Rendering.Text;
using SideWar.Runtime.Td;
using SkiaSharp;
using BuildTower = SideWar.Core.Td.BuildTower;
using UpgradeTower = SideWar.Core.Td.UpgradeTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;
using FreeZone = SideWar.Core.Simulation.FreeZone;
using TowerHost = SideWar.Core.Simulation.TowerHost;

namespace SideWar.Rendering.Tests.Golden;

public enum TdStripScene
{
    Wave,
    PrepRange,
    Crate,
    Unpositioned,
}

/// <summary>
/// Cenas da faixa TD (TD.7, 1200 × 32 DIP: 1 unidade lógica = 1 DIP) montadas pela API pública do Core com um conteúdo de teste que
/// fixa os números (créditos, custos, velocidades, ondas, caixa 100%): os goldens não dependem do balanceamento do JSON. Compostas
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
        TdStripScene.Crate => "td-strip-crate",
        _ => "td-strip-unpositioned",
    };

    public static string GoldenName(TdStripScene scene, int dpi) => $"{Name(scene)}-{dpi * 100 / 96}";

    /// <summary>Cenas com texto (contagem da caixa): golden só no SO de referência (RF-17.3).</summary>
    public static bool HasText(TdStripScene scene) => scene == TdStripScene.Crate;

    private static TdContent TestContent()
    {
        var rules = Embedded.Stages.Rules with
        {
            BombCrate = Embedded.Stages.Rules.BombCrate with { Chance = 1.0, FirstEligibleWaveStage1 = 1, MinDropSeconds = 5, DropMaxSpawnFraction = 0.7 },
        };
        var stage = Embedded.Stages.Stages.First(s => s.Id == 1) with { Waves = 1, StartingCredits = 1_000_000, MaxTowers = 20 };
        TdWaveGroupDef G(string id, int count, double offset) => new(id, count, offset, 1.5);
        var wave = new TdWaveDef(1, false, 1.0, [G("grunt", 1, 0), G("runner", 1, 1), G("flyer", 1, 2), G("armored", 1, 3), G("regen", 1, 4), G("leech", 1, 5), G("ransomware", 1, 6)]);
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

    private static void Build(TdMatchState m, string tower, int x, int upgrades, string? branch = null, int capstone = 0)
    {
        int id = m.Towers.Count + 1;
        Do(m, new BuildTower(tower, x));
        id = m.Towers[^1].Id;
        for (int i = 0; i < upgrades; i++) Do(m, new UpgradeTower(id));
        if (branch is not null) Do(m, new ChooseBranch(id, branch));
        for (int i = 0; i < capstone; i++) Do(m, new UpgradeTower(id));
    }

    private static TdMatchState NewMatch(int width = WidthDip)
    {
        var m = TdMatchState.Create(TestContent(), 1, Rng.Seed(42, 54));
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
        var m = NewMatch();
        switch (scene)
        {
            case TdStripScene.Wave:
                Build(m, "bolt", 150, 2, "A");
                Build(m, "frost", 260, 1);
                Build(m, "cannon", 370, 2, "B", 1);
                Build(m, "balancer", 480, 0);
                Build(m, "scaleset", 590, 1);
                Build(m, "bolt", 700, 0);
                Do(m, new StartWave());
                Steps(m, 30 * 14);
                return (m, TdView.None, 100.0);
            case TdStripScene.PrepRange:
                Build(m, "bolt", 330, 2, "B");
                Build(m, "frost", 440, 2);
                Build(m, "cannon", 880, 1);
                return (m, new TdView(m.Towers[0].Id, 0, false), 100.0);
            case TdStripScene.Crate:
                Build(m, "bolt", 330, 1);
                Build(m, "cannon", 620, 2, "A");
                Build(m, "frost", 900, 0);
                Do(m, new SetPresence(true));
                Do(m, new StartWave());
                for (int i = 0; i < 30 * 40 && !m.CrateActive; i++) Steps(m, 1);
                if (!m.CrateActive) throw new InvalidOperationException("a caixa não caiu");
                Steps(m, 30 * 6); // 2 s restantes: piscando
                return (m, new TdView(0, 0, false), 100.0);
            default:
                Build(m, "bolt", 150, 1);
                Build(m, "frost", 260, 0);
                Build(m, "cannon", 700, 1);
                // a zona esquerda some: as duas primeiras ficam SEM POSIÇÃO
                Do(m, new LayoutChanged(StripLayout.Create(WidthDip, WidthDip - 46, 88, 4, WidthDip - 80, TowerHost.Band, [new FreeZone(600, WidthDip - 32)])));
                return (m, TdView.None, 100.0);
        }
    }

    /// <summary>Renderiza a cena (<paramref name="dpi"/> 96 = 100%, 144 = 150%) como bitmap opaco: faixa de 32 DIP + barra sintética.</summary>
    public static SKBitmap Render(TdStripScene scene, int dpi)
    {
        var (match, view, now) = Build(scene);
        using var typo = new Typography();
        using var renderer = new TdBandRenderer(typo);
        renderer.SetDpi(dpi);
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
