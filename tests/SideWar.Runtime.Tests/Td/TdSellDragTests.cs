using SideWar.Core;
using SideWar.Core.Content.Td;
using SideWar.Core.Simulation;
using SideWar.Core.Td;
using SideWar.Runtime.Td;
using Xunit;
using BuildTower = SideWar.Core.Td.BuildTower;
using LayoutChanged = SideWar.Core.Td.LayoutChanged;

namespace SideWar.Runtime.Tests.Td;

/// <summary>Venda por arrasto: soltar uma torre colocada fora da faixa (acima) vende; mover e fundir seguem iguais.</summary>
public sealed class TdSellDragTests
{
    private sealed class Host : ITdUiHost
    {
        public bool Paused => false;
        public void TogglePause() { }
        public void RequestExit() { }
        public bool HudMinimized { get; set; } = true;
        public void SetHudMinimized(bool minimized) => HudMinimized = minimized;
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

    private readonly TdMatchState _match;
    private readonly List<IGameCommand> _sent = [];
    private readonly TdUiController _ui;
    private readonly int _tower;
    private readonly float _x;
    private const float Above = -(TdUiController.SellDragMarginDip + 10f);

    public TdSellDragTests()
    {
        _match = TdMatchState.Create(TdContentLoader.LoadEmbedded(), 6, Rng.Seed(7, 54), null, TdMatchOptions.Classic with { InitialReputation = TdRank.Threshold((TdRankLevel)5) });
        _match.Apply(new LayoutChanged(StripLayout.Create(1000, 954, 88, 4, 920, TowerHost.Band, [new FreeZone(32, 968)])));
        typeof(TdMatchState).GetProperty("Credits")!.SetValue(_match, 5000L);
        _ui = new TdUiController(() => _match, c => { _sent.Add(c); _match.Apply(c); }, new Host(), () => 0.0, null);
        Assert.True(_match.Apply(new BuildTower("frost", 500)).IsOk);
        _tower = _match.Towers[0].Id;
        _ui.Sync();
        _x = TdBand.TowerCenterDip(_match, _match.Towers[0], TdBand.WidthDip(_match));
    }

    [Fact]
    public void Dragging_above_the_band_shows_the_refund_indicator_and_dropping_sells()
    {
        long refund = _match.SellValue(_tower);
        long credits = _match.Credits;
        Assert.True(_ui.BeginTowerDrag(_tower, _x));
        _ui.PointerMoved(_x, Above);
        Assert.True(_ui.DragSellArmed);
        Assert.Equal(refund, _ui.View.SellRefund);
        Assert.True(refund > 0);
        Assert.Equal(-1, _ui.View.GhostX); // sem fantasma de mover
        _ui.SceneLeftUp(_x, Above);
        Assert.Contains(_sent, c => c is SellTower s && s.TowerId == _tower);
        Assert.Empty(_match.Towers);
        Assert.Equal(credits + refund, _match.Credits);
        Assert.Equal(-1, _ui.View.SellRefund);
        Assert.False(_ui.DraggingTower);
    }

    [Fact]
    public void Returning_inside_cancels_the_indicator_and_dropping_does_not_sell()
    {
        _ui.BeginTowerDrag(_tower, _x);
        _ui.PointerMoved(_x, Above);
        Assert.True(_ui.View.SellRefund > 0);
        _ui.PointerMoved(_x - 120f, 10f);
        Assert.Equal(-1, _ui.View.SellRefund);
        Assert.False(_ui.DragSellArmed);
        _ui.SceneLeftUp(_x - 120f, 10f);
        Assert.DoesNotContain(_sent, c => c is SellTower);
        Assert.Single(_match.Towers);
        Assert.Contains(_sent, c => c is RelocateTower); // continua sendo mover
    }

    [Fact]
    public void Dropping_on_the_band_edge_without_leaving_never_sells()
    {
        _ui.BeginTowerDrag(_tower, _x);
        _ui.PointerMoved(_x + 30f, -(TdUiController.SellDragMarginDip - 1f));
        Assert.Equal(-1, _ui.View.SellRefund);
        _ui.SceneLeftUp(_x + 30f, -(TdUiController.SellDragMarginDip - 1f));
        Assert.DoesNotContain(_sent, c => c is SellTower);
    }

    [Fact]
    public void Escape_or_cancelled_capture_never_sells_and_a_stray_release_does_nothing()
    {
        _ui.BeginTowerDrag(_tower, _x);
        _ui.PointerMoved(_x, Above);
        _ui.Cancel();
        Assert.Equal(-1, _ui.View.SellRefund);
        _ui.SceneLeftUp(_x, Above);
        Assert.DoesNotContain(_sent, c => c is SellTower);
        Assert.Single(_match.Towers);
    }

    [Fact]
    public void Without_a_drag_in_progress_a_release_or_move_above_never_sells()
    {
        _ui.PointerMoved(_x, Above);
        _ui.SceneLeftUp(_x, Above);
        Assert.DoesNotContain(_sent, c => c is SellTower);
        Assert.Single(_match.Towers);
    }

    [Fact]
    public void Boss_key_during_the_drag_never_sells()
    {
        _ui.BeginTowerDrag(_tower, _x);
        _ui.PointerMoved(_x, Above);
        _ui.SetBossHidden(true);
        _ui.SceneLeftUp(_x, Above);
        Assert.DoesNotContain(_sent, c => c is SellTower);
        Assert.Single(_match.Towers);
        Assert.Equal(-1, _ui.View.SellRefund);
    }

    [Fact]
    public void Tutorial_and_help_mention_dragging_out_to_sell()
    {
        Assert.Contains(TdTutorial.Pages, p => p.Text.Contains("para fora da faixa para vender"));
    }
}
