using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Rendering.Panel;
using SideWar.Rendering.Text;
using SideWar.Runtime.Td;
using SkiaSharp;
using Xunit;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Rendering.Tests.Golden;

/// <summary>Vista Configurações dentro do menu (engrenagem): uma imagem por seção a 100% e 150%.</summary>
public sealed class TdSettingsGoldenTests
{
    private static readonly SKColor Backdrop = new(0x1b, 0x22, 0x30);

    private sealed class Host : ITdUiHost
    {
        public TdSettingsData Settings { get; private set; } = new() { Sound = true, Volume = 60, AutoStart = true, StartupVisible = true, StartupAvailable = true, StartWithWindows = true, AlertsMutedMask = 1 << 3 };
        public void ApplySettings(TdSettingsData data) => Settings = data;
        public bool Paused => false;
        public void TogglePause() { }
        public void RequestExit() { }
        public bool HudMinimized => false;
        public void SetHudMinimized(bool minimized) { }
        public void StartStage(int stageId) { }
        public int StageCount => 6;
        public bool StageUnlocked(int stageId) => true;
        public int StageStars(int stageId) => 0;
        public bool StageCrown(int stageId) => false;
        public int StageMedals(int stageId) => 0;
        public int StageBestScore(int stageId) => 0;
        public TdRankProgress Rank => TdRank.Progress(0);
        public TdRecordOutcome LastOutcome => default;
        public int SealsAvailable => 0;
        public bool NodePurchased(string nodeId) => false;
        public bool PurchaseNode(string nodeId) => true;
    }

    public static readonly string[] Scenes = ["geral", "geral-rolada", "aparencia", "desempenho", "atalhos", "atalhos-captura", "acessibilidade", "diagnostico"];

    private static TdPopoverView Build(string scene)
    {
        var match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 1, Rng.Seed(7, 54));
        match.Apply(new LayoutChanged(StripLayout.Create(1000, 954, 88, 4, 920, TowerHost.Band, [new FreeZone(32, 968)])));
        var ui = new TdUiController(() => match, c => match.Apply(c), new Host(), () => 0.0);
        ui.Sync();
        int section = scene.Split('-')[0] switch { "geral" => 0, "aparencia" => 1, "desempenho" => 2, "atalhos" => 3, "acessibilidade" => 4, _ => 5 };
        ui.OpenSettingsView(section);
        if (scene == "geral-rolada") ui.ScrollBy(12);
        if (scene == "atalhos-captura")
            ui.Activate(ui.BuildPopover()!.Items.First(i => i.Action == TdUiAction.SettingHotkey && i.SArg == "hk:pause"));
        return ui.BuildPopover()!;
    }

    public static TheoryData<string, int> Cases()
    {
        var d = new TheoryData<string, int>();
        foreach (var s in Scenes)
            foreach (int dpi in new[] { 96, 144 }) d.Add(s, dpi);
        return d;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Settings_MatchesGolden(string scene, int dpi)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "texto Skia difere entre SOs; golden com texto só no Linux (RF-17.3)");
        var p = Build(scene);
        using var typo = new Typography();
        using var r = new TdUiRenderer(typo, dpi) { TextEnabled = true };
        var (pw, ph) = r.PhysicalSize(p.Width, p.Height);
        using var bmp = new SKBitmap(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        c.Clear(Backdrop);
        using var layer = new SKBitmap(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var lc = new SKCanvas(layer)) r.DrawPopover(lc, p, -1);
        GoldenImage.Blit(c, layer, 0, 0);
        string? failure = GoldenImage.Verify($"td-settings-{scene}-{dpi * 100 / 96}", bmp);
        Assert.True(failure is null, failure);
    }
}
