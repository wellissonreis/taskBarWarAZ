using SideWar.Core.Td;
using SideWar.Rendering.Panel;
using SideWar.Rendering.Scene;
using SideWar.Rendering.Text;
using SideWar.Runtime.Td;
using SkiaSharp;
using Xunit;

namespace SideWar.Rendering.Tests.Golden;

/// <summary>Regressão: o app não abria porque a HUD desenhava um rótulo vazio (SKTextBlob.Create devolve null).</summary>
public sealed class TdHudEmptyTextTests
{
    private sealed class Host : ITdUiHost
    {
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
        public TdRankProgress Rank => TdRank.Progress(2381);
        public TdRecordOutcome LastOutcome => default;
        public int SealsAvailable => 0;
        public bool NodePurchased(string nodeId) => false;
        public bool PurchaseNode(string nodeId) => true;
        public TdGameOptions GameOptions { get; set; } = new();
        public double EffectiveSpeed { get; set; } = 1;
    }

    /// <summary>Settings do relato: gameSpeed 3, extraSpeeds ligado, extraSpeedMask 138; também 10x e Máx.</summary>
    [Theory(Timeout = 20000)]
    [InlineData(1, 1.0)]
    [InlineData(3, 3.0)]
    [InlineData(10, 10.0)]
    [InlineData(10, 4.0)]
    [InlineData(TdSpeed.Max, 0.0)]
    public async Task Hud_with_extra_speeds_draws_without_empty_text(int speed, double effective)
    {
        await Task.Yield();
        var (match, _, _) = TdStripScenes.Build(TdStripScene.PrepRange);
        var host = new Host { GameOptions = new TdGameOptions(ExtraSpeeds: true, SpeedMask: 138, GameSpeed: speed), EffectiveSpeed = effective };
        var ui = new TdUiController(() => match, c => match.Apply(c), host, () => 0.0, null);
        ui.Sync();
        var hud = ui.BuildHud();
        Assert.DoesNotContain(hud.Items, i => i.Kind == TdUiKind.Label && i.SArg == "speedeff" && i.Text.Length == 0);
        using var typo = new Typography();
        using var renderer = new TdUiRenderer(typo, 120);
        using var bitmap = new SKBitmap(1600, 200);
        using var canvas = new SKCanvas(bitmap);
        renderer.DrawHud(canvas, hud, -1);
        // Mesmo um item vazio explícito não pode derrubar o desenho.
        renderer.DrawHud(canvas, hud with { Items = [.. hud.Items, new TdUiItem(new(0, 0, 40, 22), TdUiKind.Label, "", SArg: "speedeff")] }, -1);
    }
}
